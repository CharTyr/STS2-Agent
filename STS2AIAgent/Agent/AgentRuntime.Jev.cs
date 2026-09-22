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

        var client = new JevClient(settings.JevBaseUrl, settings.JevApiKey, settings.JevModel);
        return new JevExecutionDecider(client, settings.JevModel);
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
    /// Validates the Jev execution-model configuration. The real round-trip ping lands with the Jev
    /// engine (child task jev-two-layer-engine); until then this reports whether the configuration is
    /// complete enough to use, so the settings page's test button is never a no-op.
    /// </summary>
    public Task<string> TestJevConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.JevApiKey))
        {
            return Task.FromResult(Loc.T("未配置 Jev API Key。"));
        }

        if (string.IsNullOrWhiteSpace(settings.JevBaseUrl))
        {
            return Task.FromResult(Loc.T("未配置 Jev Base URL。"));
        }

        return Task.FromResult(Loc.T("Jev 配置已就绪（{0}）。连接测试将随双层决策引擎一同提供。", settings.JevModel));
    }
}
