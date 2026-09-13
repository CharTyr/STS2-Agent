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
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "is_x_star_cost", "damage", "block", "keywords", "tags", "vars", "upgrade" },
            ["monsters"] = new[] { "id", "name", "type", "min_hp", "max_hp", "moves", "damage_values", "block_values" },
            ["powers"] = new[] { "id", "name", "description", "type", "stack_type", "allow_negative" },
            ["potions"] = new[] { "id", "name", "description", "rarity", "pool", "usage", "target_type" }
        },
        ["shop"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "is_x_star_cost", "keywords" },
            ["relics"] = new[] { "id", "name", "description", "rarity", "pool", "is_melted" },
            ["potions"] = new[] { "id", "name", "description", "rarity", "pool", "usage", "target_type" }
        },
        ["event"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["events"] = new[] { "id", "name", "type", "act", "description", "options" }
        }
    };

    /// <summary>
    /// Read-only view of <see cref="SceneFieldSets"/> for source-level contract tests. Kept
    /// internal so the filter's public surface does not grow.
    /// </summary>
    internal static IReadOnlyDictionary<string, Dictionary<string, string[]>> SceneFieldSetView => SceneFieldSets;

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

    /// <summary>
    /// Scene-scoped id sources for <see cref="DeriveRelevantItemIds"/>: (scene, collection) -> JSON
    /// paths into the full /state payload, each ending in the collection's id field. This is what
    /// makes <c>get_relevant_game_data</c> work without the caller naming ids: "the ids this screen
    /// is about". mcp_server/src/sts2_mcp/server.py mirrors it as _SCENE_ITEM_SOURCES and
    /// tests/test_scene_field_alignment.py keeps the two equal, the same way SceneFieldSets is kept.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Dictionary<string, string[]>> SceneItemSources =
        new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["combat"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards"] = new[] { "combat.hand[].card_id" },
                ["monsters"] = new[] { "combat.enemies[].enemy_id" },
                ["powers"] = new[] { "combat.player.powers[].power_id", "combat.enemies[].powers[].power_id" },
                ["potions"] = new[] { "run.potions[].potion_id" }
            },
            ["shop"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards"] = new[] { "shop.cards[].card_id" },
                ["relics"] = new[] { "shop.relics[].relic_id" },
                ["potions"] = new[] { "shop.potions[].potion_id" }
            },
            ["event"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["events"] = new[] { "event.event_id" }
            }
        };

    /// <summary>
    /// Used when the current scene declares no source for the collection: the run-level ids the
    /// player already owns, so a deck or relic lookup still answers on a screen that is about
    /// something else.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> FallbackItemSources =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "run.deck[].card_id" },
            ["relics"] = new[] { "run.relics[].relic_id" },
            ["potions"] = new[] { "run.potions[].potion_id" },
            ["monsters"] = new[] { "combat.enemies[].enemy_id" },
            ["powers"] = new[] { "combat.player.powers[].power_id", "combat.enemies[].powers[].power_id" },
            ["events"] = new[] { "event.event_id" }
        };

    /// <summary>
    /// The ids <c>get_relevant_game_data</c> should look up for the current screen, in the order the
    /// surface presents them, deduplicated. The scene decides first, and an empty scene answer --
    /// an unknown collection, or a screen whose scene payload is absent -- falls back to the
    /// run-level ids, so the result is empty only when neither source carries one. An empty answer
    /// is a legitimate "nothing to look up here", not an error.
    /// </summary>
    public static IReadOnlyList<string> DeriveRelevantItemIds(string? screen, string collection, JsonElement state)
    {
        var scene = DetectScene(screen);
        string[]? paths = null;
        if (SceneItemSources.TryGetValue(scene, out var perCollection))
        {
            perCollection.TryGetValue(collection, out paths);
        }

        var ids = CollectIdsFromPaths(state, paths);
        if (ids.Count == 0 && FallbackItemSources.TryGetValue(collection, out var fallbackPaths))
        {
            // A scene can name a source whose payload is absent on that screen: FAKE_MERCHANT is
            // classified as shop, but the shop payload is null there because the merchant room the
            // ids come from does not exist. An empty scene answer therefore still falls back to the
            // run-level ids instead of reporting nothing at all.
            ids = CollectIdsFromPaths(state, fallbackPaths);
        }

        return ids;
    }

    /// <summary>
    /// Runs every path of one source list over the state, deduplicated and in source order.
    /// </summary>
    private static List<string> CollectIdsFromPaths(JsonElement state, IReadOnlyList<string>? paths)
    {
        var ids = new List<string>();
        if (paths == null)
        {
            return ids;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            CollectIds(state, path.Split('.'), 0, ids, seen);
        }

        return ids;
    }

    /// <summary>
    /// Walks one <c>a.b[].c</c> path. A missing property or a wrong JSON kind ends that branch
    /// quietly: state shapes vary by screen, and an absent id is not an error.
    /// </summary>
    private static void CollectIds(
        JsonElement node,
        IReadOnlyList<string> tokens,
        int index,
        List<string> ids,
        HashSet<string> seen)
    {
        // A null or scalar node ends the branch: screens carry null payloads (shop on FAKE_MERCHANT,
        // run.potions slots), and walking into one must not throw. Mirrors the dict guard in
        // mcp_server/src/sts2_mcp/server.py _collect_path_ids.
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var token = tokens[index];
        var isArray = token.EndsWith("[]", StringComparison.Ordinal);
        var name = isArray ? token[..^2] : token;
        if (!node.TryGetProperty(name, out var value))
        {
            return;
        }

        if (index == tokens.Count - 1)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrEmpty(text) && seen.Add(text))
                {
                    ids.Add(text);
                }
            }

            return;
        }

        if (isArray)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in value.EnumerateArray())
            {
                CollectIds(item, tokens, index + 1, ids, seen);
            }

            return;
        }

        CollectIds(value, tokens, index + 1, ids, seen);
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
