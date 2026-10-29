using System.Text.Json;

namespace STS2AIAgent.Agent;

internal static class ActJsonParser
{
    public static bool TryParse(string? text, out string argumentsJson) =>
        TryParse(text, wholeReplyOnly: false, out argumentsJson);

    /// <summary>
    /// <paramref name="wholeReplyOnly"/> accepts only a reply that IS the act (bare or in one code
    /// fence). A model with native tools that writes JSON as its whole answer meant to act; one that
    /// quotes an action inside advice ("<c>{"action":"end_turn"}</c> is fine") did not.
    /// </summary>
    public static bool TryParse(string? text, bool wholeReplyOnly, out string argumentsJson)
    {
        argumentsJson = "{}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var candidates = !wholeReplyOnly
            ? EnumerateCandidates(text)
            : trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal)
                ? EnumerateCandidates(text).Skip(1).Take(1)
                : new[] { trimmed };

        foreach (var candidate in candidates)
        {
            if (!TryReadActionObject(candidate, out argumentsJson))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidates(string text)
    {
        var trimmed = text.Trim();
        yield return trimmed;

        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = trimmed.IndexOf('\n', fenceStart);
            var fenceEnd = trimmed.IndexOf("```", fenceStart + 3, StringComparison.Ordinal);
            if (contentStart >= 0 && fenceEnd > contentStart)
            {
                yield return trimmed[(contentStart + 1)..fenceEnd].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            yield return trimmed[start..(end + 1)];
        }
    }

    private static bool TryReadActionObject(string json, out string argumentsJson)
    {
        argumentsJson = "{}";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("action", out var action) ||
                action.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(action.GetString()))
            {
                return false;
            }

            argumentsJson = document.RootElement.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
