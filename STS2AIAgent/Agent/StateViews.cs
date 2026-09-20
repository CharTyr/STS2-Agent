using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// Two read-only projections over a raw <c>/state</c> payload: a run summary and a state diff.
/// </summary>
/// <remarks>
/// This mirrors <c>mcp_server/src/sts2_mcp/state_views.py</c> field for field, the way ADR 0002
/// requires of the two MCP surfaces. Both work on the <b>raw</b> payload rather than the compact
/// <c>agent_view</c>, because the raw field names are the documented ones in <c>docs/api.md</c> and
/// do not move, while the compact view renames many of them (it has its own rename table there) --
/// a mirror that read the compact shape would have to track that table too.
///
/// Neither projection invents a value. A field the payload does not carry serializes as null, and a
/// payload with no <c>run</c> answers with a null summary, so a caller can tell "not in a run" from
/// "in a run with nothing in it".
/// </remarks>
internal static class StateViews
{
    /// <summary>
    /// A diff of two whole payloads can be enormous (a full hand, every intent, the map graph), so
    /// the cap keeps one call from flooding a model's context; the caller reads <c>truncated</c> and
    /// narrows the comparison instead of silently receiving a partial diff.
    /// </summary>
    public const int MaxDiffEntries = 200;

    /// <summary>The map graph nests in principle without bound; a debugging tool must still terminate.</summary>
    public const int MaxDiffDepth = 12;

    /// <summary>
    /// A one-call summary of the current run, or null when the payload carries no run.
    /// </summary>
    public static object? BuildRunSummary(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object ||
            !state.TryGetProperty("run", out var run) ||
            run.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var potions = ArrayOf(run, "potions");
        var occupied = 0;
        foreach (var potion in potions)
        {
            if (potion.ValueKind == JsonValueKind.Object &&
                potion.TryGetProperty("occupied", out var isOccupied) &&
                isOccupied.ValueKind == JsonValueKind.True)
            {
                occupied++;
            }
        }

        var party = new List<object>();
        foreach (var member in ArrayOf(run, "players"))
        {
            if (member.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            party.Add(new
            {
                player_id = Text(member, "player_id"),
                is_local = Bool(member, "is_local"),
                is_alive = Bool(member, "is_alive"),
                current_hp = Number(member, "current_hp"),
                max_hp = Number(member, "max_hp"),
                gold = Number(member, "gold")
            });
        }

        return new
        {
            character_id = Text(run, "character_id"),
            character_name = Text(run, "character_name"),
            floor = Number(run, "floor"),
            act_id = Text(run, "act_id"),
            boss_id = Text(run, "boss_id"),
            ascension = Number(run, "ascension"),
            current_hp = Number(run, "current_hp"),
            max_hp = Number(run, "max_hp"),
            gold = Number(run, "gold"),
            max_energy = Number(run, "max_energy"),
            deck_size = ArrayOf(run, "deck").Count,
            relic_count = ArrayOf(run, "relics").Count,
            potion_count = potions.Count,
            potions_occupied = occupied,
            party
        };
    }

    /// <summary>
    /// The paths that differ between two payloads; each entry carries the value before and after,
    /// with null for a side that does not have the path at all.
    /// </summary>
    public static object BuildStateDiff(JsonElement before, JsonElement after, int limit = MaxDiffEntries)
    {
        var cap = Math.Max(1, limit);
        var beforeLeaves = Flatten(before);
        var afterLeaves = Flatten(after);

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        paths.UnionWith(beforeLeaves.Keys);
        paths.UnionWith(afterLeaves.Keys);

        var changes = new List<object>();
        var truncated = false;
        foreach (var path in paths)
        {
            beforeLeaves.TryGetValue(path, out var beforeValue);
            afterLeaves.TryGetValue(path, out var afterValue);
            var hasBefore = beforeLeaves.ContainsKey(path);
            var hasAfter = afterLeaves.ContainsKey(path);

            if (hasBefore && hasAfter && beforeValue == afterValue)
            {
                continue;
            }

            changes.Add(new
            {
                path,
                before = hasBefore ? beforeValue.Value : null,
                after = hasAfter ? afterValue.Value : null
            });

            if (changes.Count >= cap)
            {
                truncated = changes.Count < paths.Count;
                break;
            }
        }

        return new
        {
            changes,
            change_count = changes.Count,
            truncated,
            limit = cap
        };
    }

    /// <summary>
    /// A scalar as (kind, value). The kind is part of the comparison on purpose: JSON <c>"12"</c>
    /// and <c>12</c> are different facts about a payload, and the Python mirror cannot rely on bare
    /// equality either (<c>True == 1</c> there), so both tag leaves the same way.
    /// </summary>
    private readonly record struct Leaf(string Kind, string Text, object? Value);

    /// <summary>Every scalar path in the payload, tagged so the comparison is type-aware.</summary>
    private static Dictionary<string, Leaf> Flatten(JsonElement value)
    {
        var flat = new Dictionary<string, Leaf>(StringComparer.Ordinal);
        FlattenInto(value, string.Empty, 0, flat);
        return flat;
    }

    private static void FlattenInto(JsonElement value, string path, int depth, Dictionary<string, Leaf> flat)
    {
        if (depth > MaxDiffDepth)
        {
            flat[path] = new Leaf("string", "<max depth>", "<max depth>");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var wroteChild = false;
                foreach (var property in value.EnumerateObject())
                {
                    wroteChild = true;
                    var child = path.Length == 0 ? property.Name : path + "." + property.Name;
                    FlattenInto(property.Value, child, depth + 1, flat);
                }

                if (!wroteChild)
                {
                    flat[path] = new Leaf("string", "{}", "{}");
                }

                return;

            case JsonValueKind.Array:
                // Lists are compared by length and by element, so an index that appeared or vanished
                // is a change of its own rather than a reshuffle of every later index.
                var length = "len=" + value.GetArrayLength();
                flat[path + "[]"] = new Leaf("string", length, length);
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    FlattenInto(item, path + "[" + index + "]", depth + 1, flat);
                    index++;
                }

                return;

            default:
                flat[path] = Scalar(value);
                return;
        }
    }

    private static Leaf Scalar(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                return new Leaf("string", "s:" + text, text);
            case JsonValueKind.Number:
                return new Leaf("number", "n:" + value.GetRawText(), value.GetDouble());
            case JsonValueKind.True:
                return new Leaf("bool", "b:true", true);
            case JsonValueKind.False:
                return new Leaf("bool", "b:false", false);
            default:
                return new Leaf("null", "z:", null);
        }
    }

    private static IReadOnlyList<JsonElement> ArrayOf(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        var items = new List<JsonElement>();
        foreach (var item in value.EnumerateArray())
        {
            items.Add(item);
        }

        return items;
    }

    private static string? Text(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? Bool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? Number(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out var number)
            ? number
            : null;
    }
}
