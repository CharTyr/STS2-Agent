using System.Text.Json;
using System.Text.Json.Nodes;

namespace STS2AIAgent.Agent;

/// <summary>
/// Trims the compact game state for the strategy planner's prompt, keeping it a valid JSON document
/// within a character cap.
/// </summary>
/// <remarks>
/// The planner used to receive the compact state cut at a character count plus an ellipsis. On nearly
/// every combat frame that is not JSON at all, so the planner could not parse its own input and the
/// plan scope (screen/round) was always empty. This trimmer removes fields in priority order instead:
/// the glossary and the bulk catalog (deck, piles, relic descriptions) go first, because a planner
/// writing "kill the weakest enemy" does not need the full deck; combat facts the plan is about
/// (screen, turn, hand, enemies, intents, action readiness) go last.
///
/// The result is always parseable by <see cref="JsonDocument.Parse"/>: if nothing fits, the summary
/// degrades to an empty object rather than to text that is no longer JSON.
/// </remarks>
internal static class PlanningSummary
{
    /// <summary>Root fields removed first, in this order, until the summary fits.</summary>
    private static readonly string[] RootTrimOrder =
    {
        "glossary",
        "relic_descriptions",
        "reward",
        "shop",
        "event",
        "bundles",
        "chest",
        "selection",
        "modal",
        "capstone",
        "timeline",
        "unlock",
        "game_over",
        "rest",
        "map"
    };

    /// <summary>Fields inside `run` removed first: deck and piles are the largest, least decision-relevant parts.</summary>
    private static readonly string[] RunTrimOrder = { "deck", "piles", "relic_descriptions", "relics", "potions" };

    /// <summary>
    /// Returns <paramref name="json"/> unchanged when it fits and is not JSON; otherwise a trimmed
    /// copy whose serialized length is at most <paramref name="maxChars"/>.
    /// </summary>
    public static string Trim(string json, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length <= maxChars)
        {
            return json;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // Not JSON at all: the caller's problem, not ours to re-encode.
            return json;
        }

        if (root is not JsonObject rootObject)
        {
            return json;
        }

        TrimObject(rootObject, RunTrimOrder, maxChars, "run");
        foreach (var field in RootTrimOrder)
        {
            if (Serialize(rootObject).Length <= maxChars)
            {
                break;
            }

            rootObject.Remove(field);
        }

        foreach (var field in RunTrimOrder)
        {
            if (Serialize(rootObject).Length <= maxChars)
            {
                break;
            }

            if (rootObject["run"] is JsonObject run)
            {
                run.Remove(field);
            }
        }

        var summary = Serialize(rootObject);
        return summary.Length <= maxChars ? summary : "{}";
    }

    private static void TrimObject(JsonObject root, string[] order, int maxChars, string childName)
    {
        // Pre-pass for the child catalog: the run block is the single biggest subtree on most frames.
        if (root[childName] is not JsonObject child)
        {
            return;
        }

        foreach (var field in order)
        {
            if (Serialize(root).Length <= maxChars)
            {
                return;
            }

            child.Remove(field);
        }
    }

    private static string Serialize(JsonNode node)
    {
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
