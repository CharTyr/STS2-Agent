using System.Globalization;
using System.Numerics;
using System.Text;
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
        var cap = Math.Clamp(limit, 1, MaxDiffEntries);
        var beforeLeaves = Flatten(before);
        var afterLeaves = Flatten(after);

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        paths.UnionWith(beforeLeaves.Keys);
        paths.UnionWith(afterLeaves.Keys);

        var changes = new List<object>();
        var truncated = beforeLeaves.Values.Concat(afterLeaves.Values).Any(leaf => leaf.Kind == "depth");
        foreach (var path in paths)
        {
            beforeLeaves.TryGetValue(path, out var beforeValue);
            afterLeaves.TryGetValue(path, out var afterValue);
            var hasBefore = beforeLeaves.ContainsKey(path);
            var hasAfter = afterLeaves.ContainsKey(path);

            if (hasBefore && hasAfter && beforeValue.Kind == afterValue.Kind && beforeValue.Text == afterValue.Text)
            {
                continue;
            }

            // Only an additional difference proves that the entry cap omitted a result.
            if (changes.Count >= cap)
            {
                truncated = true;
                break;
            }

            changes.Add(new
            {
                path,
                before = hasBefore ? beforeValue.Value : null,
                after = hasAfter ? afterValue.Value : null
            });

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
            flat[path] = new Leaf("depth", "<max depth>", "<max depth>");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var wroteChild = false;
                foreach (var property in value.EnumerateObject())
                {
                    wroteChild = true;
                    var child = PropertyPath(path, property.Name);
                    FlattenInto(property.Value, child, depth + 1, flat);
                }

                if (!wroteChild)
                {
                    flat[path] = new Leaf("object", "{}", "{}");
                }

                return;

            case JsonValueKind.Array:
                // Lists are compared by length and by element, so an index that appeared or vanished
                // is a change of its own rather than a reshuffle of every later index.
                var length = "len=" + value.GetArrayLength();
                flat[path + "[]"] = new Leaf("array_length", length, length);
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
                return new Leaf("number", NumberKey(value.GetRawText()), value.Clone());
            case JsonValueKind.True:
                return new Leaf("bool", "b:true", true);
            case JsonValueKind.False:
                return new Leaf("bool", "b:false", false);
            default:
                return new Leaf("null", "z:", null);
        }
    }

    private static string PropertyPath(string path, string name)
    {
        var plain = name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
                    name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        return plain ? (path.Length == 0 ? name : path + "." + name)
            : path + "[" + QuotePathName(name) + "]";
    }

    // Match Python json.dumps(..., ensure_ascii=True), including lowercase surrogate escapes.
    // These strings are part of the path, so serializer-specific Unicode escaping is observable.
    private static string QuotePathName(string name)
    {
        var quoted = new StringBuilder();
        quoted.Append((char)34);
        foreach (var c in name)
        {
            switch ((int)c)
            {
                case 34: quoted.Append((char)92).Append((char)34); break;
                case 92: quoted.Append((char)92).Append((char)92); break;
                case 8: quoted.Append((char)92).Append('b'); break;
                case 12: quoted.Append((char)92).Append('f'); break;
                case 10: quoted.Append((char)92).Append('n'); break;
                case 13: quoted.Append((char)92).Append('r'); break;
                case 9: quoted.Append((char)92).Append('t'); break;
                default:
                    if (c < 0x20 || c > 0x7e)
                        quoted.Append((char)92).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else quoted.Append(c);
                    break;
            }
        }
        return quoted.Append((char)34).ToString();
    }

    // Compare JSON numbers by their exact decimal value, without rounding ids through double.
    // Keep the original JsonElement for output so even large numbers retain their wire value.
    private static string NumberKey(string raw)
    {
        var exponentAt = raw.IndexOfAny(new[] { 'e', 'E' });
        var mantissa = exponentAt < 0 ? raw : raw[..exponentAt];
        var exponent = exponentAt < 0 ? BigInteger.Zero
            : BigInteger.Parse(raw[(exponentAt + 1)..], CultureInfo.InvariantCulture);
        var negative = mantissa.StartsWith('-');
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        if (dot >= 0)
        {
            exponent -= mantissa.Length - dot - 1;
            mantissa = mantissa.Remove(dot, 1);
        }
        mantissa = mantissa.TrimStart('0');
        if (mantissa.Length == 0) return "0";
        var digits = mantissa.TrimEnd('0');
        exponent += mantissa.Length - digits.Length;
        return (negative ? "-" : "") + digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
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
