using System.Text.Json;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentLoop
{
    private static JsonDocument ParseArgs(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return JsonDocument.Parse("{}");
        }

        var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new JsonException("Tool arguments must be a JSON object.");
        }
        return document;
    }

    /// <summary>
    /// Reads the model's optional one-sentence rationale from an act's arguments. The reason is
    /// surfaced as the decision's <see cref="AgentTurnResult.Reasoning"/> so the overlay and any
    /// future decision log show why the agent acted, including for models that never emit
    /// reasoning_content of their own.
    /// </summary>
    internal static string? TryReadActReason(string? argumentsJson)
    {
        try
        {
            using var args = ParseArgs(argumentsJson);
            var reason = ReadString(args, "reason")?.Trim();
            return string.IsNullOrWhiteSpace(reason) ? null : reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private static int? ReadInt(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>A boolean tool argument, false when it is absent or unreadable.</summary>
    private static bool ReadBool(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String &&
               bool.TryParse(value.GetString(), out var parsed) &&
               parsed;
    }

    /// <summary>
    /// One member of a parsed action snapshot, or an empty value of the expected kind.
    /// </summary>
    /// <remarks>
    /// A snapshot that is missing a half has to degrade to "nothing is available" rather than throw
    /// out of the act path: the caller is deciding whether to touch the game, and a malformed read
    /// is the wrong moment to guess.
    /// </remarks>
    private static JsonElement ReadSnapshotPart(JsonDocument snapshot, string name, JsonValueKind kind)
    {
        if (snapshot.RootElement.ValueKind == JsonValueKind.Object &&
            snapshot.RootElement.TryGetProperty(name, out var value) &&
            value.ValueKind == kind)
        {
            return value;
        }

        return kind == JsonValueKind.Array
            ? JsonSerializer.SerializeToElement(Array.Empty<string>(), JsonOptions)
            : JsonSerializer.SerializeToElement(new { }, JsonOptions);
    }

    /// <summary>The names a snapshot's own state reports; the descriptors come from the same walk.</summary>
    private static IReadOnlyList<string> ReadActionNames(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object ||
            !state.TryGetProperty("available_actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var item in actions.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                names.Add(item.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static double ReadTimeoutSeconds(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("timeout_seconds", out var value))
        {
            return 20;
        }

        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 20
        };

        return Math.Clamp(seconds, 1, 120);
    }
}
