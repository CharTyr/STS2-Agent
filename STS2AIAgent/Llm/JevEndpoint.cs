using System.Text.RegularExpressions;

namespace STS2AIAgent.Llm;

/// <summary>
/// Where a fast-judgment service lives. The configured address is either a service root
/// (<c>https://api.typesafe.ai</c>, the original form) or the full request URL a provider documents
/// (<c>https://openrouter.ai/api/v1/systemone</c>); the client resolves both to the same call.
/// </summary>
/// <remarks>
/// A root keeps getting <c>/v1/systemone</c> appended, so every configuration saved before presets
/// existed behaves exactly as it did. An address that already ends in <c>/systemone</c> or
/// <c>/decisions</c>, or is a Cloudflare Workers AI <c>/ai/run/</c> model URL, is used as written.
/// Pure functions and a constant table, so the offline suite exercises them without a game or a network.
/// </remarks>
internal static class JevEndpoint
{
    private const string DefaultRequestPath = "/v1/systemone";

    /// <summary>
    /// A provider's documented request URL, wire format and models, ready to drop into the settings form.
    /// <see cref="Models"/>[0] is the default; <see cref="KeyUrl"/> and <see cref="DocsUrl"/> may be empty.
    /// </summary>
    internal sealed record Preset(
        string Id, string Name, string Protocol, string Url, string[] Models, string DocsUrl, string KeyUrl)
    {
        public string Model => Models[0];
    }

    /// <summary>
    /// Every provider MaiWork ships a preset for. URLs, models and links are copied from each provider's
    /// published documentation (2026-10); none has been exercised with a live key here, and every field
    /// stays editable in the form.
    /// </summary>
    internal static readonly IReadOnlyList<Preset> Presets = new Preset[]
    {
        new("typesafe", "TypeSafe", JevProtocol.SystemOne, "https://api.typesafe.ai/v1/systemone",
            new[] { "jev-latest", "jev-1.13.0" }, "https://docs.typesafe.ai/api", ""),
        new("openrouter", "OpenRouter", JevProtocol.SystemOne, "https://openrouter.ai/api/v1/systemone",
            new[] { "typesafe/jev-1.13", "~typesafe/jev-latest" }, "https://openrouter.ai/docs/guides/community/jev", "https://openrouter.ai/keys"),
        new("opencode", "OpenCode Zen", JevProtocol.SystemOne, "https://opencode.ai/zen/v1/systemone",
            new[] { "jev-1.13", "jev-1.13-free" }, "https://opencode.ai/docs/zen", ""),
        new("commandcode", "Command Code", JevProtocol.SystemOne, "https://api.commandcode.ai/provider/v1/systemone",
            new[] { "typesafe/jev" }, "https://commandcode.ai/models/jev", ""),
        new("vercel", "Vercel AI Gateway", JevProtocol.SystemOne, "https://ai-gateway.vercel.sh/typesafe/v1/systemone",
            new[] { "typesafe-ai/jev" }, "https://vercel.com/docs/ai-gateway/sdks-and-apis/typesafe", ""),
        new("upstage", "Upstage", JevProtocol.SystemOne, "https://api.upstage.ai/v1/systemone",
            new[] { "solar-decide" }, "https://console.upstage.ai/api/systemone", ""),
        new("inception", "Inception", JevProtocol.SystemOne, "https://api.inceptionlabs.ai/v1/decisions",
            new[] { "mercury-decide" }, "https://docs.inceptionlabs.ai/capabilities/decisions", ""),
        new("liquid", "Liquid AI", JevProtocol.SystemOne, "https://api.liquid.ai/decisions/v1/systemone",
            new[] { "d1", "d1:free" }, "https://docs.liquid.ai/lfm/models/d1", ""),
        new("cloudflare", "Cloudflare", JevProtocol.SystemOne,
            "https://api.cloudflare.com/client/v4/accounts/{account_id}/ai/run/@cf/cloudflare/clef-flash",
            new[] { "clef-flash", "clef" }, "https://developers.cloudflare.com/workers-ai/models/clef/", ""),
        new("openai", "OpenAI Decisions", JevProtocol.OpenAiDecisions, "https://api.openai.com/v1/decisions",
            new[] { "gpt-6-luna" }, "https://developers.openai.com/api/docs/guides/decisions", ""),
    };

    /// <summary>
    /// The preset whose address matches the configured one, or null for a custom address. A preset with a
    /// placeholder (Cloudflare's account id) matches any value there and any model in the last segment.
    /// </summary>
    internal static Preset? FindPreset(string? configuredUrl)
    {
        var normalized = Normalize(configuredUrl);
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Url, normalized, StringComparison.OrdinalIgnoreCase)) return preset;
            if (preset.Url.IndexOf('{') < 0) continue;
            // The placeholder segment matches any account id, and the last segment any model name.
            var pattern = Regex.Replace(Regex.Escape(preset.Url), @"\\\{[^}]*}", "[^/]+");
            pattern = "^" + pattern[..pattern.LastIndexOf('/')] + "/[^/]+$";
            if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase)) return preset;
        }

        return null;
    }

    /// <summary>The URL the question call is posted to.</summary>
    internal static string RequestUrl(string? configuredUrl)
    {
        var normalized = Normalize(configuredUrl);
        return IsFullRequestUrl(normalized) ? normalized : normalized + DefaultRequestPath;
    }

    /// <summary>True while the address still holds a preset's <c>{…}</c> placeholder, such as Cloudflare's account id.</summary>
    internal static bool HasPlaceholder(string? configuredUrl) =>
        Normalize(configuredUrl).IndexOf('{') >= 0;

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');

    private static bool IsFullRequestUrl(string normalized) =>
        normalized.EndsWith("/systemone", StringComparison.OrdinalIgnoreCase)
        || normalized.EndsWith("/decisions", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/ai/run/", StringComparison.OrdinalIgnoreCase);
}
