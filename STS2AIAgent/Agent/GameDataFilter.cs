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
        },
        // A card being offered is evaluated the same way whether it sits in a shop, behind a reward
        // screen, in a bundle or in a deck-selection grid: cost, type, rarity and description decide
        // it. These three scenes therefore share the shop card set instead of combat's larger one --
        // the combat-only damage/block/vars/upgrade fields would ride along on every offer lookup,
        // which is exactly the bloat the projection exists to avoid. The chest shares the shop relic
        // set for the same reason. Gold/potion/relic reward rows carry no stable id, so those scenes
        // declare no collection for them and the run-level fallback answers instead of a guessed id.
        // Mirrors _SCENE_FIELD_SETS in mcp_server/src/sts2_mcp/game_data.py.
        ["reward"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "is_x_star_cost", "keywords" }
        },
        ["card_selection"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "is_x_star_cost", "keywords" }
        },
        ["bundle_selection"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = new[] { "id", "name", "description", "type", "rarity", "target", "cost", "is_x_cost", "star_cost", "is_x_star_cost", "keywords" }
        },
        ["chest"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["relics"] = new[] { "id", "name", "description", "rarity", "pool", "is_melted" }
        }
    };

    /// <summary>
    /// Read-only view of <see cref="SceneFieldSets"/> for source-level contract tests. Kept
    /// internal so the filter's public surface does not grow.
    /// </summary>
    internal static IReadOnlyDictionary<string, Dictionary<string, string[]>> SceneFieldSetView => SceneFieldSets;

    /// <summary>
    /// Classifies one /state screen name into the scene its metadata is scoped by.
    ///
    /// The checks run in a deliberate order, and the order is part of the contract:
    /// "COMBAT_REWARD" carries the reward keyword and is claimed by the combat rule that runs first,
    /// because the combat screen is the one that names its payload <c>combat</c>; and a card
    /// selection matches the full <c>card_selection</c> token so "BUNDLE_SELECTION" cannot be
    /// swallowed by a broader "selection" keyword. Mirrors _detect_scene_from_screen in
    /// mcp_server/src/sts2_mcp/game_data.py; GameDataFilterTests.DetectScene_MatchesGuidedMcpRules
    /// pins the mapping screen by screen and tests/test_scene_field_alignment.py reads that table
    /// back, so the two sides cannot classify the same screen differently.
    /// </summary>
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

        // Offered-choice screens the tool used to classify as the generic menu scene, which made
        // "the ids this screen is about" answer with the deck and relics the player already owns.
        // Only reached after the combat rule, so a combat-reward name stays a combat screen.
        if (value.Contains("reward", StringComparison.Ordinal))
        {
            return "reward";
        }

        if (value.Contains("card_selection", StringComparison.Ordinal))
        {
            return "card_selection";
        }

        if (value.Contains("chest", StringComparison.Ordinal))
        {
            return "chest";
        }

        if (value.Contains("bundle_selection", StringComparison.Ordinal))
        {
            return "bundle_selection";
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
    /// is about". mcp_server/src/sts2_mcp/game_data.py mirrors it as _SCENE_ITEM_SOURCES and
    /// tests/test_scene_field_alignment.py keeps the two equal, the same way SceneFieldSets is kept.
    ///
    /// The collection column is what keeps the answer honest: a screen that is about cards declares
    /// paths only under "cards", so a relic lookup on a card screen can never come back with card
    /// ids (and the other way round). A screen whose offered collection has no stable id at all --
    /// the gold/potion/relic rows of a reward screen -- declares nothing and falls through to the
    /// run-level fallback rather than inventing an id.
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
                // All three stock lists answered from the offer ids the shelf carries: the raw /state
                // payload's own card_id / relic_id / potion_id fields. The compact agent_view drops
                // the relic and potion ids (it keeps only the display line), which is why the raw
                // path is the one listed -- it is present whenever the shelf is.
                ["cards"] = new[] { "shop.cards[].card_id" },
                ["relics"] = new[] { "shop.relics[].relic_id" },
                ["potions"] = new[] { "shop.potions[].potion_id" }
            },
            ["event"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["events"] = new[] { "event.event_id" }
            },
            // Offered cards and relics are named differently by the two views one /state response
            // carries: the raw payload says reward.card_options[] / chest.relic_options[], the
            // embedded compact agent_view says reward.cards[] / chest.relics[]. Both are listed (raw
            // first: it is the list the screen builders write, and it is never absent while the
            // screen is up) so a caller handing over either view gets the made offer rather than the
            // deck the player already owns.
            ["reward"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards"] = new[] { "reward.card_options[].card_id", "agent_view.reward.cards[].card_id" }
            },
            ["card_selection"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards"] = new[] { "selection.cards[].card_id", "agent_view.selection.cards[].card_id" }
            },
            ["chest"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["relics"] = new[] { "chest.relic_options[].relic_id", "agent_view.chest.relics[].relic_id" }
            },
            ["bundle_selection"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards"] = new[] { "bundles[].cards[].card_id", "agent_view.bundles[].cards[].card_id" }
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
    /// surface presents them, deduplicated. The scene picks the source and the source is looked up
    /// <em>by collection</em>: a screen that offers cards can never answer a relic question with card
    /// ids, and a collection the screen does not offer answers from the run-level ids the player
    /// already owns. That fallback runs only when the scene's own source yields nothing -- an unknown
    /// collection, a screen whose scene payload is absent, or a collection this screen does not offer.
    /// An empty answer is a legitimate "nothing to look up here", not an error.
    ///
    /// The collection name is matched case-insensitively, as the dictionaries are; the tool is the
    /// one place a caller spells it by hand, so "Cards" reaches the same source as "cards".
    /// </summary>
    public static IReadOnlyList<string> DeriveRelevantItemIds(string? screen, string collection, JsonElement state)
    {
        var scene = DetectScene(screen);
        var collectionKey = (collection ?? string.Empty).Trim();
        string[]? paths = null;
        if (SceneItemSources.TryGetValue(scene, out var perCollection))
        {
            perCollection.TryGetValue(collectionKey, out paths);
        }

        var ids = CollectIdsFromPaths(state, paths);
        if (ids.Count == 0 && FallbackItemSources.TryGetValue(collectionKey, out var fallbackPaths))
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
