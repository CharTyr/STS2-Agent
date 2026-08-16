using System.Text.Json;

namespace STS2AIAgent.Agent;

internal static class ActIndexValidator
{
    private static readonly Dictionary<string, string[][]> OptionPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["play_card"] = new[] { new[] { "combat", "hand" } },
        ["choose_map_node"] = new[] { new[] { "map", "options" }, new[] { "map", "nodes" } },
        ["choose_event_option"] = new[] { new[] { "event", "options" } },
        ["choose_reward_card"] = new[] { new[] { "reward", "cards" } },
        ["claim_reward"] = new[] { new[] { "reward", "rewards" } },
        ["resolve_rewards"] = new[] { new[] { "reward", "rewards" }, new[] { "reward", "alternatives" } },
        ["select_deck_card"] = new[] { new[] { "selection", "cards" } },
        ["select_character"] = new[] { new[] { "character_select", "characters" }, new[] { "multiplayer_lobby", "characters" } },
        ["buy_card"] = new[] { new[] { "shop", "cards" } },
        ["buy_relic"] = new[] { new[] { "shop", "relics" } },
        ["buy_potion"] = new[] { new[] { "shop", "potions" } },
        ["choose_rest_option"] = new[] { new[] { "rest", "options" } },
        ["choose_treasure_relic"] = new[] { new[] { "chest", "relics" }, new[] { "chest", "options" } },
        ["choose_capstone_option"] = new[] { new[] { "capstone", "options" } },
        ["choose_bundle"] = new[] { new[] { "bundles" } },
        ["choose_timeline_epoch"] = new[] { new[] { "timeline", "epochs" }, new[] { "timeline", "options" } },
        ["use_potion"] = new[] { new[] { "run", "potions" } },
        ["discard_potion"] = new[] { new[] { "run", "potions" } }
    };

    public static string? Validate(
        string action,
        int? cardIndex,
        int? targetIndex,
        int? optionIndex,
        string availableActionsJson,
        string compactStateJson)
    {
        var requiresIndex = false;
        var requiresTarget = false;
        try
        {
            using var actions = JsonDocument.Parse(string.IsNullOrWhiteSpace(availableActionsJson) ? "[]" : availableActionsJson);
            if (actions.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in actions.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    if (!string.Equals(name, action, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    requiresIndex = ReadBool(item, "requires_index");
                    requiresTarget = ReadBool(item, "requires_target");
                    break;
                }
            }
        }
        catch (JsonException)
        {
        }

        var playCard = string.Equals(action, "play_card", StringComparison.OrdinalIgnoreCase);
        var index = playCard ? cardIndex : optionIndex;

        if (requiresIndex && index is null)
        {
            return playCard
                ? "card_index must come from the latest combat.hand payload."
                : "option_index must come from the latest payload.";
        }

        if (requiresTarget && targetIndex is null)
        {
            return "target_index must come from the latest payload.";
        }

        JsonDocument? state = null;
        try
        {
            state = JsonDocument.Parse(string.IsNullOrWhiteSpace(compactStateJson) ? "{}" : compactStateJson);
            var root = state.RootElement;

            if (playCard && cardIndex is int card)
            {
                if (!ContainsIndex(root, OptionPaths["play_card"], card))
                {
                    return $"card_index {card} is not in the latest combat.hand.";
                }

                var targets = ReadCardTargets(root, card);
                if (targets is { Count: > 0 })
                {
                    if (targetIndex is null)
                    {
                        return "target_index must come from the latest payload for this card.";
                    }

                    if (!targets.Contains(targetIndex.Value))
                    {
                        return $"target_index {targetIndex.Value} is not in the latest targets for card {card}.";
                    }
                }
            }
            else if (index is int option)
            {
                if (OptionPaths.TryGetValue(action, out var paths) &&
                    HasAnyArray(root, paths) &&
                    !ContainsIndex(root, paths, option))
                {
                    return $"option_index {option} is not in the latest payload for {action}.";
                }
            }
        }
        catch (JsonException)
        {
        }
        finally
        {
            state?.Dispose();
        }

        return null;
    }

    public static bool IsUnsettled(string? actResultJson)
    {
        if (string.IsNullOrWhiteSpace(actResultJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(actResultJson);
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                string.Equals(status.GetString(), "pending", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (root.TryGetProperty("stable", out var stable) &&
                stable.ValueKind is JsonValueKind.False)
            {
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static bool ReadBool(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static bool HasAnyArray(JsonElement root, IReadOnlyList<string[]> paths)
    {
        foreach (var path in paths)
        {
            if (TryGetArray(root, path, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIndex(JsonElement root, IReadOnlyList<string[]> paths, int index)
    {
        foreach (var path in paths)
        {
            if (!TryGetArray(root, path, out var array))
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("i", out var i) &&
                    i.TryGetInt32(out var value) &&
                    value == index)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<int>? ReadCardTargets(JsonElement root, int cardIndex)
    {
        if (!TryGetArray(root, new[] { "combat", "hand" }, out var hand))
        {
            return null;
        }

        foreach (var card in hand.EnumerateArray())
        {
            if (card.ValueKind != JsonValueKind.Object ||
                !card.TryGetProperty("i", out var i) ||
                !i.TryGetInt32(out var value) ||
                value != cardIndex)
            {
                continue;
            }

            if (!card.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<int>();
            }

            var list = new List<int>();
            foreach (var target in targets.EnumerateArray())
            {
                if (target.TryGetInt32(out var targetIndex))
                {
                    list.Add(targetIndex);
                }
            }

            return list;
        }

        return null;
    }

    private static bool TryGetArray(JsonElement root, IReadOnlyList<string> path, out JsonElement array)
    {
        array = default;
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        array = current;
        return true;
    }
}
