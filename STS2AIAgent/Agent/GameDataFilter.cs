using System.Text.Json;

namespace STS2AIAgent.Agent;

internal static class GameDataFilter
{
    public static readonly string[] KnownCollections =
    {
        "cards", "relics", "monsters", "potions", "events", "powers", "characters"
    };

    private static readonly Dictionary<string, Dictionary<string, string[]>> SceneFieldSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["combat"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "damage", "block", "keywords", "tags", "vars", "upgrade" },
            ["monsters"] = new[] { "id", "name", "type", "hp", "moves", "damage", "block" },
            ["powers"] = new[] { "id", "name", "description", "type", "stack_type" }
        },
        ["shop"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "cost", "keywords" },
            ["relics"] = new[] { "id", "name", "description", "rarity" },
            ["potions"] = new[] { "id", "name", "description", "rarity", "target" }
        },
        ["event"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["events"] = new[] { "id", "name", "description", "options" }
        }
    };

    public static string DetectScene(string? screen)
    {
        var value = screen?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Contains("shop", StringComparison.Ordinal) || value.Contains("merchant", StringComparison.Ordinal))
        {
            return "shop";
        }

        if (value.Contains("event", StringComparison.Ordinal))
        {
            return "event";
        }

        if (value.Contains("combat", StringComparison.Ordinal))
        {
            return "combat";
        }

        return "menu";
    }

    public static JsonElement? FindItem(JsonElement collection, string itemId)
    {
        if (collection.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in collection.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetId(item, out var id) && string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase))
            {
                return item.Clone();
            }
        }

        return null;
    }

    public static Dictionary<string, JsonElement?> FindItems(JsonElement collection, IEnumerable<string> itemIds)
    {
        var result = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemId in itemIds)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            result[itemId] = FindItem(collection, itemId);
        }

        return result;
    }

    public static Dictionary<string, JsonElement?> ProjectRelevant(
        string screen,
        string collection,
        JsonElement source,
        IEnumerable<string> itemIds)
    {
        var items = FindItems(source, itemIds);
        var scene = DetectScene(screen);
        if (!SceneFieldSets.TryGetValue(scene, out var fieldSets) ||
            !fieldSets.TryGetValue(collection, out var fields))
        {
            return items;
        }

        var projected = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in items)
        {
            projected[pair.Key] = pair.Value is { } element ? ProjectFields(element, fields) : null;
        }

        return projected;
    }

    public static IReadOnlyList<string> ParseItemIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static JsonElement ProjectFields(JsonElement item, IReadOnlyList<string> fields)
    {
        var buffer = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (item.TryGetProperty(field, out var value))
            {
                buffer[field] = value.Clone();
            }
        }

        return JsonSerializer.SerializeToElement(buffer);
    }

    private static bool TryGetId(JsonElement item, out string id)
    {
        foreach (var key in new[] { "id", "ID", "Id" })
        {
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                id = value.GetString() ?? string.Empty;
                return id.Length > 0;
            }
        }

        id = string.Empty;
        return false;
    }
}
