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
    private void ObserveStrategyContext(string? screen, string? act, double? confidence)
    {
        bool dualLayerOn;
        double threshold;
        lock (_gate)
        {
            dualLayerOn = InstanceRole.IsCompanion ? _settings.DualLayerCoopEnabled : _settings.DualLayerSoloEnabled;
            threshold = _settings.JevConfidenceThreshold;
        }

        if (!dualLayerOn || ResolveJevDecider() == null)
        {
            return;
        }

        var contextKey = (screen ?? "UNKNOWN") + "|" + (act ?? "");
        if (!Planner.ShouldRefresh(contextKey, confidence, threshold))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _strategyRefreshInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var summary = await _loop.DescribeCurrentStateForPlanningAsync(_lifetime.Token);
                var adopted = await Planner.RefreshAsync(summary, _lifetime.Token);
                NoteEvent(adopted ? "strategy refreshed by planner" : "strategy refresh produced no new plan");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                NoteEvent("strategy refresh failed: " + DiagnosticExport.Redact(ex.Message));
            }
            finally
            {
                Interlocked.Exchange(ref _strategyRefreshInFlight, 0);
            }
        });
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

        // The per-request timeout is configurable: five minutes suits a batch job, but a play turn
        // that stalls should fail visibly in about a minute and a half, not hold the loop silently.
        var timeout = settings.JevRequestTimeoutSeconds is > 0 ? TimeSpan.FromSeconds(settings.JevRequestTimeoutSeconds.Value) : (TimeSpan?)null;
        var client = new JevClient(settings.JevBaseUrl, settings.JevApiKey, settings.JevModel, requestTimeout: timeout);
        return new JevExecutionDecider(client, settings.JevModel);
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
    private (bool DualLayer, bool JevConfigured) DualLayerStatus()
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
            var client = new JevClient(settings.JevBaseUrl, settings.JevApiKey, settings.JevModel);
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
