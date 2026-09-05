using System.Diagnostics;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed class AgentRuntime
{
    private const string LogPrefix = "[STS2AIAgent.Runtime]";

    private static readonly Lazy<AgentRuntime> LazyInstance = new(() => new AgentRuntime());

    private readonly object _gate = new();
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly SemaphoreSlim _dualLaunchGate = new(1, 1);
    private volatile bool _dualLaunching;
    private readonly SettingsStore _store = new();
    private readonly List<ChatTurn> _history = new();
    private readonly TeamConversation _teamConversation = new();
    private readonly SemaphoreSlim _teamMessageGate = new(1, 1);
    private volatile bool _teamMessagePending;
    private string _teamStatus = "组队后，可以在这里和 AI 队友商量打法。";
    private CancellationTokenSource _lifetime = new();
    private readonly AutoPlaySession _playSession = new();
    private readonly SemaphoreSlim _companionControlGate = new(1, 1);
    private readonly SemaphoreSlim _remoteControlGate = new(1, 1);
    private volatile bool _teamControlPending;
    private string _teamControlStatus = "队友控制尚未连接。";
    private volatile bool _companionReady;
    private volatile bool _companionAutoStartSuppressed;
    private AgentSettings _settings;
    private readonly AgentLoop _loop;
    private string _status = "就绪";
    private string _lastAction = "-";
    private string _lastThought = "-";
    private string _dualStatus = "尚未启动双开。";
    private string _mcpStatus = "MCP 未启动。外部 Agent 可走下方接入说明。";
    private string? _mcpUrl;
    private Process? _mcpProcess;

    public static AgentRuntime Instance => LazyInstance.Value;

    public event Action? Changed;

    private AgentRuntime()
    {
        _settings = _store.Load();
        _loop = new AgentLoop(new GameBridge(), new DefaultLlmClientFactory(), () =>
        {
            lock (_gate)
            {
                return _settings;
            }
        }, InstanceRole.IsCompanion ? () => _teamConversation.BuildDecisionContext() : null);
    }

    public AgentSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public bool PlayRunning => _playSession.IsActive;
    public string PlayPhase => _playSession.Phase;
    public bool TeamControlPending => _teamControlPending;
    public string TeamControlStatus => _teamControlStatus;

    public async Task ControlTeammateAsync(bool running, CancellationToken cancellationToken)
    {
        if (!await _remoteControlGate.WaitAsync(0, cancellationToken)) return;
        _teamControlPending = true;
        _teamControlStatus = running ? "正在请求队友继续…" : "正在等待队友暂停；已提交的动作会先完成。";
        RaiseChanged();
        try
        {
            if (_dualLaunching) throw new InvalidOperationException("请等待组队完成。");
            var connection = LocalDualInstanceLauncher.Connection ?? throw new InvalidOperationException("请先邀请 AI 队友。");
            var phase = await connection.ControlAsync(running, cancellationToken);
            _teamControlStatus = phase switch
            {
                "paused" => "队友已暂停。仍然可以聊天，点击继续后才会自动行动。",
                "running" => "队友正在自动游玩。",
                _ => "队友仍在停止当前任务，请稍后再次确认暂停。"
            };
        }
        catch (Exception ex)
        {
            _teamControlStatus = "未确认队友控制结果：" + ex.Message;
        }
        finally
        {
            _teamControlPending = false;
            _remoteControlGate.Release();
            RaiseChanged();
        }
    }

    public async Task<string> SetCompanionRunningAsync(bool running, CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion can receive control requests.");
        await _companionControlGate.WaitAsync(cancellationToken);
        try
        {
            _companionAutoStartSuppressed = true;
            if (running)
            {
                if (!_companionReady) throw new InvalidOperationException("队友尚未完成组队，请稍后继续。");
                StartAutoPlay();
            }
            else
            {
                var stopping = _playSession.RequestPause();
                SetStatus(stopping.IsCompleted ? "已暂停自动游玩" : "正在暂停，等待当前任务完成…");
                await stopping.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (PlayPhase == "paused") SetStatus("已暂停自动游玩");
            }
            return PlayPhase;
        }
        finally { _companionControlGate.Release(); }
    }

    public string Status => _status;

    public string LastAction => _lastAction;

    public string LastThought => _lastThought;

    public string DualStatus => _dualStatus;
    public bool DualLaunching => _dualLaunching;
    public bool TeamMessagePending => _teamMessagePending;
    public string TeamStatus => _teamStatus;
    public IReadOnlyList<ChatTurn> TeamHistory => _teamConversation.Snapshot();

    public async Task SendTeamMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (!await _teamMessageGate.WaitAsync(0, cancellationToken)) return;
        _teamMessagePending = true;
        try
        {
            if (_dualLaunching) throw new InvalidOperationException("正在组队，请等待连接完成后发送消息。");
            var connection = LocalDualInstanceLauncher.Connection
                ?? throw new InvalidOperationException("请先邀请 AI 队友。此处消息只发送给本次邀请的队友。");
            _teamConversation.Add("user", text);
            _teamStatus = "消息正在送往队友；若它正在行动，会在本次行动完成后回复。";
            RaiseChanged();
            var reply = await connection.SendMessageAsync(text, cancellationToken);
            _teamConversation.Add("assistant", reply.Length > TeamConversation.MaxMessageLength ? reply[..TeamConversation.MaxMessageLength] : reply);
            _teamStatus = "队友已回复。你的建议会作为后续决策的参考。";
        }
        catch (Exception ex)
        {
            _teamStatus = "队伍消息未确认完成：" + ex.Message;
        }
        finally
        {
            _teamMessagePending = false;
            _teamMessageGate.Release();
            RaiseChanged();
        }
    }

    public async Task<string> ReplyToTeammateAsync(string text, CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion can receive team messages.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        cancellationToken = deadline.Token;
        // Share the turn gate with gameplay, but do not require autoplay to be
        // stopped: this request only reads state and adds conversational context.
        await _turnGate.WaitAsync(cancellationToken);
        try
        {
            var previous = _teamConversation.Snapshot();
            _teamConversation.Add("user", text);
            var result = await _loop.ChatAsync(text, previous, new ChatOptions
            {
                TeammateConversation = true,
                AttachState = true
            }, cancellationToken);
            if (result.Error != null) throw new InvalidOperationException(result.Error);
            if (string.IsNullOrWhiteSpace(result.AssistantText))
                throw new InvalidOperationException("队友未返回文本回复；建议已记录供后续决策参考。");
            var reply = result.AssistantText;
            if (reply.Length > TeamConversation.MaxMessageLength) reply = reply[..TeamConversation.MaxMessageLength];
            _teamConversation.Add("assistant", reply);
            RaiseChanged();
            return reply;
        }
        finally
        {
            _turnGate.Release();
        }
    }

    public string McpStatus => _mcpStatus;

    public string? McpUrl => _mcpUrl;

    public bool McpRunning
    {
        get
        {
            try
            {
                return _mcpProcess is { HasExited: false };
            }
            catch
            {
                return false;
            }
        }
    }

    public string SettingsPath => _store.Path;

    public IReadOnlyList<ChatTurn> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public void Initialize()
    {
        AppendLog($"API {Server.HttpServer.Instance.Prefix}  role={InstanceRole.Current}");
        if (InstanceRole.IsCompanion)
        {
            SetStatus("同伴实例：正在加入大厅");
            _ = Task.Run(() => CompanionEntryAsync(_lifetime.Token));
        }
    }

    public void Shutdown()
    {
        StopAutoPlay();
        StopMcp();
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
        }
    }

    public void SaveSettings(AgentSettings settings)
    {
        settings.EnsureValidShape();
        lock (_gate)
        {
            _settings = settings;
        }

        _store.Save(settings);
        SetStatus("设置已保存");
        RaiseChanged();
    }

    public void PersistOverlayPlacement(float? left, float? top)
    {
        lock (_gate)
        {
            _settings.OverlayLeft = left;
            _settings.OverlayTop = top;
            _store.Save(_settings);
        }
    }

    public void PersistChatAttachFlags(bool attachState, bool attachScreenshot)
    {
        lock (_gate)
        {
            _settings.AttachStateInChat = attachState;
            _settings.AttachScreenshotInChat = attachScreenshot;
            _store.Save(_settings);
        }
    }

    public AgentSettings ReloadSettings()
    {
        var loaded = _store.Load();
        lock (_gate)
        {
            _settings = loaded;
        }

        RaiseChanged();
        return loaded;
    }

    public Task SendChatAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        bool allowAct,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => SendChatCoreAsync(text, attachState, attachScreenshot, allowAct, cancellationToken), cancellationToken);
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => TestConnectionCoreAsync(cancellationToken), cancellationToken);
    }

    public void StartAutoPlay()
    {
        if (_dualLaunching)
        {
            SetStatus("正在组队，请等待 AI 队友连接完成。");
            return;
        }

        if (PlayRunning)
        {
            return;
        }

        var task = _playSession.TryStart(AutoPlayLoopAsync, _lifetime.Token);
        if (task == null) return;
        SetStatus("自动游玩中");
        _ = ObservePlayCompletionAsync(task);
    }

    public void StopAutoPlay()
    {
        if (InstanceRole.IsCompanion) _companionAutoStartSuppressed = true;
        var task = _playSession.RequestPause();
        SetStatus(task.IsCompleted ? "已暂停自动游玩" : "正在暂停，等待当前任务完成…");
    }

    public Task StartMcpAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => StartMcpCoreAsync(cancellationToken), cancellationToken);
    }

    public void StopMcp()
    {
        McpProcessLauncher.TryStop(_mcpProcess);
        _mcpProcess = null;
        _mcpUrl = null;
        _mcpStatus = "MCP 已停止。";
        RaiseChanged();
    }

    public Task StepOnceAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => StepOnceCoreAsync(cancellationToken), cancellationToken);
    }

    public Task LaunchDualInstanceAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        return Task.Run(() => LaunchDualInstanceCoreAsync(settings, cancellationToken), cancellationToken);
    }

    public void ClearChat()
    {
        lock (_gate)
        {
            _history.Clear();
        }

        RaiseChanged();
    }

    private async Task SendChatCoreAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        bool allowAct,
        CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (PlayRunning)
        {
            AddHistory("assistant", "自动游玩进行中。请先暂停，再对话或代打。");
            return;
        }

        var prior = History;
        AddHistory("user", text);
        SetStatus("正在请求模型…");
        try
        {
            await _turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            try
            {
                result = await _loop.ChatAsync(
                text,
                prior,
                new ChatOptions
                {
                    AttachState = attachState,
                    AttachScreenshot = attachScreenshot,
                    AllowAct = allowAct
                },
                cancellationToken);
            }
            finally
            {
                _turnGate.Release();
            }

            var reply = result.Error != null
                ? result.Error
                : string.IsNullOrWhiteSpace(result.AssistantText) ? "(无文本回复)" : result.AssistantText;
            AddHistory("assistant", reply);
            _lastThought = result.Reasoning ?? reply;
            if (!string.IsNullOrWhiteSpace(result.Acted))
            {
                _lastAction = result.Acted;
            }

            SetStatus(result.Error == null ? "对话完成" : "对话出错");
        }
        catch (OperationCanceledException)
        {
            SetStatus("对话已取消");
        }
        catch (Exception ex)
        {
            AddHistory("assistant", "请求失败：" + ex.Message);
            SetStatus("对话失败");
        }
    }

    private async Task<string> TestConnectionCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("正在测试连通…");
        try
        {
            var reply = await _loop.TestConnectionAsync(cancellationToken);
            SetStatus("连通正常");
            AddHistory("assistant", "连通测试：" + reply);
            return reply;
        }
        catch (Exception ex)
        {
            SetStatus("连通失败");
            return ex.Message;
        }
    }

    private async Task StepOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (PlayRunning)
        {
            SetStatus("自动游玩中，请先暂停再单步");
            return;
        }

        SetStatus("单步决策中");
        try
        {
            await _turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            try
            {
                result = await _loop.PlayOnceAsync(cancellationToken);
            }
            finally
            {
                _turnGate.Release();
            }

            ApplyPlayResult(result);
        }
        catch (OperationCanceledException)
        {
            SetStatus("单步已取消");
        }
        catch (Exception ex)
        {
            SetStatus("单步失败：" + ex.Message);
        }
    }

    private async Task LaunchDualInstanceCoreAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        if (!await _dualLaunchGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        _dualLaunching = true;
        var previousConnection = LocalDualInstanceLauncher.Connection;
        _dualStatus = "正在检查组队条件…";
        RaiseChanged();
        try
        {
            if (_teamMessagePending || _teamControlPending)
            {
                _dualStatus = "请等待当前队伍消息完成，再重新组队。";
                return;
            }
            var screen = await new GameBridge().GetScreenAsync(cancellationToken);
            var error = CoopLaunchPolicy.GetError(InstanceRole.IsCompanion, PlayRunning, screen, settings.TryResolvePlayModel());
            if (error != null)
            {
                _dualStatus = error;
                return;
            }

            // The child reads settings at startup. Persist the edited model
            // selection before launching so both windows use the same choices.
            SaveSettings(settings);
            _dualStatus = "正在邀请 AI 队友，等待游戏窗口连接…";
            RaiseChanged();
            _dualStatus = await DualInstanceCoordinator.HostLocalCoopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _dualStatus = "已取消等待队友连接；若队友窗口已打开，请在该窗口确认状态。";
        }
        catch (Exception ex)
        {
            _dualStatus = "邀请队友失败：" + ex.Message;
        }
        finally
        {
            if (!ReferenceEquals(previousConnection, LocalDualInstanceLauncher.Connection))
            {
                _teamConversation.Clear();
                _teamStatus = "队伍对话已重置。确认队友连接后，可以商量这次冒险的打法。";
            }
            _dualLaunching = false;
            _dualLaunchGate.Release();
            RaiseChanged();
        }
    }

    private async Task CompanionEntryAsync(CancellationToken cancellationToken)
    {
        var joined = await DualInstanceCoordinator.RunCompanionBootstrapAsync(cancellationToken);
        if (!joined)
        {
            SetStatus("同伴实例加入大厅失败");
            return;
        }
        await _companionControlGate.WaitAsync(cancellationToken);
        try
        {
            _companionReady = true;
            var autoPlay = Environment.GetEnvironmentVariable("STS2_AGENT_AUTOPLAY");
            if (!_companionAutoStartSuppressed &&
                (string.Equals(autoPlay, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoPlay, "true", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(autoPlay)))
            {
                StartAutoPlay();
            }
        }
        finally { _companionControlGate.Release(); }
    }

    private async Task ObservePlayCompletionAsync(Task task)
    {
        try
        {
            await task;
            if (PlayPhase == "paused") SetStatus("已暂停自动游玩");
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Auto-play session ended: {ex.Message}");
            if (PlayPhase == "paused") SetStatus("自动游玩已停止：" + ex.Message);
        }
    }

    private async Task AutoPlayLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AutoPlayRecovery.RunAsync(async token =>
            {
                await _turnGate.WaitAsync(token);
                try
                {
                    return await _loop.PlayOnceAsync(token);
                }
                finally
                {
                    _turnGate.Release();
                }

            }, ApplyPlayResult, cancellationToken);
        }
        finally { RaiseChanged(); }
    }

    private async Task StartMcpCoreAsync(CancellationToken cancellationToken)
    {
        if (McpRunning)
        {
            _mcpStatus = "MCP 已在运行：" + (_mcpUrl ?? "");
            RaiseChanged();
            return;
        }

        string configured;
        int preferredPort;
        lock (_gate)
        {
            configured = _settings.McpServerPath;
            preferredPort = _settings.McpPort;
        }

        var root = McpProcessLauncher.FindMcpRoot(configured);
        if (root == null)
        {
            _mcpStatus = "未找到 mcp_server。把 release 包里的 mcp_server 放到游戏目录旁，或在接入页填写路径。";
            RaiseChanged();
            return;
        }

        lock (_gate)
        {
            _settings.McpServerPath = root;
            _store.Save(_settings);
        }

        _mcpStatus = "正在启动 MCP…";
        RaiseChanged();
        try
        {
            var api = HttpServer.Instance.Prefix.TrimEnd('/');
            var launched = await McpProcessLauncher.StartAsync(root, api, preferredPort, cancellationToken);
            if (!launched.Ok)
            {
                _mcpStatus = launched.Message;
                RaiseChanged();
                return;
            }

            _mcpProcess = launched.Process;
            _mcpUrl = launched.Url;
            _mcpStatus = launched.Message;
        }
        catch (OperationCanceledException)
        {
            _mcpStatus = "MCP 启动已取消。";
        }
        catch (Exception ex)
        {
            _mcpStatus = "MCP 启动失败：" + ex.Message;
        }

        RaiseChanged();
    }

    private void ApplyPlayResult(AgentTurnResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Acted))
        {
            _lastAction = result.Acted;
        }

        _lastThought = result.Reasoning ?? result.AssistantText ?? _lastThought;
        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            AddHistory("assistant", result.AssistantText);
        }

        SetStatus(result.Error == null
            ? (result.Acted != null ? "已执行 " + result.Acted : "等待可操作状态")
            : result.Error);
    }

    private void AddHistory(string role, string text)
    {
        lock (_gate)
        {
            _history.Add(new ChatTurn { Role = role, Text = text });
            if (_history.Count > 80)
            {
                _history.RemoveRange(0, _history.Count - 80);
            }
        }

        RaiseChanged();
    }

    private void AppendLog(string line)
    {
        Log.Info($"{LogPrefix} {line}");
    }

    private void SetStatus(string status)
    {
        _status = status;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
    }
}
