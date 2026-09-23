using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// An external planner's partial strategy update (<c>POST /strategy</c>, MCP <c>update_play_strategy</c>).
/// </summary>
/// <remarks>
/// Both surfaces document "omitted fields keep their current values". Before this suite the HTTP route
/// parsed the body with <see cref="PlayStrategy.TryParse"/>, which fills omitted fields with defaults, so
/// a posture-only nudge from the Python sidecar silently wiped the instructions and hints; the native
/// MCP merged by hand but rebuilt the record without the macro goal. One merge now serves both.
/// </remarks>
internal static class PlayStrategyUpdateTests
{
    private static PlayStrategy Current() => new()
    {
        Posture = "aggressive",
        Goal = "kill the weakest enemy first",
        Instructions = "strip Slippery before committing damage",
        OptionHints = new Dictionary<string, string>(StringComparer.Ordinal) { ["play_card"] = "prefer attacks" },
        PlanScreen = "COMBAT",
        PlanRound = 2,
        Source = "llm"
    };

    private static PlayStrategyUpdate? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PlayStrategyUpdate.TryRead(document.RootElement);
    }

    public static void OmittedFieldsKeepTheirCurrentValues()
    {
        var update = Parse("""{"posture":"defensive"}""");
        Assert.NotNull(update);

        var next = update!.ApplyTo(Current(), "mcp");

        Assert.Equal("defensive", next.Posture);
        Assert.Equal("kill the weakest enemy first", next.Goal);
        Assert.Equal("strip Slippery before committing damage", next.Instructions);
        Assert.Equal("prefer attacks", next.OptionHints["play_card"]);
        Assert.Equal("mcp", next.Source);
        // A posture nudge does not rewrite what the plan says, so where it was written still holds.
        Assert.Equal("COMBAT", next.PlanScreen);
        Assert.Equal(2, next.PlanRound);
    }

    public static void AnExternalPlannerCanSetTheMacroGoal()
    {
        var update = Parse("""{"goal":"hold potions for the elite"}""");
        Assert.NotNull(update);

        var next = update!.ApplyTo(Current(), "mcp");

        Assert.Equal("hold potions for the elite", next.Goal);
        Assert.Equal("aggressive", next.Posture);
        // The guidance was rewritten outside any recorded frame: the old scope no longer describes it.
        Assert.Equal(string.Empty, next.PlanScreen);
        Assert.Null(next.PlanRound);
    }

    public static void AnOverlongGoalIsClamped()
    {
        var update = Parse("{\"goal\":\"" + new string('g', PlayStrategy.MaxGoalCharacters + 50) + "\"}");
        var next = update!.ApplyTo(Current(), "mcp");
        Assert.Equal(PlayStrategy.MaxGoalCharacters, next.Goal.Length);
    }

    public static void AnUpdateWithNoRecognisedFieldIsRejected()
    {
        Assert.Null(Parse("{}"));
        Assert.Null(Parse("""{"posture":5,"option_hints":"not an object"}"""));
        Assert.Null(Parse("[]"));
    }

    public static void ExplicitHintsReplaceTheMap()
    {
        var update = Parse("""{"option_hints":{"end_turn":"only when nothing helps","bad":7}}""");
        var next = update!.ApplyTo(Current(), "mcp");

        Assert.False(next.OptionHints.ContainsKey("play_card"), "an explicit map replaces the old one");
        Assert.Equal("only when nothing helps", next.OptionHints["end_turn"]);
        Assert.False(next.OptionHints.ContainsKey("bad"), "a non-string hint is dropped");
    }

    /// <summary>
    /// The planner reads the compact state capped at 4,000 characters plus an ellipsis, which is no
    /// longer valid JSON once a combat frame is larger than that -- and combat frames nearly always
    /// are. The plan scope must still be read from the head of the cut summary, or it is empty on
    /// every combat plan and the executor never learns a plan came from an earlier encounter.
    /// </summary>
    public static void PlanScopeSurvivesTheTruncatedPlannerSummary()
    {
        var padding = string.Join(",", Enumerable.Range(0, 400).Select(i => $"\"card_{i}\""));
        var full = "{\"run_id\":\"R1\",\"screen\":\"COMBAT\",\"session\":{\"mode\":\"singleplayer\"},\"turn\":3,"
            + "\"combat\":{\"player\":{\"hp\":\"25/87\"}},\"run\":{\"deck\":[" + padding + "]}}";
        Assert.True(full.Length > 4000, "the fixture must exceed the planner's read cap");
        var truncated = full[..4000] + "…";

        var scope = JevOptionEnumerator.ReadScope(truncated);

        Assert.Equal("COMBAT", scope.Screen);
        Assert.Equal(3, scope.Round);

        // The decision snapshot's nested shape still reads, whole or cut.
        var nested = "{\"state\":{\"screen\":\"MAP\",\"turn\":null,\"run\":{\"deck\":[" + padding;
        Assert.Equal("MAP", JevOptionEnumerator.ReadScope(nested).Screen);
        // A cut before the fields arrive yields nothing rather than a guess.
        Assert.Null(JevOptionEnumerator.ReadScope("{\"run_id\":\"R1\",\"scr").Screen);
    }

    /// <summary>
    /// An external update is merged under the store's own lock, so a planner write that lands between
    /// "read current" and "write merged" cannot be silently reverted with stale fields.
    /// </summary>
    public static void AnExternalUpdateMergesAtomicallyInTheStore()
    {
        // Read-modify-write from two threads: each merge appends one character to what it read. A
        // merge that read a snapshot before another writer landed would overwrite it, and the final
        // length would come up short. Under the store's lock no increment can be lost.
        const int perThread = 500;
        var store = new StrategyStore();
        store.Update(Current() with { Instructions = string.Empty });

        void Append() 
        {
            for (var i = 0; i < perThread; i++)
            {
                store.UpdateMerged(current => new PlayStrategyUpdate(null, null, current.Instructions + "x", null)
                    .ApplyTo(current, "mcp"));
            }
        }

        Task.WaitAll(Task.Run(Append), Task.Run(Append));

        Assert.Equal(perThread * 2, store.Current.Instructions.Length);
    }

    /// <summary>Both writers go through the one merge, so they cannot drift apart again.</summary>
    public static void BothWritersUseTheSharedMerge()
    {
        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        Assert.Contains("PlayStrategyUpdate.TryRead(", router);
        Assert.Contains("AgentRuntime.Instance.UpdatePlayStrategy(update)", router);

        var native = AgentSourceFixture.Read("STS2AIAgent/Server/NativeMcpServer.Tools.cs");
        Assert.Contains("ReadString(arguments, \"goal\")", native);
        Assert.Contains("_strategyStore.UpdateMerged(current => update.ApplyTo(current, \"mcp\"))", native);

        // The runtime's external entry merges inside the store's lock, never read-then-write outside it.
        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Jev.cs");
        Assert.Contains("_strategyStore.UpdateMerged(current => update.ApplyTo(current, \"mcp\"))", runtime);
        Assert.False(native.Contains("_strategyStore.Update(new PlayStrategy", StringComparison.Ordinal),
            "The native MCP rebuilds the record by hand again, which drops fields it does not name.");
    }
}
