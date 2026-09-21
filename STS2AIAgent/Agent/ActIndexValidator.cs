using System.Text.Json;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

/// <summary>
/// One rejected act index: which field was wrong, what was submitted, and the indices the latest
/// payload actually offers.
/// </summary>
/// <remarks>
/// The validator used to answer with a bare message string, and the envelope around it carried only
/// an echo of what the caller had just sent. A model that guessed a stale <c>card_index</c> learned
/// that it was wrong and nothing about what would have been right, while the "action is not
/// available" answer next to it already carried <c>available_actions</c>.
///
/// The indices are free here: deciding that an index is stale means the validator is already holding
/// the payload that lists the good ones. <see cref="ToApiException"/> renders this as the same error
/// object the HTTP boundary sends (<c>code</c>, <c>message</c>, <c>details</c>, <c>retryable</c>), so
/// the HTTP API, the in-game loop, and the native MCP tool all answer an index mistake with one
/// shape -- and with the human-readable message unchanged, as <c>error.message</c>.
/// </remarks>
internal sealed record ActIndexError(
    int StatusCode,
    string Code,
    string Message,
    string Field,
    int? Submitted,
    string? ValidField,
    IReadOnlyList<int> ValidIndices,
    bool Locked = false)
{
    /// <summary>
    /// The shared error envelope for this rejection, with the submitted indexes appended as the
    /// surfaces have always echoed them.
    /// </summary>
    public ApiException ToApiException(string action, IReadOnlyList<string>? availableActions = null) =>
        new(StatusCode, Code, Message, new
        {
            action,
            field = Field,
            submitted = Submitted,
            valid_field = ValidField,
            valid_indices = ValidIndices,
            locked = Locked,
            available_actions = availableActions
        });
}

internal static class ActIndexValidator
{
    /// <summary>Where an action's option index lives in the compact state, primary path first.</summary>
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
        ["choose_timeline_epoch"] = new[] { new[] { "timeline", "slots" }, new[] { "timeline", "epochs" }, new[] { "timeline", "options" } },
        ["use_potion"] = new[] { new[] { "run", "potions" } },
        ["discard_potion"] = new[] { new[] { "run", "potions" } }
    };

    private const string HandPath = "combat.hand";

    private const string HandTargetPath = "combat.hand[].targets";

    /// <summary>
    /// The index rejection for these parameters, or null when they are usable. Every rejection names
    /// the offending field, the submitted value, and the indices the latest payload offers for it.
    /// </summary>
    public static ActIndexError? Validate(
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

        // The payload is read before the parameters are judged, because every answer below reports
        // the indices the caller could have used and those only exist in the payload. A payload that
        // cannot be parsed still leaves the "must come from the latest payload" answers intact, so
        // the missing-parameter checks do not depend on it.
        var state = TryParse(compactStateJson);
        try
        {
            var root = state?.RootElement ?? default;
            var optionList = playCard
                ? IndexListAt(root, OptionPaths["play_card"])
                : IndexListAt(root, OptionPaths.TryGetValue(action, out var paths) ? paths : null);
            var targetList = playCard && cardIndex is int card
                ? ReadCardTargets(root, card) ?? Array.Empty<int>()
                : Array.Empty<int>();

            if (requiresIndex && index is null)
            {
                return playCard
                    ? Missing("card_index must come from the latest combat.hand.", "card_index", HandPath, optionList.Indices)
                    : Missing("option_index must come from the latest payload.", "option_index", optionList.Field, optionList.Indices);
            }

            if (requiresTarget && targetIndex is null)
            {
                return Missing("target_index must come from the latest payload.", "target_index", HandTargetPath, targetList);
            }

            if (state is null)
            {
                return null;
            }

            if (playCard && cardIndex is int playedCard)
            {
                if (!optionList.Indices.Contains(playedCard))
                {
                    return OutOfRange($"card_index {playedCard} is not in the latest combat.hand.", "card_index", playedCard, HandPath, optionList.Indices);
                }

                if (targetList.Count > 0)
                {
                    if (targetIndex is null)
                    {
                        return Missing("target_index must come from the latest payload for this card.", "target_index", HandTargetPath, targetList);
                    }

                    if (!targetList.Contains(targetIndex.Value))
                    {
                        return OutOfRange($"target_index {targetIndex.Value} is not in the latest targets for card {playedCard}.", "target_index", targetIndex, HandTargetPath, targetList);
                    }
                }
            }
            else if (!playCard && index is int option && optionList.Found)
            {
                if (!optionList.Indices.Contains(option))
                {
                    return OutOfRange($"option_index {option} is not in the latest payload for {action}.", "option_index", option, optionList.Field, optionList.Indices);
                }

                if (string.Equals(action, "choose_event_option", StringComparison.OrdinalIgnoreCase) &&
                    IsLockedIndex(root, OptionPaths[action], option))
                {
                    return OutOfRange($"option_index {option} is locked.", "option_index", option, optionList.Field, optionList.Indices, locked: true);
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

    /// <summary>A required parameter was not submitted, so nothing was out of range yet.</summary>
    private static ActIndexError Missing(string message, string field, string? validField, IReadOnlyList<int> validIndices) =>
        new(400, "invalid_request", message, field, null, validField, validIndices);

    /// <summary>The submitted index is not one the latest payload offers.</summary>
    private static ActIndexError OutOfRange(
        string message,
        string field,
        int? submitted,
        string? validField,
        IReadOnlyList<int> validIndices,
        bool locked = false) =>
        new(409, "invalid_target", message, field, submitted, validField, validIndices, locked);

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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

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

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ReadBool(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Every index one action's payload offers, plus the path it came from.
    /// </summary>
    /// <remarks>
    /// The union across the action's paths is what the membership check below accepts, so it is also
    /// what a rejection has to report: naming fewer indices than the validator would have taken
    /// would send the caller looking for an index that was fine all along. <see cref="Field"/> names
    /// the primary path it read, for a caller that has to re-read the payload itself.
    /// </remarks>
    private readonly record struct IndexList(string Field, IReadOnlyList<int> Indices, bool Found);

    private static IndexList IndexListAt(JsonElement root, IReadOnlyList<string[]>? paths)
    {
        if (paths == null || paths.Count == 0 || root.ValueKind != JsonValueKind.Object)
        {
            return new IndexList(string.Empty, Array.Empty<int>(), false);
        }

        var field = string.Empty;
        var found = false;
        var indices = new SortedSet<int>();
        foreach (var path in paths)
        {
            if (!TryGetArray(root, path, out var array))
            {
                continue;
            }

            if (!found)
            {
                field = string.Join(".", path);
            }

            found = true;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && TryReadIndex(item, out var value))
                {
                    indices.Add(value);
                }
            }
        }

        return new IndexList(field, indices.ToArray(), found);
    }

    private static bool IsLockedIndex(JsonElement root, IReadOnlyList<string[]> paths, int index)
    {
        var item = FindIndexedItem(root, paths, index);
        if (item == null)
        {
            return false;
        }

        return ReadLockedFlag(item.Value);
    }

    private static JsonElement? FindIndexedItem(JsonElement root, IReadOnlyList<string[]> paths, int index)
    {
        foreach (var path in paths)
        {
            if (!TryGetArray(root, path, out var array))
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryReadIndex(item, out var value) && value == index)
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static bool TryReadIndex(JsonElement item, out int value)
    {
        if (item.TryGetProperty("i", out var i) && i.TryGetInt32(out value))
        {
            return true;
        }

        if (item.TryGetProperty("index", out var index) && index.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool ReadLockedFlag(JsonElement item)
    {
        if (item.TryGetProperty("locked", out var locked) && locked.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return item.TryGetProperty("is_locked", out var isLocked) && isLocked.ValueKind == JsonValueKind.True;
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
