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
/// The planner's per-option hints after being bound to one frame. <see cref="OptionHints"/> is keyed
/// by live <see cref="JevOption.Id"/> only, and <see cref="DroppedKeys"/> names the hints that matched
/// neither a live option nor a live option kind and were therefore not forwarded at all.
/// </summary>
internal readonly record struct AlignedHints(
    IReadOnlyDictionary<string, string> OptionHints,
    IReadOnlyList<string> DroppedKeys);

/// <summary>The part of a snapshot a standing plan is compared against.</summary>
/// <remarks>
/// <see cref="Round"/> is the combat round counter, which restarts at 1 for each encounter; a frame
/// whose round is below the round a plan recorded proves that plan predates the current encounter.
/// </remarks>
internal readonly record struct FrameScope(string? Screen, int? Round);

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

    /// <summary>
    /// Hard cap on the hints one request may carry after re-keying. A kind hint can fan out over every
    /// option of that kind (255 at the enumerator's own ceiling), and a request that repeats a nudge
    /// hundreds of times costs more than the guide is worth; options past the cap keep no nudge.
    /// </summary>
    internal const int MaxAlignedHintEntries = 64;

    /// <summary>The option kind an enumerated option belongs to: its action name.</summary>
    internal static string KindOf(JevOption option) => option.Action;

    /// <summary>
    /// Reads the screen and combat round out of a snapshot, tolerantly: a truncated or non-JSON summary
    /// yields nulls rather than throwing, because the planner's own state read is capped and may cut
    /// the JSON mid-object.
    /// </summary>
    internal static FrameScope ReadScope(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return new FrameScope(null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new FrameScope(null, null);
            }

            var state = root.TryGetProperty("state", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            var screen = ReadString(state, "screen");
            int? round = state.TryGetProperty("turn", out var turn)
                && turn.ValueKind == JsonValueKind.Number
                && turn.TryGetInt32(out var value)
                && value >= 0
                ? value
                : null;
            return new FrameScope(string.IsNullOrWhiteSpace(screen) ? null : screen, round);
        }
        catch (JsonException)
        {
            // The planner's summary is the compact state cut at 4,000 characters, which is not a
            // whole document on nearly every combat frame. `screen` and `turn` sit at the head of it,
            // so read forward until the cut instead of giving up on the whole plan scope.
            return ReadScopePrefix(snapshotJson);
        }
    }

    /// <summary>
    /// Reads <c>screen</c> / <c>turn</c> from the head of a possibly truncated JSON object, at the root
    /// or inside a root <c>state</c> object, stopping at the first token the cut makes unreadable.
    /// </summary>
    private static FrameScope ReadScopePrefix(string json)
    {
        string? screen = null;
        int? round = null;
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), isFinalBlock: false, state: default);
        var scopeDepth = 1;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != scopeDepth)
                {
                    continue;
                }

                var name = reader.GetString();
                if (!reader.Read())
                {
                    break;
                }

                if (name == "state" && scopeDepth == 1 && reader.TokenType == JsonTokenType.StartObject)
                {
                    scopeDepth = 2;
                }
                else if (name == "screen" && reader.TokenType == JsonTokenType.String)
                {
                    screen = reader.GetString();
                }
                else if (name == "turn" && reader.TokenType == JsonTokenType.Number
                         && reader.TryGetInt32(out var value) && value >= 0)
                {
                    round = value;
                }

                if (screen != null && round != null)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // The cut: keep whatever was read before it.
        }

        return new FrameScope(string.IsNullOrWhiteSpace(screen) ? null : screen, round);
    }

    /// <summary>
    /// Binds a strategy's option hints to the concrete options of one frame.
    /// </summary>
    /// <remarks>
    /// The planner speaks in option KINDS -- its prompt and the MCP strategy schema both say "keyed by
    /// option kind" -- while the choice question it guides is keyed by concrete option IDS
    /// (<c>play_card:0-&gt;1</c>). Nothing used to bridge the two, so a kind-keyed nudge named no
    /// criterion at all and a concrete nudge left over from an earlier frame was forwarded as though the
    /// option still existed. This re-keys both onto the ids this frame actually offers:
    /// <list type="number">
    /// <item>a hint that already names a live option id is kept as it is (the most specific guidance);</item>
    /// <item>a hint that names a live option kind fans out over that kind's live ids, in frame order, up
    /// to <see cref="MaxAlignedHintEntries"/>;</item>
    /// <item>anything else is dropped and reported rather than sent, because an id Jev cannot choose is
    /// not guidance.</item>
    /// </list>
    /// The result is the enforced invariant: every key of <see cref="AlignedHints.OptionHints"/> is a
    /// key of the same request's <c>criteria</c>.
    /// </remarks>
    internal static AlignedHints AlignHints(
        IReadOnlyDictionary<string, string>? hints,
        IReadOnlyList<JevOption> options)
    {
        var aligned = new Dictionary<string, string>(StringComparer.Ordinal);
        var dropped = new List<string>();
        if (hints == null || hints.Count == 0)
        {
            return new AlignedHints(aligned, dropped);
        }

        var liveIds = new HashSet<string>(StringComparer.Ordinal);
        var idsByKind = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            liveIds.Add(option.Id);
            var kind = KindOf(option);
            if (!idsByKind.TryGetValue(kind, out var ids))
            {
                ids = new List<string>();
                idsByKind[kind] = ids;
            }

            ids.Add(option.Id);
        }

        // Pass 1: concrete ids first, so a kind-wide nudge can never overwrite a specific one.
        var kindHints = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in hints)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (liveIds.Contains(key))
            {
                // Past the cap a live id is still reported, and the scan goes on: breaking here used
                // to drop every later key -- unknown ones included -- without a trace in DroppedKeys.
                if (aligned.Count >= MaxAlignedHintEntries)
                {
                    dropped.Add(key);
                    continue;
                }

                aligned[key] = value;
                continue;
            }

            if (idsByKind.ContainsKey(key))
            {
                kindHints.Add(new KeyValuePair<string, string>(key, value));
                continue;
            }

            dropped.Add(key);
        }

        // Pass 2: a kind hint reaches every live option of that kind.
        foreach (var (key, value) in kindHints)
        {
            foreach (var id in idsByKind[key])
            {
                if (aligned.Count >= MaxAlignedHintEntries)
                {
                    return new AlignedHints(aligned, dropped);
                }

                aligned.TryAdd(id, value);
            }
        }

        return new AlignedHints(aligned, dropped);
    }

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

                // resolve_rewards is a flow macro, not a concrete pick: left bare it executes with no
                // index and the game side takes the first card by default. Expand it over the card
                // reward list so choosing it is a real deck-building decision, with an explicit skip.
                if (string.Equals(name, "resolve_rewards", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandResolveRewards(state, options);
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

                // Missing required arguments cannot form a legal option. Let the LLM fallback
                // inspect that screen rather than paying Jev to choose an unusable placeholder.
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
            return;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryReadIndex(item, out var index)
                || IsLocked(item)
                || IsUnavailable(action, item))
            {
                continue;
            }

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

    }

    private static void ExpandResolveRewards(JsonElement state, List<JevOption> options)
    {
        // reward.cards is the card-reward list in the compact view; each entry carries i and line.
        // When no card choice is pending the list is empty and the macro stays a single bare option
        // (draining gold/relics/potions only, which has no card to decide).
        if (TryGetProperty(state, "reward", out var reward) && TryGetArray(reward, "cards", out var cards))
        {
            var expanded = 0;
            foreach (var card in cards.EnumerateArray())
            {
                if (card.ValueKind != JsonValueKind.Object || !TryReadIndex(card, out var index))
                {
                    continue;
                }

                expanded++;
                var line = ReadItemLine(card);
                AddOption(
                    options,
                    id: $"resolve_rewards:{index}",
                    action: "resolve_rewards",
                    description: string.IsNullOrEmpty(line) ? $"Take card {index} and finish rewards" : $"Take {line}",
                    optionIndex: index);
            }

            if (expanded > 0)
            {
                AddOption(
                    options,
                    id: "resolve_rewards:skip",
                    action: "resolve_rewards",
                    description: "Skip the card reward and finish rewards",
                    optionIndex: -1);
                return;
            }
        }

        AddOption(options, id: "resolve_rewards", action: "resolve_rewards", description: "resolve_rewards");
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

    /// <summary>
    /// The availability flag the compact view publishes per action, beyond <c>locked</c>. An item
    /// whose flag is explicitly false is one the executor 409s (a claimed reward, a disabled rest
    /// option, a non-actionable epoch, an unaffordable or sold-out shop item): offering it let Jev pick
    /// it, which wasted the paid Jev request and then forced a full LLM turn for the same screen.
    /// Absent flags keep the option, matching <see cref="ReadBoolOrAbsent"/>.
    /// </summary>
    private static readonly Dictionary<string, string[]> AvailabilityFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claim_reward"] = new[] { "claimable" },
        ["choose_rest_option"] = new[] { "enabled", "is_enabled" },
        ["choose_timeline_epoch"] = new[] { "actionable", "is_actionable" },
        ["buy_card"] = new[] { "affordable", "stocked", "enough_gold", "is_stocked" },
        ["buy_relic"] = new[] { "affordable", "stocked", "enough_gold", "is_stocked" },
        ["buy_potion"] = new[] { "affordable", "stocked", "enough_gold", "is_stocked" }
    };

    private static bool IsUnavailable(string action, JsonElement item)
    {
        return AvailabilityFlags.TryGetValue(action, out var flags)
            && flags.Any(flag => !ReadBoolOrAbsent(item, flag));
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
