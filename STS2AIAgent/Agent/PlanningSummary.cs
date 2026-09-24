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
        if (summary.Length <= maxChars)
        {
            return summary;
        }

        // Extreme frame: even after every trim the summary does not fit. Never answer "{}" -- the
        // planner would then write a strategy with no context at all. Fall back to a minimal skeleton:
        // screen/turn/session plus the combat numbers a plan is steered by, dropping verbose detail
        // (enemy names, card text) rather than the frame's meaning.
        return BuildSkeleton(rootObject, maxChars);
    }

    /// <summary>Minimal but meaningful summary for a frame that survives no trimming.</summary>
    private static string BuildSkeleton(JsonObject root, int maxChars)
    {
        var skeleton = new JsonObject();
        CopyIfPresent(root, skeleton, "screen");
        CopyIfPresent(root, skeleton, "turn");
        CopyIfPresent(root, skeleton, "session");
        CopyIfPresent(root, skeleton, "run_id");

        if (root["combat"] is JsonObject combat)
        {
            var slim = new JsonObject();
            if (combat["player"] is JsonObject player)
            {
                var slimPlayer = new JsonObject();
                CopyIfPresent(player, slimPlayer, "hp");
                CopyIfPresent(player, slimPlayer, "block");
                CopyIfPresent(player, slimPlayer, "energy");
                if (slimPlayer.Count > 0) slim["player"] = slimPlayer;
            }
            if (combat["enemies"] is JsonArray enemies)
            {
                slim["enemy_count"] = enemies.Count;
            }
            if (combat["hand"] is JsonArray cards)
            {
                slim["hand_count"] = cards.Count;
            }
            if (combat["action_readiness"] is JsonObject ready
                && ready.TryGetPropertyValue("reason", out var reason))
            {
                var slimReady = new JsonObject();
                if (reason != null) slimReady["reason"] = reason.DeepClone();
                slim["action_readiness"] = slimReady;
            }
            if (slim.Count > 0) skeleton["combat"] = slim;
        }

        var text = Serialize(skeleton);
        return text.Length <= maxChars ? text : "{}";
    }

    private static void CopyIfPresent(JsonObject from, JsonObject to, string key)
    {
        if (from.TryGetPropertyValue(key, out var value) && value != null)
        {
            to[key] = value.DeepClone();
        }
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
