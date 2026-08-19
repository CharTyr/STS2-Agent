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
    private readonly SettingsStore _store = new();
    private readonly List<ChatTurn> _history = new();
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _playCts;
    private Task? _playTask;
    private AgentSettings _settings;
    private readonly AgentLoop _loop;
    private bool _playRunning;
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
        });
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

    public bool PlayRunning => _playRunning;

    public string Status => _status;

    public string LastAction => _lastAction;

    public string LastThought => _lastThought;

    public string DualStatus => _dualStatus;

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
        if (_playRunning)
        {
            return;
        }

        _playRunning = true;
        SetStatus("自动游玩中");
        var previous = _playTask;
        _playTask = Task.Run(() => RestartPlayLoopAsync(previous));
    }

    public void StopAutoPlay()
    {
        _playRunning = false;
        try
        {
            _playCts?.Cancel();
        }
        catch
        {
        }

        SetStatus("已暂停自动游玩");
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

    public Task LaunchDualInstanceAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => LaunchDualInstanceCoreAsync(cancellationToken), cancellationToken);
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

        if (_playRunning)
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
        if (_playRunning)
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

    private async Task LaunchDualInstanceCoreAsync(CancellationToken cancellationToken)
    {
        if (InstanceRole.IsCompanion)
        {
            _dualStatus = "当前已是同伴实例。";
            RaiseChanged();
            return;
        }

        _dualStatus = "正在启动第二实例…";
        RaiseChanged();
        try
        {
            _dualStatus = await DualInstanceCoordinator.HostLocalCoopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _dualStatus = "双开失败：" + ex.Message;
        }

        RaiseChanged();
    }

    private async Task CompanionEntryAsync(CancellationToken cancellationToken)
    {
        var joined = await DualInstanceCoordinator.RunCompanionBootstrapAsync(cancellationToken);
        if (!joined)
        {
            SetStatus("同伴实例加入大厅失败");
            return;
        }
        var autoPlay = Environment.GetEnvironmentVariable("STS2_AGENT_AUTOPLAY");
        if (string.Equals(autoPlay, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(autoPlay, "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(autoPlay))
        {
            StartAutoPlay();
        }
    }

    private async Task RestartPlayLoopAsync(Task? previous)
    {
        try
        {
            _playCts?.Cancel();
        }
        catch
        {
        }

        if (previous != null)
        {
            try
            {
                await previous;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Warn($"{LogPrefix} Previous auto-play loop ended with: {ex.Message}");
            }
        }

        if (!_playRunning)
        {
            return;
        }

        _playCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        await AutoPlayLoopAsync(_playCts.Token);
    }

    private async Task AutoPlayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _playRunning)
        {
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

                if (cancellationToken.IsCancellationRequested || !_playRunning)
                {
                    break;
                }

                ApplyPlayResult(result);
                if (result.Error != null)
                {
                    await Task.Delay(1200, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LogPrefix} Auto-play step failed: {ex.Message}");
                SetStatus("自动游玩出错：" + ex.Message);
                await Task.Delay(1500, cancellationToken);
            }
        }

        _playRunning = false;
        RaiseChanged();
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
