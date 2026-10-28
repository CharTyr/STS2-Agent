using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Runs the whole-turn plan vectors shared with the Python MCP server
/// (<c>mcp_server/tests/fixtures/turn_plan_vectors.json</c>), so the two implementations of
/// <see cref="TurnPlanPolicy"/> cannot disagree about a plan (W3/W10).
/// </summary>
internal static class TurnPlanVectorTests
{
    private const string VectorsPath = "mcp_server/tests/fixtures/turn_plan_vectors.json";

    private static JsonElement Vectors() => JsonDocument.Parse(AgentSourceFixture.Read(VectorsPath)).RootElement.Clone();

    private static string Raw(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();

    public static void ParseVectors()
    {
        foreach (var vector in Vectors().GetProperty("parse").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var plan = TurnPlanPolicy.Parse(Raw(vector.GetProperty("args")));
            var first = vector.GetProperty("first");
            if (first.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(plan.First);
            }
            else
            {
                AssertStep(first, plan.First, name + " (first)");
            }

            if (vector.TryGetProperty("then_count", out var count))
            {
                Assert.Equal(count.GetInt32(), plan.Then.Count, name + ": then count");
            }
            else
            {
                var expected = vector.GetProperty("then").EnumerateArray().ToList();
                Assert.Equal(expected.Count, plan.Then.Count, name + ": then count");
                for (var i = 0; i < expected.Count; i++)
                {
                    AssertStep(expected[i], plan.Then[i], $"{name} (then[{i}])");
                }
            }

            var warnings = vector.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()!).ToList();
            Assert.Equal(string.Join(" | ", warnings), string.Join(" | ", plan.Warnings), name + ": warnings");
        }
    }

    public static void ResolveVectors()
    {
        var vectors = Vectors();
        var state = vectors.GetProperty("resolve_state").GetRawText();
        foreach (var vector in vectors.GetProperty("resolve").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var step = TurnPlanPolicy.Parse(vector.GetProperty("step").GetRawText()).First
                ?? throw new Exception(name + ": the step did not parse");
            var resolution = TurnPlanPolicy.Resolve(step, state, vector.GetProperty("require_cid").GetBoolean());
            var expect = vector.GetProperty("expect");
            Assert.Equal(NullableString(expect, "stop_reason"), resolution.StopReason, name + ": stop_reason");
            if (resolution.Ok)
            {
                Assert.Equal(NullableInt(expect, "card_index"), resolution.CardIndex, name + ": card_index");
                Assert.Equal(NullableInt(expect, "target_index"), resolution.TargetIndex, name + ": target_index");
                Assert.Equal(NullableInt(expect, "option_index"), resolution.OptionIndex, name + ": option_index");
            }
        }
    }

    public static void DivergedVectors()
    {
        foreach (var vector in Vectors().GetProperty("diverged").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var actual = TurnPlanPolicy.Diverged(Raw(vector.GetProperty("plan_time")), Raw(vector.GetProperty("current")));
            Assert.Equal(NullableString(vector, "expect"), actual, name!);
        }
    }

    private static void AssertStep(JsonElement expected, PlanStep? actual, string? label)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.GetProperty("action").GetString(), actual!.Action, label + ": action");
        Assert.Equal(expected.GetProperty("card_cid").ValueKind == JsonValueKind.Null ? null : (uint?)expected.GetProperty("card_cid").GetUInt32(), actual.CardCid, label + ": card_cid");
        Assert.Equal(NullableInt(expected, "card_index"), actual.CardIndex, label + ": card_index");
        Assert.Equal(NullableInt(expected, "target_index"), actual.TargetIndex, label + ": target_index");
        Assert.Equal(NullableInt(expected, "option_index"), actual.OptionIndex, label + ": option_index");
        Assert.Equal(NullableString(expected, "reason"), actual.Reason, label + ": reason");
    }

    private static int? NullableInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string? NullableString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
