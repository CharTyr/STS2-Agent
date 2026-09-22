using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>One concrete action Jev could take right now, resolved to the indices the bridge needs.</summary>
/// <remarks>
/// A descriptor in <c>available_actions</c> names an action and whether it needs an index or a target;
/// an option is that descriptor filled in with a specific card/target/option. Jev chooses an option and
/// the decider maps it back to action+indices, so the fast model never has to invent an index against
/// the live payload -- that is the job this expansion does once, deterministically, per frame.
/// </remarks>
internal sealed record JevOption
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    public required string Action { get; init; }

    public int? CardIndex { get; init; }

    public int? TargetIndex { get; init; }

    public int? OptionIndex { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public string? Tool { get; init; }
}

/// <summary>
/// Expands a decision snapshot into the flat list of concrete options Jev picks from.
/// </summary>
/// <remarks>
/// The snapshot is one state read's <c>{"state": {...compact...}, "available_actions": [descriptors]}</c>.
/// Each descriptor is turned into as many options as its payload supports: a playable hand card becomes
/// one option per legal target, an indexed choice becomes one option per index in the matching compact
/// list, and a no-argument action becomes a single option. The primary compact list for each action is
/// the one <see cref="ActIndexValidator"/> judges against, so an enumerated option is one the validator
/// will accept. Everything is defensive: a field that is missing or the wrong type skips that option
/// rather than throwing, because a malformed frame is the wrong moment to crash the decision loop.
/// </remarks>
internal static class JevOptionEnumerator
{
    /// <summary>Hard cap on options; combat card plays are added first so they survive truncation.</summary>
    private const int MaxOptions = 255;

    /// <summary>Actions whose index lives in a potion slot, filtered by the slot's usable/discard flag.</summary>
    private static readonly HashSet<string> PotionActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "use_potion",
        "discard_potion"
    };

    /// <summary>
    /// The primary compact list an indexed action reads, keyed by action. This mirrors
    /// <see cref="ActIndexValidator.OptionPaths"/> on purpose -- an enumerated option has to be one the
    /// validator would accept -- but only the first (legal) path is used here: the validator unions
    /// fallback paths for membership, while an enumerator wants the actual selectable list, not every
    /// index that might satisfy it.
    /// </summary>
    private static readonly Dictionary<string, string[]> IndexedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["choose_map_node"] = new[] { "map", "options" },
        ["choose_event_option"] = new[] { "event", "options" },
        ["choose_reward_card"] = new[] { "reward", "cards" },
        ["claim_reward"] = new[] { "reward", "rewards" },
        ["select_deck_card"] = new[] { "selection", "cards" },
        ["select_character"] = new[] { "character_select", "characters" },
        ["buy_card"] = new[] { "shop", "cards" },
        ["buy_relic"] = new[] { "shop", "relics" },
        ["buy_potion"] = new[] { "shop", "potions" },
        ["choose_rest_option"] = new[] { "rest", "options" },
        ["choose_treasure_relic"] = new[] { "chest", "relics" },
        ["choose_capstone_option"] = new[] { "capstone", "options" },
        ["choose_bundle"] = new[] { "bundles" },
        ["choose_timeline_epoch"] = new[] { "timeline", "slots" }
    };

    /// <summary>Actions that are always expanded by a known index list even if the frame forgot the flag.</summary>
    private static readonly HashSet<string> AlwaysIndexed = new(IndexedPaths.Keys, StringComparer.OrdinalIgnoreCase);

    public static List<JevOption> Enumerate(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return new List<JevOption>();
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new List<JevOption>();
            }

            // The snapshot is {state, available_actions}; tolerate a bare state object as well.
            var state = root.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Object
                ? stateElement
                : root;
            var descriptors = ReadDescriptors(root, state);
            var options = new List<JevOption>();

            // Pass 1: combat card plays first, so they outlive the cap. Keyed on the descriptor name,
            // not its requires flag, so a snapshot that carries names without flags still expands.
            if (HasDescriptor(descriptors, "play_card"))
            {
                ExpandCombatCards(state, options);
            }

            // Pass 2: everything else, in descriptor order.
            foreach (var descriptor in descriptors)
            {
                var name = descriptor.Name;
                if (string.Equals(name, "play_card", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PotionActions.Contains(name))
                {
                    ExpandPotion(name, state, options);
                    continue;
                }

                var needsSomething = descriptor.RequiresIndex
                    || descriptor.RequiresTarget
                    || descriptor.RequiresCoordinates
                    || descriptor.RequiresTool
                    || AlwaysIndexed.Contains(name);
                if (!needsSomething)
                {
                    AddOption(options, name, name, description: name);
                    continue;
                }

                if (IndexedPaths.TryGetValue(name, out var path))
                {
                    ExpandIndexed(name, path, state, options);
                    continue;
                }

                // Requires an index/target/tool/coordinates we cannot expand from the compact payload
                // (an optional tool, a raw grid sheet, a potion on an exotic screen). Keep one selectable
                // option so Jev can still name the action; the validator rejects it if the frame changed.
                AddOption(options, name, name, description: name);
            }

            return DeduplicateAndCap(options);
        }
        catch (JsonException)
        {
            return new List<JevOption>();
        }
    }

    private static List<JevOption> DeduplicateAndCap(List<JevOption> options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var capped = new List<JevOption>(Math.Min(options.Count, MaxOptions));
        foreach (var option in options)
        {
            if (capped.Count >= MaxOptions)
            {
                break;
            }

            if (seen.Add(option.Id))
            {
                capped.Add(option);
            }
        }

        return capped;
    }

    private static void ExpandCombatCards(JsonElement state, List<JevOption> options)
    {
        if (!TryGetProperty(state, "combat", out var combat)
            || !TryGetArray(combat, "hand", out var hand))
        {
            return;
        }

        var enemyNames = ReadEnemyNames(combat);
        foreach (var card in hand.EnumerateArray())
        {
            if (card.ValueKind != JsonValueKind.Object
                || !TryReadIndex(card, out var cardIndex)
                || !ReadBoolOrAbsent(card, "playable"))
            {
                continue;
            }

            var line = ReadItemLine(card);
            var targets = ReadTargetList(card);
            if (targets.Count == 0)
            {
                AddOption(
                    options,
                    id: $"play_card:{cardIndex}",
                    action: "play_card",
                    description: string.IsNullOrEmpty(line) ? $"Play card {cardIndex}" : $"Play {line}",
                    cardIndex: cardIndex);
                continue;
            }

            foreach (var target in targets)
            {
                AddOption(
                    options,
                    id: $"play_card:{cardIndex}->{target}",
                    action: "play_card",
                    description: $"Play {TargetLabel(line, target, enemyNames)}",
                    cardIndex: cardIndex,
                    targetIndex: target);
            }
        }
    }

    private static void ExpandPotion(string action, JsonElement state, List<JevOption> options)
    {
        if (!TryGetProperty(state, "run", out var run) || !TryGetArray(run, "potions", out var potions))
        {
            return;
        }

        var requiredFlag = string.Equals(action, "use_potion", StringComparison.OrdinalIgnoreCase) ? "usable" : "discard";
        var enemyNames = ReadEnemyNames(TryGetProperty(state, "combat", out var combat) ? combat : default);
        foreach (var potion in potions.EnumerateArray())
        {
            if (potion.ValueKind != JsonValueKind.Object
                || !TryReadIndex(potion, out var potionIndex)
                || !ReadBoolOrAbsent(potion, requiredFlag))
            {
                continue;
            }

            var line = ReadItemLine(potion);
            var targets = ReadTargetList(potion);
            if (targets.Count == 0)
            {
                AddOption(
                    options,
                    id: $"{action}:{potionIndex}",
                    action: action,
                    description: DescribePotion(action, line, potionIndex),
                    optionIndex: potionIndex);
                continue;
            }

            foreach (var target in targets)
            {
                AddOption(
                    options,
                    id: $"{action}:{potionIndex}->{target}",
                    action: action,
                    description: DescribePotion(action, line, potionIndex) + $" → {TargetLabel(string.Empty, target, enemyNames)}",
                    optionIndex: potionIndex,
                    targetIndex: target);
            }
        }
    }

    private static void ExpandIndexed(string action, string[] path, JsonElement state, List<JevOption> options)
    {
        if (!TryGetArrayAt(state, path, out var array))
        {
            // The legal list is not in this frame's compact view; keep the action selectable anyway.
            AddOption(options, id: action, action: action, description: action);
            return;
        }

        var expanded = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryReadIndex(item, out var index)
                || IsLocked(item))
            {
                continue;
            }

            expanded++;
            var line = ReadItemLine(item);
            var targets = ReadTargetList(item);
            if (targets.Count == 0)
            {
                AddOption(
                    options,
                    id: $"{action}:{index}",
                    action: action,
                    description: string.IsNullOrEmpty(line) ? $"{action} {index}" : line,
                    optionIndex: index);
                continue;
            }

            foreach (var target in targets)
            {
                AddOption(
                    options,
                    id: $"{action}:{index}->{target}",
                    action: action,
                    description: string.IsNullOrEmpty(line) ? $"{action} {index} → target {target}" : $"{line} → target {target}",
                    optionIndex: index,
                    targetIndex: target);
            }
        }

        if (expanded == 0)
        {
            AddOption(options, id: action, action: action, description: action);
        }
    }

    private static string TargetLabel(string line, int target, Dictionary<int, string> enemyNames)
    {
        var who = enemyNames.TryGetValue(target, out var name) ? name : $"target {target}";
        return string.IsNullOrEmpty(line) ? who : $"{line} → {who}";
    }

    private static string DescribePotion(string action, string line, int potionIndex)
    {
        var what = string.IsNullOrEmpty(line) ? $"potion {potionIndex}" : line;
        return string.Equals(action, "use_potion", StringComparison.OrdinalIgnoreCase)
            ? $"Use {what}"
            : $"Discard {what}";
    }

    private static Dictionary<int, string> ReadEnemyNames(JsonElement combat)
    {
        var names = new Dictionary<int, string>();
        if (combat.ValueKind != JsonValueKind.Object || !TryGetArray(combat, "enemies", out var enemies))
        {
            return names;
        }

        foreach (var enemy in enemies.EnumerateArray())
        {
            if (enemy.ValueKind == JsonValueKind.Object
                && TryReadIndex(enemy, out var index)
                && !names.ContainsKey(index))
            {
                names[index] = ReadString(enemy, "name") ?? $"enemy {index}";
            }
        }

        return names;
    }

    private static List<ActionDescriptor> ReadDescriptors(JsonElement root, JsonElement state)
    {
        var descriptors = new List<ActionDescriptor>();
        AppendDescriptors(root, "available_actions", descriptors);
        if (descriptors.Count == 0)
        {
            // Degrade to the name-only list the compact state itself carries.
            if (state.TryGetProperty("available_actions", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in names.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var name = item.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            descriptors.Add(new ActionDescriptor(name, false, false, false, false));
                        }
                    }
                }
            }
        }

        return descriptors;
    }

    private static void AppendDescriptors(JsonElement root, string property, List<ActionDescriptor> descriptors)
    {
        if (!root.TryGetProperty(property, out var actions) || actions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in actions.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.Object:
                    var name = ReadString(item, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        descriptors.Add(new ActionDescriptor(
                            name,
                            ReadBool(item, "requires_index"),
                            ReadBool(item, "requires_target"),
                            ReadBool(item, "requires_coordinates"),
                            ReadBool(item, "requires_tool")));
                    }

                    break;
                case JsonValueKind.String:
                    var bare = item.GetString();
                    if (!string.IsNullOrWhiteSpace(bare))
                    {
                        descriptors.Add(new ActionDescriptor(bare, false, false, false, false));
                    }

                    break;
            }
        }
    }

    private static bool HasDescriptor(List<ActionDescriptor> descriptors, string name)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddOption(
        List<JevOption> options,
        string id,
        string action,
        string description,
        int? cardIndex = null,
        int? targetIndex = null,
        int? optionIndex = null)
    {
        options.Add(new JevOption
        {
            Id = id,
            Action = action,
            Description = description,
            CardIndex = cardIndex,
            TargetIndex = targetIndex,
            OptionIndex = optionIndex
        });
    }

    private static List<int> ReadTargetList(JsonElement item)
    {
        var targets = new List<int>();
        foreach (var field in new[] { "targets", "valid_target_indices" })
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(field, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var value in array.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index))
                {
                    targets.Add(index);
                }
            }

            break;
        }

        return targets;
    }

    private static bool IsLocked(JsonElement item)
    {
        return ReadBool(item, "locked") || ReadBool(item, "is_locked");
    }

    private static string ReadItemLine(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var field in new[] { "line", "text", "title", "name", "card_id" })
        {
            if (item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static bool ReadBoolOrAbsent(JsonElement item, string name)
    {
        // Absent reads as "allowed": a compact view that omits the flag keeps the option rather than
        // dropping a real playable card or potion.
        return !item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.False;
    }

    private static bool ReadBool(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryReadIndex(JsonElement item, out int index)
    {
        if (item.TryGetProperty("i", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out index))
        {
            return true;
        }

        if (item.TryGetProperty("index", out var typed) && typed.ValueKind == JsonValueKind.Number && typed.TryGetInt32(out index))
        {
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetArray(JsonElement parent, string name, out JsonElement array)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out array))
        {
            array = default;
            return false;
        }

        return array.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetArrayAt(JsonElement root, string[] path, out JsonElement array)
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

        return current.ValueKind == JsonValueKind.Array && (array = current).ValueKind == JsonValueKind.Array;
    }

    private readonly record struct ActionDescriptor(
        string Name,
        bool RequiresIndex,
        bool RequiresTarget,
        bool RequiresCoordinates,
        bool RequiresTool);
}
