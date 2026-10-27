namespace STS2AIAgent.Llm;

/// <summary>
/// Where a fast-judgment service lives. The configured address is either a service root
/// (<c>https://api.typesafe.ai</c>, the original form) or the full request URL a provider documents
/// (<c>https://openrouter.ai/api/v1/systemone</c>); the client resolves both to the same two calls.
/// </summary>
/// <remarks>
/// A root keeps getting <c>/v1/systemone</c> appended, so every configuration saved before presets
/// existed behaves exactly as it did. Only an address that already ends in <c>/systemone</c> or
/// <c>/decisions</c> is used as written. Pure functions and a constant table, so the offline suite
/// exercises them without a game or a network.
/// </remarks>
internal static class JevEndpoint
{
    private const string DefaultRequestPath = "/v1/systemone";

    private const string DefaultModelsPath = "/v1/models";

    /// <summary>A provider's documented request URL and default model, ready to drop into the settings form.</summary>
    internal sealed record Preset(string Id, string Name, string Url, string Model, string Note);

    /// <summary>
    /// Providers that speak the System One request shape. URLs and model names are copied from each
    /// provider's published documentation; every one stays editable in the form.
    /// </summary>
    internal static readonly IReadOnlyList<Preset> Presets = new Preset[]
    {
        new("typesafe", "TypeSafe", "https://api.typesafe.ai/v1/systemone", "jev-latest", ""),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/systemone", "typesafe/jev-1.13", ""),
        new("opencode", "OpenCode Zen", "https://opencode.ai/zen/v1/systemone", "jev-1.13", ""),
        new("commandcode", "Command Code", "https://api.commandcode.ai/provider/v1/systemone", "typesafe/jev", ""),
        new("vercel", "Vercel AI Gateway", "https://ai-gateway.vercel.sh/typesafe/v1/systemone", "typesafe-ai/jev", ""),
        new("upstage", "Upstage", "https://api.upstage.ai/v1/systemone", "solar-decide", ""),
        new("inception", "Inception", "https://api.inceptionlabs.ai/v1/decisions", "mercury-decide", ""),
        new("liquid", "Liquid AI", "https://api.liquid.ai/decisions/v1/systemone", "d1", ""),
    };

    /// <summary>The preset whose address matches the configured one, or null for a custom address.</summary>
    internal static Preset? FindPreset(string? configuredUrl)
    {
        var normalized = Normalize(configuredUrl);
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Url, normalized, StringComparison.OrdinalIgnoreCase)) return preset;
        }

        return null;
    }

    /// <summary>The URL the question call is posted to.</summary>
    internal static string RequestUrl(string? configuredUrl)
    {
        var normalized = Normalize(configuredUrl);
        return EndsInRequestSegment(normalized) ? normalized : normalized + DefaultRequestPath;
    }

    /// <summary>
    /// The URL of the connection probe. A full request URL swaps its last segment for <c>models</c>
    /// (<c>.../v1/systemone</c> becomes <c>.../v1/models</c>); a root gets <c>/v1/models</c>.
    /// </summary>
    internal static string ModelsUrl(string? configuredUrl)
    {
        var normalized = Normalize(configuredUrl);
        if (!EndsInRequestSegment(normalized)) return normalized + DefaultModelsPath;
        return normalized[..(normalized.LastIndexOf('/') + 1)] + "models";
    }

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');

    private static bool EndsInRequestSegment(string normalized) =>
        normalized.EndsWith("/systemone", StringComparison.OrdinalIgnoreCase)
        || normalized.EndsWith("/decisions", StringComparison.OrdinalIgnoreCase);
}
