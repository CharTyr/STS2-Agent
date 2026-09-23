using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// The LLM planner's standing guidance for the fast execution model: a posture, free-text
/// instructions, and per-option hints Jev reads when it weighs the concrete options a screen offers.
/// </summary>
/// <remarks>
/// This is the whole output of the slow half of the two-layer engine. The planner never names a
/// specific card index or target -- those are stale the moment the game advances a frame -- it only
/// shapes how Jev chooses among the concrete options <see cref="JevOptionEnumerator"/> expands from the
/// live snapshot. That keeps the planner's value (strategy) and the executor's job (this exact click)
/// from depending on the same frame.
/// <para>
/// JSON is tolerant on the way in (<see cref="TryParse"/> keeps defaults for any missing field) and
/// faithful on the way out (<see cref="ToJson"/>), so a persisted or MCP-delivered strategy survives a
/// round trip. Snake_case keys match the rest of the agent wire conventions.
/// </para>
/// </remarks>
internal sealed record PlayStrategy
{
    /// <summary>Who is allowed to write this strategy: "default", "llm", or "mcp".</summary>
    public const string DefaultSource = "default";

    /// <summary>The <see cref="UpdatedAt"/> value a never-planned strategy carries.</summary>
    public const string DefaultTimestamp = "1970-01-01T00:00:00Z";

    /// <summary>The overall bias: "balanced", "aggressive", or "defensive".</summary>
    public string Posture { get; init; } = "balanced";

    /// <summary>Free-text planner guidance Jev reads verbatim when it decides.</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>Per-option nudges keyed by <see cref="JevOption.Id"/>; an empty map means no bias.</summary>
    public IReadOnlyDictionary<string, string> OptionHints { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>When the planner wrote this, as an ISO-8601 timestamp.</summary>
    public string UpdatedAt { get; init; } = DefaultTimestamp;

    /// <summary>Which half of the engine produced this strategy: "default", "llm", or "mcp".</summary>
    public string Source { get; init; } = DefaultSource;

    /// <summary>The strategy the store starts at, before any planner has run.</summary>
    public static PlayStrategy Default { get; } = new();

    /// <summary>
    /// Parse a strategy, keeping a default for every field the JSON omits or mis-types. Returns null
    /// when the text is not a JSON object at all, so a caller can distinguish "no strategy" from "a
    /// partial one".
    /// </summary>
    public static PlayStrategy? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            return new PlayStrategy
            {
                Posture = ReadString(root, "posture") ?? Default.Posture,
                Instructions = ReadString(root, "instructions") ?? Default.Instructions,
                OptionHints = ReadOptionHints(root) ?? Default.OptionHints,
                UpdatedAt = ReadString(root, "updated_at") ?? Default.UpdatedAt,
                Source = ReadString(root, "source") ?? Default.Source
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The strategy as JSON, honoring the same keys <see cref="TryParse"/> reads.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            posture = Posture,
            instructions = Instructions,
            option_hints = OptionHints,
            updated_at = UpdatedAt,
            source = Source
        });
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, string>? ReadOptionHints(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("option_hints", out var hints)
            || hints.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in hints.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                dictionary[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return dictionary;
    }
}
