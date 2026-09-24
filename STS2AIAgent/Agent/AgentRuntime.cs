using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

/// <summary>
/// Outcome of a teammate start/pause request. <see cref="Phase"/> is the companion's own play phase
/// ("running", "paused", "stopping") or a transport-level word ("busy", "refused"); <see cref="Ok"/>
/// is the field to branch on, because <see cref="Message"/> is localized display text.
/// </summary>
internal readonly record struct TeammateControlResult(bool Ok, string Phase, string Message);

internal sealed partial class AgentRuntime
{
    private const string LogPrefix = "[STS2AIAgent.Runtime]";

    /// <summary>How often the overlay's tick may ask for a fresh teammate summary.</summary>
    private const long TeammateStatusRefreshMs = 2000;

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
    private string? _teamStatus;
    private CancellationTokenSource _lifetime = new();
    private readonly AutoPlaySession _playSession = new();
    private CurrentRunBoundary _runBoundary = new();
    private readonly object _playLifecycleGate = new();
    private long _playGeneration;
    private PlaySessionIdentity? _playSessionIdentity;
    private readonly SemaphoreSlim _companionControlGate = new(1, 1);
    private readonly SemaphoreSlim _remoteControlGate = new(1, 1);
    private volatile bool _teamControlPending;
    private string? _teamControlStatus;
    private volatile bool _companionReady;
    private volatile bool _companionAutoStartSuppressed;
    private volatile bool _companionAutoPlay = true;
    private AgentSettings _settings;
    private readonly AgentLoop _loop;
    private string? _status;
    private string _lastAction = "-";
    private string _lastThought = "-";
    private string? _dualStatus;
    private volatile DualLaunchOutcome _dualLaunchOutcome = DualLaunchOutcome.Idle;
    private string? _mcpStatus;
    private LlmUsage _sessionUsage = LlmUsage.Empty;
    private int _sessionRequests;
    private bool _sessionUsageKnown;
    private int _lastPromptTokens;
    private string? _stopKind;
    private string? _stopDetail;
    private string? _stopRole;
    private bool _waitingForGame;
    private bool _waitingForPlayer;
    private bool _requestingModel;
    private volatile bool _requestingModelStatus;
    private readonly List<string> _diagnosticEvents = new();
    private readonly DecisionLog _decisions = new(
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(SettingsStore.DefaultPath()) ?? ".",
            "decisions.jsonl"));
    private SessionBudgetGuard _budgetGuard;
    private string? _proactiveSituationKey;
    private readonly ProactiveChatSession _proactiveChat = new();
    private string? _teammateLiveStatus;
    private long _teammateStatusAtMs;
    private volatile bool _teammateStatusRefreshing;

    public static AgentRuntime Instance => LazyInstance.Value;

    public event Action? Changed;

    private AgentRuntime()
    {
        _settings = _store.Load();
        _decisions.Recorded += OnSessionDecision;
        _strategyStore.Changed += MarkSessionDirty;
        _budgetGuard = _settings.CreateBudgetGuard();
        _loop = new AgentLoop(new GameBridge(), new DefaultLlmClientFactory(), () =>
        {
            lock (_gate)
            {
                return _settings;
            }
        }, InstanceRole.IsCompanion ? () => _teamConversation.BuildTeamContext() : null,
            () =>
            {
                lock (_gate)
                {
                    return _budgetGuard;
                }
            },
            // The memory provider prefers a continued run's restored decisions over the live log.
            RecentDecisionsForMemory,
            () =>
            {
                lock (_gate)
                {
                    return _lastPromptTokens;
                }
            },
            // The dual-layer engine: the decider is resolved live on every turn, because the Jev
            // configuration typically appears after this runtime is constructed -- a decider captured
            // here is frozen to the pre-settings state and the toggle never turns on. The store is
            // the same instance the MCP route and the in-game planner write, which is what makes the
            // overlay path and the MCP path one experience.
            deciderProvider: ResolveJevDecider,
            strategyStore: _strategyStore,
            confidenceThreshold: () =>
            {
                lock (_gate)
                {
                    return _settings.JevConfidenceThreshold;
                }
            },
            peekPlayInstruction: PeekPlayInstruction,
            acknowledgePlayInstruction: AcknowledgePlayInstruction,
            // Streamed partials go straight back to the runtime; the overlay reads the accumulated
            // text on its own tick, and the buffer decides whether the player asked to see any of it.
            onReasoningDelta: ReportReasoningDelta);
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

    /// <summary>
    /// False when the running teammate was launched for an external agent instead of for auto-play,
    /// which is what an unverified play model selects. Reported on /health so a caller can tell
    /// "paused on purpose, waiting for me" from "stopped by itself".
    /// </summary>
    public bool CompanionAutoPlay => _companionAutoPlay;

    // The idle wording is resolved on read rather than stored, so switching the game language
    // updates it too. Once real progress arrives, the recorded text takes over.
    public string TeamControlStatus => _teamControlStatus ?? Loc.T("队友控制尚未连接。");

    public async Task ControlTeammateAsync(bool running, CancellationToken cancellationToken)
    {
        await ControlTeammateResultAsync(running, cancellationToken);
    }

    /// <summary>
    /// Asks the teammate window to start or pause. The structured result exists for
    /// <c>POST /teammate/control</c>: a caller outside this process cannot read the localized
    /// <see cref="TeamControlStatus"/> to tell a confirmed pause from a refused one.
    /// </summary>
    public async Task<TeammateControlResult> ControlTeammateResultAsync(bool running, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (running ? !TryBeginTeammateStart() : !_remoteControlGate.Wait(0))
        {
            return new TeammateControlResult(false, "busy", Loc.T("上一次队友控制还没有完成，请稍后重试。"));
        }

        _teamControlPending = true;
        _teamControlStatus = running ? Loc.T("正在请求队友继续…") : Loc.T("正在等待队友暂停；已提交的动作会先完成。");
        RaiseChanged();
        try
        {
            if (_dualLaunching) throw new InvalidOperationException(Loc.T("请等待组队完成。"));
            if (LocalDualInstanceLauncher.CompanionProcessExited)
            {
                throw new InvalidOperationException(Loc.T("队友进程已退出。请回到主菜单重新邀请。"));
            }

            var connection = LocalDualInstanceLauncher.Connection ?? throw new InvalidOperationException(Loc.T("请先邀请 AI 队友。"));
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
                "paused" => Loc.T("队友已暂停。仍然可以聊天，点击继续后才会自动行动。"),
                "running" => Loc.T("队友正在自动游玩。"),
                _ => Loc.T("队友仍在停止当前任务，请稍后再次确认暂停。")
            };
            return new TeammateControlResult(true, phase, _teamControlStatus);
        }
        catch (Exception ex)
        {
            _teamControlStatus = Loc.T("未确认队友控制结果：{0}", ex.Message);
            return new TeammateControlResult(false, "refused", ex.Message);
        }
        finally
        {
            if (running) EndTeammateStart();
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
                if (!_companionReady) throw new InvalidOperationException(Loc.T("队友尚未完成组队，请稍后继续。"));
                // Starting the in-process loop is one decision no matter who asks for it, so the
                // companion answers to the same model gate as the host's Resume button and
                // POST /teammate/control. Without this, the companion's own /session/control was a
                // way to start a model loop that every other entry point refuses. Pausing is always
                // allowed: no caller should be stuck unable to stop it.
                var firstRun = FirstRunSetup.Evaluate(Settings);
                if (!firstRun.ReadyToInvite) throw new InvalidOperationException(firstRun.Hint);
                StartAutoPlay();
            }
            else
            {
                var stopping = _playSession.RequestPause();
                SetStatus(stopping.IsCompleted ? Loc.T("已暂停自动游玩") : Loc.T("正在暂停，等待当前任务完成…"));
                NoteEvent(Status);
                try
                {
                    await stopping.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (AutoPlayStoppedException)
                {
                    // The loop already ended; treat that as a completed pause.
                }
                if (PlayPhase == "paused") SetStatus(Loc.T("已暂停自动游玩"));
            }
            return PlayPhase;
        }
        finally { _companionControlGate.Release(); }
    }

    public string Status => _status ?? Loc.T("就绪");

    public string LastAction => _lastAction;

    public string LastThought => _lastThought;

    public IReadOnlyList<DecisionLogEntry> RecentDecisions(int limit = 50) => _decisions.Snapshot(limit);

    internal DecisionLog DecisionLogForBriefing => _decisions;

    public string DecisionLogJson(int limit = 50) => _decisions.RenderJson(limit);

    /// <summary>
    /// The run identity the automatic session has observed, or null before one is known. Decisions
    /// are attributed with it so a session that spans two runs can report them separately.
    /// </summary>
    public string? CurrentRunId => _runBoundary.RunId;

    /// <summary>What the current run has cost so far, or the whole log when no run is known yet.</summary>
    public RunSpend CurrentRunSpend() => _decisions.Spend(_runBoundary.RunId);

    public LlmUsage SessionUsage
    {
        get { lock (_gate) return _sessionUsage; }
    }

    public int SessionRequests
    {
        get { lock (_gate) return _sessionRequests; }
    }

    public string? StopKind
    {
        get { lock (_playLifecycleGate) return _stopKind; }
    }

    public bool TryResetSessionStats(out string message)
    {
        if (!SessionBudgetLimits.CanResetSessionStats(PlayRunning, PlayPhase) || Volatile.Read(ref _strategyRefreshInFlight) != 0)
        {
            message = Loc.T("自动游玩进行中，不能清零本会话统计。请先暂停。暂停/继续不会清零累计。");
            SetStatus(message);
            RaiseChanged();
            return false;
        }

        lock (_gate)
        {
            _sessionUsage = LlmUsage.Empty;
            _sessionRequests = 0;
            _sessionUsageKnown = false;
            _jevAcceptedTurns = 0;
            _jevFallbackTurns = 0;
            _budgetGuard = _settings.CreateBudgetGuard();
            _proactiveChat.Reset();
        }

        message = Loc.T("已清零本会话统计。预算上限未改；继续游玩将重新计数。");
        SetStatus(message);
        RaiseChanged();
        return true;
    }

    public string DualStatus => _dualStatus ?? Loc.T("尚未启动双开。");
    public DualLaunchOutcome DualLaunchOutcome => _dualLaunchOutcome;
    public bool DualLaunching => _dualLaunching;
    public bool TeamMessagePending => _teamMessagePending;
    public string TeamStatus => _teamStatus ?? Loc.T("组队后，可以在这里和 AI 队友商量打法。");
    public IReadOnlyList<ChatTurn> TeamHistory => _teamConversation.Snapshot();

    public string McpStatus => _mcpStatus ?? Loc.T("MCP 已关闭，未对外暴露。");

    public string? McpUrl => NativeMcpServer.Runtime?.EndpointUrl;

    public bool McpRunning => NativeMcpServer.Runtime?.Enabled == true;

    public string McpClientConfig => NativeMcpServer.FormatClientConfigJson(McpUrl ?? McpEndpointUrl());

    public string SettingsPath => _store.Path;

    public SettingsPersistenceNotice SettingsNotice => _store.LastNotice;

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
        NativeMcpServer.BindRuntime(
            new GameBridge(),
            Router.BuildHealthData,
            Router.ModVersion,
            _decisions,
            _strategyStore,
            DualLayerStatus);
        // The overlay, /decisions, and the SSE stream are three views of one log, so the mirror is
        // attached once, here, rather than each writer remembering to announce itself.
        _decisions.Recorded += GameEventService.Instance.PublishDecision;
        ApplyMcpFromSettings();
        AppendLog($"API {Server.HttpServer.Instance.Prefix}  role={InstanceRole.Current}");
        if (InstanceRole.IsCompanion)
        {
            SetStatus(Loc.T("同伴实例：正在加入大厅"));
            _ = Task.Run(() => CompanionEntryAsync(_lifetime.Token));
            _ = Task.Run(() => WatchHostAsync(_lifetime.Token));
        }
    }

    public void Shutdown()
    {
        StopAutoPlay();
        FlushSessionIfDirty();
        if (!InstanceRole.IsCompanion)
        {
            LocalDualInstanceLauncher.TryCloseCompanion();
        }
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
        _store.Save(settings);
        CancelStrategyRefresh();
        lock (_gate)
        {
            _settings = settings;
            _budgetGuard.UpdateLimits(settings.MaxSessionTokens, settings.MaxSessionRequests);
            if (!settings.ShowThinkingInChat) _liveThought.Reset();
        }

        ApplyMcpFromSettings();
        RaiseChanged();
    }

    // These setters run straight from overlay callbacks. SettingsStore.Save rethrows IO and access
    // failures, and an exception escaping a Godot signal callback would leave a toggle showing a
    // state the file never recorded, so the failure is reported in the status line instead.
    private void SaveSettingsQuietly()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(Loc.T("设置保存失败，原配置文件未被覆盖。请检查磁盘空间或文件占用后重试。"));
            NoteEvent("settings save failed: " + ex.GetType().Name);
        }
    }

    public void PersistOverlayVisible(bool visible)
    {
        lock (_gate)
        {
            _settings.OverlayVisibleOnStart = visible;
            _settings.HasSeenFirstRunGuide = true;
            SaveSettingsQuietly();
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
            SaveSettingsQuietly();
        }
    }

    public void PersistOverlayPlacement(float? left, float? top)
    {
        lock (_gate)
        {
            _settings.OverlayLeft = left;
            _settings.OverlayTop = top;
            SaveSettingsQuietly();
        }
    }

    public void PersistChatAttachFlags(bool attachState, bool attachScreenshot)
    {
        lock (_gate)
        {
            _settings.AttachStateInChat = attachState;
            _settings.AttachScreenshotInChat = attachScreenshot;
            SaveSettingsQuietly();
        }
    }

    public AgentSettings ReloadSettings()
    {
        var loaded = _store.Load();
        lock (_gate)
        {
            _settings = loaded;
            _budgetGuard.UpdateLimits(loaded.MaxSessionTokens, loaded.MaxSessionRequests);
        }

        RaiseChanged();
        return loaded;
    }

    public Task SendChatAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => SendChatCoreAsync(text, attachState, attachScreenshot, cancellationToken), cancellationToken);
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => TestConnectionCoreAsync(force: true, cancellationToken), cancellationToken);
    }

    public void StartAutoPlay()
    {
        Task task;
        PlaySessionIdentity identity;
        lock (_modeControlGate)
        {
            if (_dualLaunching || _teammateLaunchInFlight)
            {
                SetStatus(Loc.T("正在组队，请等待 AI 队友连接完成。"));
                return;
            }

            if (!CanStartModeLocked("solo"))
            {
                SetStatus(Loc.T("当前模式未开启，无法启动自动游玩。"));
                return;
            }
            if (_modeSwitchInFlight || PlayRunning) return;
            lock (_playLifecycleGate)
            {
                // The boundary is scoped to one automatic session, so a run that started while
                // auto-play was paused is the session's run rather than an identity change.
                _runBoundary = new CurrentRunBoundary();
                var started = _playSession.TryStart(AutoPlayLoopAsync, _lifetime.Token);
                if (started == null) return;

                task = started;

                identity = new PlaySessionIdentity(++_playGeneration, task);
                _playSessionIdentity = identity;
                _proactiveChat.BeginSession();
                _stopKind = null;
                _stopDetail = null;
                _stopRole = null;
                _waitingForGame = false;
                _waitingForPlayer = false;
                _requestingModel = true;
            }
        }

        SetStatus(Loc.T("自动游玩中"));
        _ = ObservePlayCompletionAsync(task, identity);
    }

    public void StopAutoPlay()
    {
        if (InstanceRole.IsCompanion) _companionAutoStartSuppressed = true;
        CancelStrategyRefresh();
        var task = _playSession.RequestPause();
        FlushSessionIfDirty();
        SetStatus(task.IsCompleted ? Loc.T("已暂停自动游玩") : Loc.T("正在暂停，等待当前任务完成…"));
        NoteEvent(Status);
    }

    public void SetMcpEnabled(bool enabled)
    {
        lock (_gate)
        {
            _settings.McpEnabled = enabled;
            SaveSettingsQuietly();
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
        return LaunchDualInstanceAsync(settings, companionAutoPlay: true, cancellationToken);
    }

    /// <summary>
    /// <paramref name="companionAutoPlay"/> false is the external-takeover route: the teammate is
    /// launched for an outside agent, so no play model is required and it comes up paused.
    /// </summary>
    /// <summary>
    /// Starts a launch, or returns null when another attempt already owns the gate. A caller that
    /// cannot claim the gate owns no attempt and must not read <see cref="DualLaunchOutcome"/>:
    /// the winning thread writes that field after claiming, and a failed claim is not ordered after
    /// that write, so a loser can still observe Idle or the previous attempt's terminal outcome.
    /// Null is the only reliable "this attempt is not mine" signal.
    /// </summary>
    public Task? TryLaunchDualInstanceAsync(AgentSettings settings, bool companionAutoPlay, CancellationToken cancellationToken)
    {
        var begin = TryBeginDualLaunch(out var reason);
        if (!begin)
        {
            // Busy still means "someone else's attempt is in flight": pending, not an error. A named
            // refusal records the reason once, so invite/continue no longer hang at pending forever
            // with an outcome stuck at Idle.
            if (reason.IsBusy()) return null;
            _dualStatus = DualLaunchBlockText(reason);
            _dualLaunchOutcome = DualLaunchOutcome.Rejected;
            RaiseChanged();
            return Task.CompletedTask;
        }

        return Task.Run(
            () => LaunchDualInstanceCoreAsync(settings, cancellationToken, continueRun: false, companionAutoPlay),
            CancellationToken.None);
    }

    public Task LaunchDualInstanceAsync(AgentSettings settings, bool companionAutoPlay, CancellationToken cancellationToken)
    {
        return TryLaunchDualInstanceAsync(settings, companionAutoPlay, cancellationToken) ?? Task.CompletedTask;
    }

    public Task ContinueDualInstanceAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        return ContinueDualInstanceAsync(settings, companionAutoPlay: true, cancellationToken);
    }

    /// <summary>
    /// Continue-route twin of <see cref="TryLaunchDualInstanceAsync"/>: null means the gate is
    /// already owned, so the caller reports pending instead of classifying someone else's outcome.
    /// </summary>
    public Task? TryContinueDualInstanceAsync(AgentSettings settings, bool companionAutoPlay, CancellationToken cancellationToken)
    {
        var begin = TryBeginDualLaunch(out var reason);
        if (!begin)
        {
            if (reason.IsBusy()) return null;
            _dualStatus = DualLaunchBlockText(reason);
            _dualLaunchOutcome = DualLaunchOutcome.Rejected;
            RaiseChanged();
            return Task.CompletedTask;
        }

        return Task.Run(
            () => LaunchDualInstanceCoreAsync(settings, cancellationToken, continueRun: true, companionAutoPlay),
            CancellationToken.None);
    }

    public Task ContinueDualInstanceAsync(AgentSettings settings, bool companionAutoPlay, CancellationToken cancellationToken)
    {
        return TryContinueDualInstanceAsync(settings, companionAutoPlay, cancellationToken) ?? Task.CompletedTask;
    }

    private async Task SendChatCoreAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (PlayRunning)
        {
            await QueuePlayInstructionAsync(text, cancellationToken);
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

        SetRequestingModelStatus();
        try
        {
            await _turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            try
            {
                await ObserveCurrentSessionAsync(cancellationToken);
                var prior = History;
                AddHistory("user", text);
                result = await _loop.ChatAsync(
                text,
                prior,
                new ChatOptions
                {
                    AttachState = attachState,
                    AttachScreenshot = attachScreenshot
                },
                cancellationToken, ObserveSessionState);
                RecordTurnReceipt(result, recordBudget: true);
                var reply = result.Error != null
                    ? result.Error
                    : string.IsNullOrWhiteSpace(result.AssistantText) ? Loc.T("(无文本回复)") : result.AssistantText;
                AppendTurnTraces(result);
                AddHistory("assistant", reply);
                _lastThought = result.Reasoning ?? reply;
                if (!string.IsNullOrWhiteSpace(result.Acted))
                {
                    _lastAction = result.Acted;
                }

                SetStatus(result.Error == null ? Loc.T("对话完成") : Loc.T("对话出错"));
            }
            catch (AgentTurnCanceledException ex)
            {
                RecordTurnReceipt(ex.Receipt, recordBudget: true);
                throw;
            }
            finally
            {
                FlushSessionIfDirty();
                _turnGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Loc.T("对话已取消"));
        }
        catch (Exception ex)
        {
            AddHistory("assistant", Loc.T("请求失败：{0}", ex.Message));
            SetStatus(Loc.T("对话失败"));
        }
        finally { ClearLiveThought(); } // No partial reasoning survives canceled or failed idle chat.
    }

    private async Task<string> TestConnectionCoreAsync(bool force, CancellationToken cancellationToken)
    {
        SetStatus(Loc.T("正在测试模型…会向配置的服务发送测试请求。"));
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
                SetStatus(Loc.T("游玩模型测试失败"));
            }
            else if (play.Record.Status == "verified")
            {
                ClearModelTestFailure();
                SetStatus(Loc.T("游玩模型连通成功（不等于工具/视觉已验证）"));
                MarkFirstRunGuideSeen();
            }
            else
            {
                SetStatus(Loc.T("模型尚未验证"));
            }

            NoteEvent(summary);
            return summary;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(Loc.T("模型测试已取消。"));
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("连通失败"));
            ClassifyStop(ex.Message, ModelRoleNames.Play);
            return DiagnosticExport.Redact(ex.Message);
        }
    }

    private async Task StepOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (PlayRunning)
        {
            SetStatus(Loc.T("自动游玩中，请先暂停再单步"));
            return;
        }

        SetStatus(Loc.T("单步决策中"));
        try
        {
            await _turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            string? budgetStop;
            try
            {
                await ObserveCurrentSessionAsync(cancellationToken);
                result = await _loop.PlayOnceAsync(cancellationToken, ObserveSessionState, SetPlayPhase);
                lock (_gate)
                {
                    budgetStop = _budgetGuard.Observe(result) ?? _budgetGuard.CheckBudget();
                }
                ApplyPlayResult(result);
            }
            catch (AgentTurnCanceledException ex)
            {
                RecordTurnReceipt(ex.Receipt, recordBudget: true);
                throw;
            }
            finally
            {
                ClearLiveThought(); // A failed single step must not leave its partial reasoning up.
                FlushSessionIfDirty();
                _turnGate.Release();
            }

            if (!string.IsNullOrWhiteSpace(budgetStop))
            {
                SetStop(StopKindPolicy.Budget, budgetStop, ModelRoleNames.Play);
                SetStatus(budgetStop);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Loc.T("单步已取消"));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("单步失败：{0}", ex.Message));
        }
    }

    private async Task LaunchDualInstanceCoreAsync(
        AgentSettings settings,
        CancellationToken cancellationToken,
        bool continueRun,
        bool companionAutoPlay)
    {
        var previousConnection = LocalDualInstanceLauncher.Connection;
        try
        {
            if (_teamMessagePending || _teamControlPending)
            {
                _dualStatus = Loc.T("请等待当前队伍消息完成，再重新组队。");
                _dualLaunchOutcome = DualLaunchOutcome.Rejected;
                return;
            }
            var screen = await new GameBridge().GetScreenAsync(cancellationToken);
            var error = CoopLaunchPolicy.GetError(
                InstanceRole.IsCompanion,
                PlayRunning,
                screen,
                settings,
                requireVerifiedPlayModel: companionAutoPlay);
            if (error != null)
            {
                _dualStatus = error;
                _dualLaunchOutcome = DualLaunchOutcome.Rejected;
                return;
            }

            // The child reads settings at startup. Persist the edited model
            // selection before launching so both windows use the same choices.
            SaveSettings(settings);
            _dualStatus = continueRun
                ? Loc.T("正在继续联机存档，等待队友窗口连回…")
                : Loc.T("正在邀请 AI 队友，等待游戏窗口连接…");
            RaiseChanged();
            var launchResult = continueRun
                ? await DualInstanceCoordinator.ContinueLocalCoopResultAsync(cancellationToken, companionAutoPlay)
                : await DualInstanceCoordinator.HostLocalCoopResultAsync(cancellationToken, companionAutoPlay);
            _dualStatus = launchResult.Message;
            _dualLaunchOutcome = launchResult.Ok ? DualLaunchOutcome.Succeeded : DualLaunchOutcome.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _dualStatus = Loc.T("已取消等待队友连接；若队友窗口已打开，请在该窗口确认状态。");
            _dualLaunchOutcome = DualLaunchOutcome.Canceled;
        }
        catch (Exception ex)
        {
            _dualStatus = continueRun
                ? Loc.T("继续联机存档失败：{0}", ex.Message)
                : Loc.T("邀请队友失败：{0}", ex.Message);
            _dualLaunchOutcome = DualLaunchOutcome.Failed;
        }
        finally
        {
            if (!ReferenceEquals(previousConnection, LocalDualInstanceLauncher.Connection))
            {
                // The route belongs to the teammate session that is actually running, so it is only
                // recorded when this attempt established a new connection. A rejected retry -- most
                // often "the teammate window is already running" -- must not relabel a teammate that
                // was launched the other way, or /health would tell an external agent to take over a
                // seat that is already being played by the in-process loop.
                _companionAutoPlay = companionAutoPlay;
                _teamConversation.Clear();
                _teamStatus = Loc.T("队伍对话已重置。确认队友连接后，可以商量这次冒险的打法。");
            }
            _dualLaunching = false;
            EndTeammateLaunch();
            _dualLaunchGate.Release();
            RaiseChanged();
        }
    }

    private async Task CompanionEntryAsync(CancellationToken cancellationToken)
    {
        var joined = await DualInstanceCoordinator.RunCompanionBootstrapAsync(cancellationToken);
        if (!joined)
        {
            SetStatus(Loc.T("同伴实例加入大厅失败"));
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
            else
            {
                // External-takeover launch: this process is a seat an outside agent drives. A
                // companion window has no overlay (ModEntry skips it for companions), so the route is
                // reported where it can actually be observed: play_phase stays "paused" with zero
                // model requests, and the host window's dual status names the route.
                SetStatus(Loc.T("等待外部接管：队友窗口已就绪，未自动开始游玩。"));
                NoteEvent(Status);
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
                if (PlayPhase == "paused" && _stopKind == null) SetStatus(Loc.T("已暂停自动游玩"));
            }
        }
        catch (Exception ex)
        {
            lock (_playLifecycleGate)
            {
                if (!IsCurrentPlaySessionLocked(identity)) return;

                Log.Warn($"{LogPrefix} Auto-play session ended: {ex.Message}");
                ClassifyStop(ex, ModelRoleNames.Play);
                _requestingModel = false;
                if (PlayPhase == "paused") SetStatus(Loc.T("自动游玩已停止：{0}", DiagnosticExport.Redact(ex.Message)));
                NoteEvent("stop " + (_stopKind ?? "failed") + ": " + ex.Message);
            }
        }
    }

    private async Task AutoPlayLoopAsync(CancellationToken cancellationToken)
    {
        var boundary = _runBoundary;
        var moment = ProactiveChatMoment.None;
        SessionBudgetGuard budgetGuard;
        lock (_gate)
        {
            budgetGuard = _budgetGuard;
        }

        try
        {
            await AutoPlayRecovery.RunAsync(async token =>
            {
                // Recovery owns the turn gate through receipt accounting.
                try
                {
                    _requestingModel = true;
                    RaiseChanged();
                    // Bounded and token-aware: a game thread that stops pumping must fail this turn
                    // visibly and let pause through, not wedge the loop in "stopping" forever.
                    var snapshot = await GameThread.InvokeAsync(() =>
                    {
                        var payload = GameStateService.BuildStatePayload();
                        return (payload.screen, payload.session.phase, payload.run_id, payload.in_combat, payload.run?.act_id);
                    }, TurnGameThreadBudget, token);
                    // Do not re-arm the boundary here: replacing it before checking would clear the
                    // "entered a run" flag that makes a return to the menu a stop. Starting auto-play
                    // is what installs a fresh boundary (see StartAutoPlay).
                    boundary.Check(snapshot.Item1, snapshot.Item2, snapshot.Item3);
                    // The boundary only remembers a run it has already entered. A fresh frame is
                    // what may switch or clear the persisted session.
                    ObserveSessionStateSnapshot(snapshot.Item3, snapshot.Item1, snapshot.Item2);
                    moment = ObserveProactiveMoment(snapshot.Item1, snapshot.Item4);
                    var immediate = await TryCompanionImmediateAsync(token);
                    if (immediate != null)
                    {
                        return immediate;
                    }

                    SetRequestingModelStatus();
                    double? executionConfidence = null;
                    var turn = await _loop.PlayOnceAsync(token, json =>
                    {
                        boundary.Check(json);
                        // A compact frame can confirm the same run, but it is not a fresh raw
                        // snapshot and must not retire the live session by itself.
                        ObserveSessionState(json);
                    }, SetPlayPhase, value => executionConfidence = value);
                    // Feed the strategy planner the turn's context: the screen/phase the snapshot
                    // saw and the confidence the Jev path reported (null on the LLM path). The
                    // planner decides for itself whether this context deserves a replan.
                    ObserveStrategyContext(snapshot.Item1, snapshot.Item5, executionConfidence, token);
                    return turn;
                }
                finally
                {
                    _requestingModel = false;
                    // Before the receipt is committed: a turn that threw, paused or stopped must not
                    // leave a streamed reasoning bubble standing.
                    ClearLiveThought();
                }

            }, ApplyPlayResult, cancellationToken, delay: null, budgetGuard: budgetGuard,
            afterTurn: async token =>
            {
                await _turnGate.WaitAsync(token);
                try { await TryProactiveChatAsync(moment, token); }
                finally { ClearLiveThought(); FlushSessionIfDirty(); _turnGate.Release(); }
            },
            reportInterrupted: result => RecordTurnReceipt(result), turnGate: _turnGate);
        }
        finally { CancelStrategyRefresh(); FlushSessionIfDirty(); RaiseChanged(); }
    }

    /// <summary>
    /// Deadline for the auto-play turn's own game-thread posts (the turn snapshot and the companion's
    /// immediate decision). A normal frame runs them within milliseconds.
    /// </summary>
    private static readonly TimeSpan TurnGameThreadBudget = TimeSpan.FromSeconds(15);

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
        }, TurnGameThreadBudget, cancellationToken);

        if (decision.Kind == CompanionImmediateDecision.Wait)
        {
            await Task.Delay(400, cancellationToken);
            return new AgentTurnResult
            {
                Reasoning = Loc.T("等待你选择地图节点，随后投同一格。"),
                WaitingForPlayer = true,
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
        }, TurnGameThreadBudget, cancellationToken);

        var reasoning = string.Equals(acted, "confirm_modal", StringComparison.OrdinalIgnoreCase)
            ? Loc.T("确认阻挡操作的教学弹窗。")
            : Loc.T("跟随你的地图选择。");
        return new AgentTurnResult
        {
            Acted = acted,
            ActResultJson = json,
            Reasoning = reasoning,
            ToolRounds = 0,
            RequestsSpent = 0
        };
    }

    private ProactiveChatMoment ObserveProactiveMoment(string? screen, bool inCombat)
    {
        var key = ProactiveChatPolicy.SituationKey(screen, inCombat);
        var moment = ProactiveChatPolicy.Observe(_proactiveSituationKey, key);
        _proactiveSituationKey = key;
        return moment;
    }

    // Runs while the turn gate is already held: the chat call must not take it again,
    // and a failure here must never fail the auto-play turn.
    private async Task TryProactiveChatAsync(ProactiveChatMoment moment, CancellationToken cancellationToken)
    {
        try
        {
            if (moment == ProactiveChatMoment.None)
            {
                return;
            }

            bool enabled;
            string tone;
            string? budgetBlock;
            lock (_gate)
            {
                enabled = _settings.ProactiveChatEnabled;
                tone = _settings.ProactiveChatTone;
                budgetBlock = _budgetGuard.CheckBudget();
            }

            // The session owns the counter and the interval stamp, so the volume bounds
            // cannot drift from the policy.
            var decision = _proactiveChat.Decide(enabled, PlayRunning, budgetBlock, moment, DateTimeOffset.UtcNow);
            if (!decision.Send)
            {
                return;
            }

            var result = await _loop.ChatAsync(
                ProactiveChatPolicy.BuildPrompt(moment),
                History,
                new ChatOptions
                {
                    AttachState = true,
                    ReadOnly = true,
                ExtraSystemInstruction = ProactiveChatTones.BuildSystemInstruction(tone)
            },
            cancellationToken);
            AccountTurn(result, recordBudget: true);
            if (result.Error != null)
            {
                NoteEvent("proactive chat: " + DiagnosticExport.Redact(result.Error));
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.AssistantText))
            {
                AddHistory("assistant", result.AssistantText);
            }

            NoteEvent("proactive chat sent (" + moment + ")");
        }
        catch (AgentTurnCanceledException ex)
        {
            RecordTurnReceipt(ex.Receipt, recordBudget: true);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            NoteEvent("proactive chat skipped: " + DiagnosticExport.Redact(ex.Message));
        }
    }

    private void ApplyMcpFromSettings()
    {
        var enabled = Settings.McpEnabled;
        var url = McpEndpointUrl();
        NativeMcpServer.Runtime?.SetEnabled(enabled, url);
        _mcpStatus = enabled
            ? Loc.T("MCP 已打开。把下面的地址或配置贴进外部客户端。")
            : null;
    }

    private static string McpEndpointUrl()
    {
        return HttpServer.Instance.Prefix.TrimEnd('/') + "/mcp";
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
            RequestingModel = _requestingModel || _requestingModelStatus,
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

    private void ClassifyStop(string message, string? role = null)
    {
        SetStop(StopKindPolicy.Classify(message), message, role);
    }

    private void ClassifyStop(Exception error, string? role = null)
    {
        var kind = error is AutoPlayStoppedException stopped
            ? StopKindPolicy.Resolve(stopped.Kind, error.Message)
            : StopKindPolicy.Classify(error.Message);
        SetStop(kind, error.Message, role);
    }

    private void SetStop(string kind, string message, string? role)
    {
        _stopKind = kind;
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

    private void AppendLog(string line)
    {
        Log.Info($"{LogPrefix} {line}");
    }

    public void NotifyStatus(string status) => SetStatus(status);

    private void RaiseChanged()
    {
        Changed?.Invoke();
    }
}
