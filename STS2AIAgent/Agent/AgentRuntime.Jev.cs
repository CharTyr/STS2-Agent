using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The runtime's Jev execution-model surface: the shared <see cref="StrategyStore"/> both halves of
/// the dual-layer engine read and write, plus configuration validation.
/// </summary>
/// <remarks>
/// Split from <c>AgentRuntime.cs</c> because that file is at its size budget and the Jev surface grows
/// with the dual-layer engine (child task jev-two-layer-engine). Keeping it in its own partial means
/// the engine's runtime wiring does not arrive as another twenty-line addition to the file the
/// ratchet watches.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>
    /// The one strategy store for this instance. The in-game planner and an external MCP client both
    /// write it; the Jev decider reads it on every action. A single store is what makes the overlay
    /// path and the MCP path the same experience rather than two.
    /// </summary>
    private readonly StrategyStore _strategyStore = new();

    /// <summary>The current play strategy the Jev decider is following.</summary>
    public PlayStrategy CurrentStrategy => _strategyStore.Current;

    /// <summary>
    /// The slow half of the dual-layer engine, wired to the same store the decider reads. Created
    /// once with the runtime; its cadence guard makes a turn cheap unless the context changed or
    /// Jev has been unsure for a streak.
    /// </summary>
    private StrategyPlanner? _strategyPlanner;
    private int _strategyRefreshInFlight;
    private CancellationTokenSource? _strategyCancellation;

    private StrategyPlanner Planner => _strategyPlanner ??= new StrategyPlanner(
        new DefaultLlmClientFactory(),
        () =>
        {
            lock (_gate)
            {
                return _settings;
            }
        },
        _strategyStore);

    /// <summary>
    /// Feeds one finished turn to the planner: the screen/act context key and the Jev confidence.
    /// When the planner decides a replan is due, it runs in the background -- the play loop never
    /// waits for a strategy, and the decider keeps the previous one until the new plan lands.
    /// </summary>
    private void ObserveStrategyContext(string? screen, string? act, double? confidence, CancellationToken playToken)
    {
        StrategyPlanner planner;
        CancellationTokenSource cancellation;
        string runId;
        long revision;
        lock (_gate)
        {
            var enabled = InstanceRole.IsCompanion ? _settings.DualLayerCoopEnabled : _settings.DualLayerSoloEnabled;
            if (!enabled || !_settings.HasJevConfigured() || !PlaySessionStore.IsPersistable(_sessionRunId)
                || playToken.IsCancellationRequested || _lifetime.IsCancellationRequested) return;
            if (Interlocked.CompareExchange(ref _strategyRefreshInFlight, 1, 0) != 0) return;
            planner = Planner;
            var key = _sessionRunId + "|" + (screen ?? "UNKNOWN") + "|" + (act ?? "");
            if (!planner.ShouldRefresh(key, confidence, _settings.JevConfidenceThreshold))
            {
                Interlocked.Exchange(ref _strategyRefreshInFlight, 0);
                return;
            }
            runId = _sessionRunId!;
            revision = _strategyStore.Revision;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(playToken, _lifetime.Token);
            _strategyCancellation = cancellation;
        }
        _ = Task.Run(async () =>
        {
            var token = cancellation.Token;
            try
            {
                var summary = await _loop.DescribeCurrentStateForPlanningAsync(token);
                // Reserve one request only when the previous turn has committed its own spend.
                // The lease covers reservation, not the slow provider wait.
                async Task<bool> BeginRequest(CancellationToken requestToken)
                {
                    await _turnGate.WaitAsync(requestToken);
                    try
                    {
                        requestToken.ThrowIfCancellationRequested();
                        lock (_gate)
                        {
                            if (_sessionRunId != runId || _strategyStore.Revision != revision
                                || _budgetGuard.CheckBudget() != null) return false;
                            AccountTurn(new AgentTurnResult { RequestsSpent = 1 }, recordBudget: true);
                            return true;
                        }
                    }
                    finally { _turnGate.Release(); }
                }
                var (adopted, usage) = await planner.RefreshAsync(summary, token, BeginRequest, revision,
                    _decisions.Snapshot(50), runId);
                // The request was reserved before dispatch, including failed/canceled attempts.
                // Only returned token usage is added here; an absent model never reserves anything.
                if (usage != null) AccountTurn(new AgentTurnResult { Usage = usage }, recordBudget: true);
                NoteEvent(adopted ? "strategy refreshed by planner" : "strategy refresh produced no new plan");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { NoteEvent("strategy refresh failed: " + DiagnosticExport.Redact(ex.Message)); }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_strategyCancellation, cancellation)) _strategyCancellation = null;
                    Interlocked.Exchange(ref _strategyRefreshInFlight, 0);
                }
                cancellation.Dispose();
                FlushSessionIfDirty();
            }
        });
    }

    private void CancelStrategyRefresh()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _strategyStore.Invalidate();
            cancellation = _strategyCancellation;
            _strategyPlanner = null;
        }
        // Provider cancellation callbacks must not run while holding the runtime lock.
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { } // The completed planner may have disposed its own source.
    }

    /// <summary>
    /// Builds the Jev execution decider when the configuration is complete enough to call Jev, or null
    /// when it is not -- a missing key or base URL leaves the loop on the plain LLM path rather than
    /// constructing a client that can only fail.
    /// </summary>
    private IActionDecider? BuildJevDecider()
    {
        AgentSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        if (!settings.HasJevConfigured())
        {
            return null;
        }

        // The per-request timeout is configurable; unset means 90 seconds. Five minutes suits a
        // batch job, but a play turn that stalls should fail visibly in about a minute and a half,
        // not hold the loop silently.
        var timeout = TimeSpan.FromSeconds(settings.JevRequestTimeoutSeconds is > 0 ? settings.JevRequestTimeoutSeconds.Value : 90);
        // The play loop already owns recovery and accounting; do not hide extra attempts inside it.
        try
        {
            var client = new JevClient(settings.JevBaseUrl, settings.JevApiKey, settings.JevModel, maxRetries: 0, requestTimeout: timeout);
            // The recovery turn gate spans provider work and receipt accounting; the planner
            // reserves any concurrent request under the same gate. Check committed + pending
            // attempts before EACH Jev transport call, while the decider owns a single turn deadline.
            return new JevExecutionDecider(client, settings.JevModel, turnTimeout: timeout,
                allowAttempt: attemptsSoFar =>
                {
                    lock (_gate) return _budgetGuard.CheckBudget(extraRequests: attemptsSoFar) == null;
                });
        }
        catch (JevException ex) when (ex.Kind == JevExceptionKind.Config)
        {
            NoteEvent(ex.Message);
            return null;
        }
    }

    /// <summary>The cached decider and the configuration fingerprint it was built from.</summary>
    private IActionDecider? _jevDecider;
    private string _jevDeciderFingerprint = string.Empty;

    /// <summary>
    /// The live decider the loop asks for on every turn. Building the decider once at construction
    /// froze it to the configuration that existed before the player ever opened settings -- entering
    /// the Jev key later left the dual-layer toggle permanently dead until a restart. Rebuilds only
    /// when the Jev configuration fingerprint changes, so a turn that finds the same settings pays
    /// one string comparison.
    /// </summary>
    private IActionDecider? ResolveJevDecider()
    {
        lock (_gate)
        {
            var fingerprint = _settings.HasJevConfigured()
                ? string.Join('\n', _settings.JevBaseUrl, _settings.JevApiKey, _settings.JevModel, _settings.JevRequestTimeoutSeconds?.ToString() ?? "")
                : string.Empty;
            if (fingerprint == _jevDeciderFingerprint)
            {
                return _jevDecider;
            }

            _jevDeciderFingerprint = fingerprint;
            _jevDecider = fingerprint.Length == 0 ? null : BuildJevDecider();
            return _jevDecider;
        }
    }

    /// <summary>
    /// The dual-layer status the MCP planner briefing reports: whether the mode is on for this
    /// instance's role, and whether the Jev configuration is complete. Read under the gate so a
    /// settings save mid-request cannot hand the briefing a torn pair.
    /// </summary>
    internal (bool DualLayer, bool JevConfigured) DualLayerStatus()
    {
        lock (_gate)
        {
            return (InstanceRole.IsCompanion ? _settings.DualLayerCoopEnabled : _settings.DualLayerSoloEnabled,
                _settings.HasJevConfigured());
        }
    }

    /// <summary>
    /// Replaces the current play strategy. Called by the in-game <c>StrategyPlanner</c> and by the
    /// <c>POST /strategy</c> route an external planner drives; both go through here so the source tag
    /// and the change notification are applied the same way.
    /// </summary>
    public void UpdatePlayStrategy(PlayStrategy strategy)
    {
        _strategyStore.Update(strategy);
        MarkSessionDirty();
        RaiseChanged();
    }

    /// <summary>
    /// Applies an external planner's partial update (<c>POST /strategy</c>). The merge runs inside the
    /// store's lock, so a plan the in-game planner lands at the same moment is never reverted.
    /// </summary>
    public PlayStrategy UpdatePlayStrategy(PlayStrategyUpdate update)
    {
        var stored = _strategyStore.UpdateMerged(current => update.ApplyTo(current, "mcp"));
        MarkSessionDirty();
        RaiseChanged();
        return stored;
    }

    /// <summary>
    /// Validates the Jev execution-model configuration with a real round trip: a configured client
    /// pings <c>GET /v1/models</c> and the answer (or the classified failure) is what the settings
    /// page shows.
    /// </summary>
    public async Task<string> TestJevConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.JevApiKey))
        {
            return Loc.T("未配置 Jev API Key。");
        }

        if (string.IsNullOrWhiteSpace(settings.JevBaseUrl))
        {
            return Loc.T("未配置 Jev Base URL。");
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(settings.JevRequestTimeoutSeconds is > 0 ? settings.JevRequestTimeoutSeconds.Value : 90);
            var client = new JevClient(settings.JevBaseUrl, settings.JevApiKey, settings.JevModel, requestTimeout: timeout);
            return await client.PingAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Loc.T("Jev 连接失败：{0}", ex.Message);
        }
    }
}
