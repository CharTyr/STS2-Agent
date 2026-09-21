using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The C# half of the state views. These are the mirror of <c>mcp_server/tests/test_state_views.py</c>
/// and assert the same facts, because ADR 0002 keeps the two MCP surfaces aligned: a difference here
/// is a difference an external client sees depending on which surface it connected to.
/// </summary>
internal static class StateViewsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private const string CombatState = """
        {"screen":"COMBAT","run":{"character_id":"SILENT","character_name":"Silent","floor":7,
        "act_id":"1","boss_id":"HEXAGHOST","ascension":3,"current_hp":41,"max_hp":70,"gold":212,
        "max_energy":3,"deck":[{"card_id":"STRIKE"},{"card_id":"DEFEND"}],
        "relics":[{"relic_id":"BURNING_BLOOD"}],
        "potions":[{"occupied":true,"potion_id":"FIRE_POTION"},{"occupied":false}],
        "players":[{"player_id":"p1","is_local":true,"is_alive":true,"current_hp":41,"max_hp":70,"gold":212}]}}
        """;

    public static void RunSummaryReadsTheDocumentedRunFields()
    {
        var json = JsonSerializer.Serialize(StateViews.BuildRunSummary(Json(CombatState)));
        using var document = JsonDocument.Parse(json);
        var summary = document.RootElement;

        Assert.Equal("SILENT", summary.GetProperty("character_id").GetString());
        Assert.Equal(7d, summary.GetProperty("floor").GetDouble());
        Assert.Equal("HEXAGHOST", summary.GetProperty("boss_id").GetString());
        Assert.Equal(41d, summary.GetProperty("current_hp").GetDouble());
        Assert.Equal(212d, summary.GetProperty("gold").GetDouble());
    }

    public static void RunSummaryCountsWhatThePayloadHolds()
    {
        var json = JsonSerializer.Serialize(StateViews.BuildRunSummary(Json(CombatState)));
        using var document = JsonDocument.Parse(json);
        var summary = document.RootElement;

        Assert.Equal(2, summary.GetProperty("deck_size").GetInt32());
        Assert.Equal(1, summary.GetProperty("relic_count").GetInt32());
        // Two slots, one of them filled: the two counts are different facts.
        Assert.Equal(2, summary.GetProperty("potion_count").GetInt32());
        Assert.Equal(1, summary.GetProperty("potions_occupied").GetInt32());
        Assert.Equal(1, summary.GetProperty("party").GetArrayLength());
        Assert.Equal("p1", summary.GetProperty("party")[0].GetProperty("player_id").GetString());
    }

    public static void RunSummaryWithoutARunIsNull()
    {
        Assert.Null(StateViews.BuildRunSummary(Json("""{"screen":"MAIN_MENU"}""")));
        Assert.Null(StateViews.BuildRunSummary(Json("""{"run":null}""")));
        Assert.Null(StateViews.BuildRunSummary(Json("[]")));
        Assert.Null(StateViews.BuildRunSummary(default));
    }

    public static void RunSummaryMissingFieldsStayNull()
    {
        var json = JsonSerializer.Serialize(StateViews.BuildRunSummary(Json("""{"run":{"floor":2}}""")));
        using var document = JsonDocument.Parse(json);
        var summary = document.RootElement;

        Assert.Equal(2d, summary.GetProperty("floor").GetDouble());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("gold").ValueKind);
        Assert.Equal(0, summary.GetProperty("deck_size").GetInt32());
    }

    public static void DiffReportsOnlyChangedPaths()
    {
        var diff = StateViews.BuildStateDiff(
            Json("""{"run":{"current_hp":41,"gold":212},"screen":"COMBAT"}"""),
            Json("""{"run":{"current_hp":38,"gold":212},"screen":"COMBAT"}"""));

        var json = JsonSerializer.Serialize(diff);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("change_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        var change = root.GetProperty("changes")[0];
        Assert.Equal("run.current_hp", change.GetProperty("path").GetString());
        Assert.Equal(41d, change.GetProperty("before").GetDouble());
        Assert.Equal(38d, change.GetProperty("after").GetDouble());
    }

    public static void DiffReportsOneSidedPathsAsNull()
    {
        var diff = StateViews.BuildStateDiff(
            Json("""{"a":1}"""),
            Json("""{"b":2}"""));

        var json = JsonSerializer.Serialize(diff);
        using var document = JsonDocument.Parse(json);
        var changes = document.RootElement.GetProperty("changes");
        Assert.Equal(2, changes.GetArrayLength());

        var first = changes[0];
        Assert.Equal("a", first.GetProperty("path").GetString());
        Assert.Equal(1d, first.GetProperty("before").GetDouble());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("after").ValueKind);

        var second = changes[1];
        Assert.Equal("b", second.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("before").ValueKind);
        Assert.Equal(2d, second.GetProperty("after").GetDouble());
    }

    public static void DiffOfIdenticalPayloadsIsEmptyAndNotTruncated()
    {
        var payload = Json("""{"run":{"deck":[{"card_id":"STRIKE"}]}}""");

        var json = JsonSerializer.Serialize(StateViews.BuildStateDiff(payload, payload));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("change_count").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
    }

    public static void DiffTreatsATypeChangeAsAChange()
    {
        // JSON "12" and 12 are different facts about a payload.
        var numberToString = JsonSerializer.Serialize(
            StateViews.BuildStateDiff(Json("""{"gold":12}"""), Json("""{"gold":"12"}""")));
        using var stringDocument = JsonDocument.Parse(numberToString);
        Assert.Equal(1, stringDocument.RootElement.GetProperty("change_count").GetInt32());

        var boolToNumber = JsonSerializer.Serialize(
            StateViews.BuildStateDiff(Json("""{"flag":true}"""), Json("""{"flag":1}""")));
        using var boolDocument = JsonDocument.Parse(boolToNumber);
        Assert.Equal(1, boolDocument.RootElement.GetProperty("change_count").GetInt32());
    }

    public static void DiffTracksListLengthAndIndexes()
    {
        var diff = StateViews.BuildStateDiff(
            Json("""{"combat":{"hand":[{"card_id":"STRIKE"}]}}"""),
            Json("""{"combat":{"hand":[{"card_id":"DEFEND"},{"card_id":"STRIKE"}]}}"""));

        var json = JsonSerializer.Serialize(diff);
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("changes").EnumerateArray()
            .Select(change => change.GetProperty("path").GetString())
            .ToArray();

        Assert.True(paths.Contains("combat.hand[]"), "expected the list length change");
        Assert.True(paths.Contains("combat.hand[0].card_id"), "expected the index change");
    }

    public static void DiffCapIsReportedSoAPartialDiffNeverReadsAsEmpty()
    {
        var before = new Dictionary<string, int>();
        var after = new Dictionary<string, int>();
        for (var index = 0; index < StateViews.MaxDiffEntries + 20; index++)
        {
            before["k" + index] = 0;
            after["k" + index] = 1;
        }

        var diff = StateViews.BuildStateDiff(
            Json(JsonSerializer.Serialize(before)),
            Json(JsonSerializer.Serialize(after)),
            limit: 10);

        var json = JsonSerializer.Serialize(diff);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(10, root.GetProperty("change_count").GetInt32());
        Assert.Equal(10, root.GetProperty("limit").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    public static void DiffTreatsAnEmptyObjectAsALeaf()
    {
        var diff = StateViews.BuildStateDiff(
            Json("""{"shop":{}}"""),
            Json("""{"shop":{"open":true}}"""));

        var json = JsonSerializer.Serialize(diff);
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("changes").EnumerateArray()
            .Select(change => change.GetProperty("path").GetString())
            .ToArray();

        Assert.True(paths.Contains("shop"), "an empty object is a different payload from a filled one");
    }
}
