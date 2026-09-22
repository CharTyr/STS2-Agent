using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The runtime's Jev execution-model surface: configuration validation today, the real round-trip
/// ping once the Jev engine lands.
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
