namespace STS2AIAgent.Tests;

/// <summary>
/// <c>GET /data/monsters</c> exports each monster's moves, once each, by name.
/// </summary>
/// <remarks>
/// This export was wrong twice. It read a public <c>MonsterModel.MoveNames</c> property by
/// reflection after the game had removed it, so every monster exported <c>moves: []</c>. Fixed by
/// calling the localization query that property had always wrapped -- and the first live run of that
/// fix showed nine monsters with duplicate moves, because the same prefix also holds each move's
/// dialogue. <c>FAKE_MERCHANT_MONSTER</c>'s ENRAGE came out three times, two of them taunts.
/// </remarks>
internal static class MonsterMovesExportContractTests
{
    private const string ExportPath = "STS2AIAgent/Game/GameDataExportService.cs";

    public static void MovesComeFromTheLocalizationTableDirectly()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.Read(ExportPath), "BuildMonsterMoves"));

        Assert.True(
            body.Contains("LocManager.Instance.GetTable(\"monsters\").GetLocStringsWithPrefix(", StringComparison.Ordinal),
            "BuildMonsterMoves must call the public localization query directly. It is compile-checked; "
            + "the reflective MoveNames read it replaced emptied the export when the game removed it.");
        Assert.False(
            body.Contains("GetProperty(", StringComparison.Ordinal),
            "BuildMonsterMoves must not reflect. Every member it needs is public.");
    }

    public static void OnlyTheTitleOfEachMoveIsExported()
    {
        var source = AgentSourceFixture.Read(ExportPath);
        var body = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, "BuildMonsterMoves"));

        Assert.True(
            body.Contains("entry.key.EndsWith(MoveTitleSuffix,StringComparison.Ordinal)", StringComparison.Ordinal),
            "BuildMonsterMoves must keep only `.title` keys. The prefix also holds each move's banter and "
            + "speakLine entries, and exporting them duplicates moves and names them with taunts.");
        Assert.True(
            AgentSourceFixture.WithoutWhitespace(source).Contains("privateconststringMoveTitleSuffix=\".title\";", StringComparison.Ordinal),
            "MoveTitleSuffix must stay `.title` -- the key MonsterModel.GetBestiaryMoveName builds.");
    }
}
