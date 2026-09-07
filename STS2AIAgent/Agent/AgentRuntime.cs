using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
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
    private readonly object _playLifecycleGate = new();
    private long _playGeneration;
    private PlaySessionIdentity? _playSessionIdentity;
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
    private string _mcpStatus = "MCP 已关闭，未对外暴露。";
    private LlmUsage _sessionUsage = LlmUsage.Empty;
    private int _sessionRequests;
    private bool _sessionUsageKnown;
    private string? _stopKind;
    private string? _stopDetail;
    private string? _stopRole;
    private bool _waitingForGame;
    private bool _waitingForPlayer;
    private bool _requestingModel;
    private readonly List<string> _diagnosticEvents = new();
    private SessionBudgetGuard _budgetGuard;

    public static AgentRuntime Instance => LazyInstance.Value;

    public event Action? Changed;

    private AgentRuntime()
    {
        _settings = _store.Load();
        _budgetGuard = _settings.CreateBudgetGuard();
        _loop = new AgentLoop(new GameBridge(), new DefaultLlmClientFactory(), () =>
        {
            lock (_gate)
            {
                return _settings;
            }
        }, InstanceRole.IsCompanion ? () => _teamConversation.BuildDecisionContext() : null,
            () =>
            {
                lock (_gate)
                {
                    return _budgetGuard;
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
            if (LocalDualInstanceLauncher.CompanionProcessExited)
            {
                throw new InvalidOperationException("队友进程已退出。请回到主菜单重新邀请。");
            }

            var connection = LocalDualInstanceLauncher.Connection ?? throw new InvalidOperationException("请先邀请 AI 队友。");
            if (running)
            {
                var firstRun = FirstRunSetup.Evaluate(Settings);
                if (!firstRun.ReadyToInvite)
                {
                    throw new InvalidOperationException(firstRun.Hint);
                }
            }

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
                NoteEvent(Status);
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

    public LlmUsage SessionUsage
    {
        get { lock (_gate) return _sessionUsage; }
    }

    public int SessionRequests
    {
        get { lock (_gate) return _sessionRequests; }
    }

    public void ResetSessionStats()
    {
        lock (_gate)
        {
            _sessionUsage = LlmUsage.Empty;
            _sessionRequests = 0;
            _sessionUsageKnown = false;
            _budgetGuard = _settings.CreateBudgetGuard();
        }
        RaiseChanged();
    }

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
        string? budgetBlocked;
        lock (_gate)
        {
            budgetBlocked = _budgetGuard.CheckBudget();
        }

        if (budgetBlocked != null)
        {
            throw new InvalidOperationException(budgetBlocked);
        }

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
            AccountTurn(result, recordBudget: true);
            RaiseChanged();
            return reply;
        }
        finally
        {
            _turnGate.Release();
        }
    }

    public string McpStatus => _mcpStatus;

    public string? McpUrl => NativeMcpServer.Runtime?.EndpointUrl;

    public bool McpRunning => NativeMcpServer.Runtime?.Enabled == true;

    public string McpClientConfig => NativeMcpServer.FormatClientConfigJson(McpUrl ?? McpEndpointUrl());

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
        NativeMcpServer.BindRuntime(new GameBridge(), Router.BuildHealthData, Router.ModVersion);
        ApplyMcpFromSettings();
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
        NativeMcpServer.Runtime?.SetEnabled(false, McpEndpointUrl());
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
            _budgetGuard = settings.CreateBudgetGuard(_sessionUsage.TotalTokens, _sessionRequests);
        }

        _store.Save(settings);
        ApplyMcpFromSettings();
        RaiseChanged();
    }

    public void PersistOverlayVisible(bool visible)
    {
        lock (_gate)
        {
            _settings.OverlayVisibleOnStart = visible;
            _settings.HasSeenFirstRunGuide = true;
            _store.Save(_settings);
        }
    }

    public void MarkFirstRunGuideSeen()
    {
        lock (_gate)
        {
            if (_settings.HasSeenFirstRunGuide)
            {
                return;
            }

            _settings.HasSeenFirstRunGuide = true;
            _store.Save(_settings);
        }
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
            _budgetGuard = loaded.CreateBudgetGuard(_sessionUsage.TotalTokens, _sessionRequests);
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
        return Task.Run(() => TestConnectionCoreAsync(force: true, cancellationToken), cancellationToken);
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

        Task task;
        PlaySessionIdentity identity;
        lock (_playLifecycleGate)
        {
            var started = _playSession.TryStart(AutoPlayLoopAsync, _lifetime.Token);
            if (started == null) return;

            task = started;

            identity = new PlaySessionIdentity(++_playGeneration, task);
            _playSessionIdentity = identity;
            _stopKind = null;
            _stopDetail = null;
            _stopRole = null;
            _waitingForGame = false;
            _waitingForPlayer = false;
            _requestingModel = true;
        }

        SetStatus("自动游玩中");
        _ = ObservePlayCompletionAsync(task, identity);
    }

    public void StopAutoPlay()
    {
        if (InstanceRole.IsCompanion) _companionAutoStartSuppressed = true;
        var task = _playSession.RequestPause();
        SetStatus(task.IsCompleted ? "已暂停自动游玩" : "正在暂停，等待当前任务完成…");
        NoteEvent(Status);
    }

    public void SetMcpEnabled(bool enabled)
    {
        lock (_gate)
        {
            _settings.McpEnabled = enabled;
            _store.Save(_settings);
        }

        ApplyMcpFromSettings();
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

        string? budgetBlocked = null;
        lock (_gate)
        {
            budgetBlocked = _budgetGuard.CheckBudget();
        }

        if (budgetBlocked != null)
        {
            AddHistory("user", text);
            AddHistory("assistant", budgetBlocked);
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

            AccountTurn(result, recordBudget: true);

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

    private async Task<string> TestConnectionCoreAsync(bool force, CancellationToken cancellationToken)
    {
        SetStatus("正在测试模型…会向配置的服务发送测试请求。");
        NoteEvent(Status);
        try
        {
            var results = await _loop.TestConfiguredRolesAsync(force, cancellationToken);
            AgentSettings settings;
            lock (_gate)
            {
                settings = _settings;
            }

            foreach (var item in results)
            {
                ModelRoleProbe.Upsert(settings, item.Record);
            }

            SaveSettings(settings);
            var play = results.First(item => item.Role == ModelRoleNames.Play);
            var summary = string.Join(" ", results.Select(item => ModelRoleProbe.FormatLine(item.Record)));
            if (play.Record.Status == "failed")
            {
                SetModelTestFailure(play.Record);
                SetStatus("游玩模型测试失败");
            }
            else if (play.Record.Status == "verified")
            {
                ClearModelTestFailure();
                SetStatus("游玩模型连通成功（不等于工具/视觉已验证）");
                MarkFirstRunGuideSeen();
            }
            else
            {
                SetStatus("模型尚未验证");
            }

            NoteEvent(summary);
            return summary;
        }
        catch (Exception ex)
        {
            SetStatus("连通失败");
            ClassifyStop(ex.Message, ModelRoleNames.Play);
            return DiagnosticExport.Redact(ex.Message);
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
            var error = CoopLaunchPolicy.GetError(InstanceRole.IsCompanion, PlayRunning, screen, settings);
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

    private async Task ObservePlayCompletionAsync(Task task, PlaySessionIdentity identity)
    {
        try
        {
            await task;
            lock (_playLifecycleGate)
            {
                if (!IsCurrentPlaySessionLocked(identity)) return;

                _requestingModel = false;
                if (PlayPhase == "paused" && _stopKind == null) SetStatus("已暂停自动游玩");
            }
        }
        catch (Exception ex)
        {
            lock (_playLifecycleGate)
            {
                if (!IsCurrentPlaySessionLocked(identity)) return;

                Log.Warn($"{LogPrefix} Auto-play session ended: {ex.Message}");
                ClassifyStop(ex.Message, ModelRoleNames.Play);
                _requestingModel = false;
                if (PlayPhase == "paused") SetStatus("自动游玩已停止：" + DiagnosticExport.Redact(ex.Message));
                NoteEvent("stop " + (_stopKind ?? "failed") + ": " + ex.Message);
            }
        }
    }

    private async Task AutoPlayLoopAsync(CancellationToken cancellationToken)
    {
        var boundary = new CurrentRunBoundary();
        SessionBudgetGuard budgetGuard;
        lock (_gate)
        {
            budgetGuard = _budgetGuard;
        }

        try
        {
            await AutoPlayRecovery.RunAsync(async token =>
            {
                await _turnGate.WaitAsync(token);
                try
                {
                    _requestingModel = true;
                    RaiseChanged();
                    var immediate = await TryCompanionImmediateAsync(token);
                    if (immediate != null)
                    {
                        return immediate;
                    }

                    SetStatus("正在请求模型…");
                    return await _loop.PlayOnceAsync(token, boundary.Check);
                }
                finally
                {
                    _requestingModel = false;
                    _turnGate.Release();
                }

            }, ApplyPlayResult, cancellationToken, delay: null, budgetGuard: budgetGuard);
        }
        finally { RaiseChanged(); }
    }

    private async Task<AgentTurnResult?> TryCompanionImmediateAsync(CancellationToken cancellationToken)
    {
        var followMapVotes = InstanceRole.IsCompanion;
        var decision = await GameThread.InvokeAsync(() =>
        {
            var payload = GameStateService.BuildStatePayload();
            var mapOptions = payload.map?.available_nodes
                .Select(node => new CompanionMapOption(node.index, node.vote_count, node.has_local_vote))
                .ToArray();
            return CompanionPlayPolicy.DecideImmediate(
                payload.screen,
                payload.available_actions,
                followMapVotes ? mapOptions : null,
                payload.modal?.type_name,
                payload.modal?.can_confirm == true,
                payload.modal?.can_dismiss == true,
                payload.in_combat,
                followMapVotes);
        });

        if (decision.Kind == CompanionImmediateDecision.Wait)
        {
            await Task.Delay(400, cancellationToken);
            return new AgentTurnResult
            {
                Reasoning = "等待你选择地图节点，随后投同一格。",
                WaitingForGame = true,
                ToolRounds = 0,
                RequestsSpent = 0
            };
        }

        if (decision.Kind != CompanionImmediateDecision.Act || string.IsNullOrWhiteSpace(decision.Action))
        {
            return null;
        }

        var acted = decision.Action;
        var json = await GameThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await GameActionService.ExecuteAsync(new ActionRequest
            {
                action = acted,
                option_index = decision.OptionIndex,
                client_context = new { source = "companion_follow", instance_role = InstanceRole.Current }
            });
            return response.message ?? response.action;
        });

        var reasoning = string.Equals(acted, "confirm_modal", StringComparison.OrdinalIgnoreCase)
            ? "确认阻挡操作的教学弹窗。"
            : "跟随你的地图选择。";
        return new AgentTurnResult
        {
            Acted = acted,
            ActResultJson = json,
            Reasoning = reasoning,
            ToolRounds = 0
        };
    }

    private void ApplyMcpFromSettings()
    {
        var enabled = Settings.McpEnabled;
        var url = McpEndpointUrl();
        NativeMcpServer.Runtime?.SetEnabled(enabled, url);
        _mcpStatus = enabled
            ? "MCP 已打开。把下面的地址或配置贴进外部客户端。"
            : "MCP 已关闭，未对外暴露。";
    }

    private static string McpEndpointUrl()
    {
        return HttpServer.Instance.Prefix.TrimEnd('/') + "/mcp";
    }

    private void AccountTurn(AgentTurnResult result, bool recordBudget = false)
    {
        lock (_gate)
        {
            if (result.Usage != null)
            {
                _sessionUsage = LlmUsage.Combine(_sessionUsage, result.Usage) ?? LlmUsage.Empty;
                _sessionUsageKnown = true;
            }

            _sessionRequests += Math.Max(0, result.RequestsSpent);
            if (recordBudget)
            {
                _budgetGuard.Observe(result);
            }
        }
    }

    private void ApplyPlayResult(AgentTurnResult result)
    {
        AccountTurn(result);
        _waitingForGame = result.WaitingForGame;
        _waitingForPlayer = result.Reasoning != null && result.Reasoning.Contains("等待你", StringComparison.Ordinal);
        _requestingModel = false;

        if (!string.IsNullOrWhiteSpace(result.Acted))
        {
            _lastAction = result.Acted;
        }

        _lastThought = result.Reasoning ?? result.AssistantText ?? _lastThought;
        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            AddHistory("assistant", result.AssistantText);
        }

        if (result.RequiresConfiguration)
        {
            ClassifyStop(result.Error ?? "配置错误", ModelRoleNames.Play);
        }

            SetStatus(result.Error == null
            ? (result.Acted != null ? "已执行 " + result.Acted : result.WaitingForGame ? "等待游戏可操作" : "等待可操作状态")
            : DiagnosticExport.Redact(result.Error));
    }

    public PlayerFacingView PlayerFacing()
    {
        string? budget;
        lock (_gate)
        {
            budget = _budgetGuard.CheckBudget();
        }

        return PlayerFacingSession.Compose(new PlayerFacingSnapshot
        {
            FirstRun = FirstRunSetup.Evaluate(Settings),
            PlayPhase = PlayPhase,
            PlayRunning = PlayRunning,
            Status = Status,
            DualLaunching = DualLaunching,
            DualStatus = DualStatus,
            TeamControlPending = TeamControlPending,
            TeamControlStatus = TeamControlStatus,
            CompanionConnected = LocalDualInstanceLauncher.Connection != null,
            CompanionProcessAlive = LocalDualInstanceLauncher.CompanionProcessAlive,
            CompanionProcessExited = LocalDualInstanceLauncher.CompanionProcessExited,
            WaitingForGame = _waitingForGame,
            WaitingForPlayer = _waitingForPlayer,
            RequestingModel = _requestingModel || Status.Contains("请求模型", StringComparison.Ordinal),
            FinishingSubmittedAction = PlayPhase == "stopping",
            StopKind = _stopKind,
            StopDetail = _stopDetail,
            UsageKnown = _sessionUsageKnown,
            SessionUsage = SessionUsage,
            SessionRequests = SessionRequests,
            BudgetReason = budget,
            IsCompanion = InstanceRole.IsCompanion
        });
    }

    public string ExportDiagnostics()
    {
        IReadOnlyList<string> events;
        lock (_gate)
        {
            events = _diagnosticEvents.ToArray();
        }

        return DiagnosticExport.Render(new DiagnosticSnapshot
        {
            ModVersion = Router.ModVersion,
            Role = InstanceRole.Current,
            PlayPhase = PlayPhase,
            Status = Status,
            DualStatus = DualStatus,
            TeamControlStatus = TeamControlStatus,
            StopKind = _stopKind,
            StopDetail = _stopDetail,
            ApiPrefix = HttpServer.Instance.Prefix,
            McpUrl = McpUrl,
            McpEnabled = McpRunning,
            UsageKnown = _sessionUsageKnown,
            SessionRequests = SessionRequests,
            SessionTokens = _sessionUsageKnown ? SessionUsage.TotalTokens : null,
            RecentEvents = events,
            RecentRequestIds = Router.RecentRequestIds(),
            Settings = Settings
        });
    }

    public bool SessionUsageKnown
    {
        get { lock (_gate) return _sessionUsageKnown; }
    }

    private void SetModelTestFailure(ModelRoleTestRecord record)
    {
        _stopKind = ModelRoleProbe.FailureKind(record.StatusCode, record.Error);
        _stopDetail = DiagnosticExport.Redact(record.Error);
        _stopRole = record.Role;
    }

    private void ClearModelTestFailure()
    {
        if (PlayerFacingSession.ShouldClearModelTestFailure(_stopKind, _stopRole, ModelRoleNames.Play))
        {
            _stopKind = null;
            _stopDetail = null;
            _stopRole = null;
        }
    }

    private static string ClassifyStopKind(string? message)
    {
        message ??= string.Empty;
        if (message.Contains("预算", StringComparison.Ordinal) || message.Contains("上限", StringComparison.Ordinal))
        {
            return "budget";
        }

        if (message.Contains("当前局", StringComparison.Ordinal) || message.Contains("对局", StringComparison.Ordinal))
        {
            return "run_end";
        }

        if (message.Contains("请检查模型", StringComparison.Ordinal) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("402", StringComparison.Ordinal) ||
            message.Contains("403", StringComparison.Ordinal) ||
            message.Contains("404", StringComparison.Ordinal) ||
            message.Contains("422", StringComparison.Ordinal) ||
            message.Contains("认证失败", StringComparison.Ordinal) ||
            message.Contains("配置错误", StringComparison.Ordinal))
        {
            return "config";
        }

        if (message.Contains("408", StringComparison.Ordinal) ||
            message.Contains("429", StringComparison.Ordinal) ||
            message.Contains("超时", StringComparison.Ordinal) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Name or service", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "network";
        }

        return "failed";
    }

    private void ClassifyStop(string message, string? role = null)
    {
        _stopKind = ClassifyStopKind(message);
        _stopDetail = DiagnosticExport.Redact(message);
        _stopRole = role;
    }

    private bool IsCurrentPlaySessionLocked(PlaySessionIdentity identity)
    {
        return PlayerFacingSession.IsCurrentPlaySession(_playSessionIdentity, identity);
    }

    private void NoteEvent(string line)
    {
        lock (_gate)
        {
            _diagnosticEvents.Add(DateTimeOffset.UtcNow.ToString("HH:mm:ss") + " " + DiagnosticExport.Redact(line));
            if (_diagnosticEvents.Count > 24)
            {
                _diagnosticEvents.RemoveRange(0, _diagnosticEvents.Count - 24);
            }
        }
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

    public void NotifyStatus(string status) => SetStatus(status);

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
