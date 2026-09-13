using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline contract for the co-op save precheck. The game renames the multiplayer run save (and its
/// backup) to *.VAL.corrupt when the save's players[].net_id set does not contain the local player
/// id, so the ids have to be read out of the save text and compared before any load. The rule is
/// fail-open: a save the probe could not read must never block a legitimate continue.
/// </summary>
internal static class CoopSavePrecheckTests
{
    private const string TwoPlayerSave = """
        { "schema_version": 20, "players": [ { "net_id": 1, "current_hp": 70 }, { "net_id": 2026091302 } ] }
        """;

    public static void ReadsPlayerNetIdsFromTheSave()
    {
        var ids = CoopSavePrecheckPolicy.ReadPlayerNetIds(TwoPlayerSave);
        Assert.Equal(2, ids.Count);
        Assert.Equal(1UL, ids[0]);
        Assert.Equal(2026091302UL, ids[1]);
    }

    public static void ReadsNumericStringNetIds()
    {
        var ids = CoopSavePrecheckPolicy.ReadPlayerNetIds(
            """{ "players": [ { "net_id": "7" }, { "net_id": 8 } ] }""");
        Assert.Equal(2, ids.Count);
        Assert.Equal(7UL, ids[0]);
        Assert.Equal(8UL, ids[1]);
    }

    public static void UnreadableSavesYieldNoIds()
    {
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds(null).Count);
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("   ").Count);
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("not json").Count);
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("{}").Count);
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("""{ "players": {} }""").Count);
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("""{ "players": [] }""").Count);
        // A player entry without a usable net_id is skipped, never guessed.
        Assert.Equal(0, CoopSavePrecheckPolicy.ReadPlayerNetIds("""{ "players": [ { "net_id": "abc" }, {} ] }""").Count);
    }

    public static void HostMismatchNamesBothIdsAndTheConsequence()
    {
        var ids = CoopSavePrecheckPolicy.ReadPlayerNetIds(TwoPlayerSave);
        Assert.True(CoopSavePrecheckPolicy.DescribeHostMismatch(ids, 1) == null);
        Assert.True(CoopSavePrecheckPolicy.DescribeHostMismatch(ids, 2026091302) == null);

        var message = CoopSavePrecheckPolicy.DescribeHostMismatch(ids, 2026091007);
        Assert.NotNull(message);
        Assert.Contains("2026091007", message);
        Assert.Contains("1,2026091302", message);
        Assert.Contains(".VAL.corrupt", message);
        Assert.Contains("--clientId", message);
    }

    public static void CompanionMismatchNamesBothIdsAndTheRejection()
    {
        var ids = CoopSavePrecheckPolicy.ReadPlayerNetIds(TwoPlayerSave);
        Assert.True(CoopSavePrecheckPolicy.DescribeCompanionMismatch(ids, 2026091302) == null);

        var message = CoopSavePrecheckPolicy.DescribeCompanionMismatch(ids, 2);
        Assert.NotNull(message);
        Assert.Contains("NetId 2", message);
        Assert.Contains("NotInSaveGame", message);
        Assert.Contains("1,2026091302", message);
    }

    public static void UnreadableIdsFailOpen()
    {
        Assert.True(CoopSavePrecheckPolicy.DescribeHostMismatch(Array.Empty<ulong>(), 5) == null);
        Assert.True(CoopSavePrecheckPolicy.DescribeCompanionMismatch(Array.Empty<ulong>(), 5) == null);

        var noIds = CoopSavePrecheckPolicy.ReadPlayerNetIds("not json");
        Assert.True(CoopSavePrecheckPolicy.DescribeHostMismatch(noIds, 5) == null);
        Assert.True(CoopSavePrecheckPolicy.DescribeCompanionMismatch(noIds, 5) == null);
    }
}
