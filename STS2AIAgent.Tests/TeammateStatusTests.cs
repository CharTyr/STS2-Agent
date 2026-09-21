using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// The host's view of the teammate's live state.
/// </summary>
/// <remarks>
/// The payload read here is the <b>companion's</b> own <c>/state</c>, so "teammate" means every player
/// whose <c>is_local</c> is false: each instance reports the other one as non-local, and one payload
/// then names exactly the people the reader cares about. A companion mid-restart answers nothing, and
/// that has to produce no summary rather than an exception on the overlay's tick.
/// </remarks>
internal static class TeammateStatusTests
{
    private const string CombatState = """
        {"screen":"COMBAT","combat":{"hand":[{"i":0},{"i":1},{"i":2}],"players":[
        {"player_id":"p1","is_local":true,"is_alive":true,"current_hp":70,"max_hp":70,"block":0,"energy":3},
        {"player_id":"p2","is_local":false,"is_alive":true,"current_hp":41,"max_hp":66,"block":7,"energy":2}]}}
        """;

    private const string MapState = """
        {"screen":"MAP","run":{"players":[
        {"player_id":"p1","is_local":true,"is_alive":true,"current_hp":70,"max_hp":70,"gold":100},
        {"player_id":"p2","is_local":false,"is_alive":true,"current_hp":38,"max_hp":66,"gold":120}]}}
        """;

    public static void ReadsTheNonLocalPlayerInCombat()
    {
        var status = TeammateStatus.Parse(CombatState);

        Assert.Equal("COMBAT", status.Screen);
        Assert.True(status.InCombat);
        Assert.Equal(1, status.Players.Count);
        Assert.Equal("p2", status.Players[0].PlayerId);
        Assert.Equal(41, status.Players[0].CurrentHp);
        Assert.Equal(66, status.Players[0].MaxHp);
        Assert.Equal(7, status.Players[0].Block);
        Assert.Equal(2, status.Players[0].Energy);
        Assert.Equal(3, status.HandCount);
    }

    public static void FallsBackToTheRunPartyOutsideCombat()
    {
        var status = TeammateStatus.Parse(MapState);

        Assert.Equal("MAP", status.Screen);
        Assert.False(status.InCombat);
        Assert.Equal(1, status.Players.Count);
        Assert.Equal(38, status.Players[0].CurrentHp);
        // Hand count is a combat fact; reporting 0 outside combat would read as an empty hand.
        Assert.Equal(0, status.HandCount);
    }

    public static void DescriptionNamesHealthAndOnlyTheFactsThatApply()
    {
        var inCombat = TeammateStatus.Parse(CombatState).Describe();
        Assert.NotNull(inCombat);
        Assert.Contains("p2", inCombat!);
        Assert.Contains("41/66", inCombat);
        Assert.Contains("7", inCombat);
        Assert.Contains("2", inCombat);
        Assert.Contains("3", inCombat);

        var onMap = TeammateStatus.Parse(MapState).Describe();
        Assert.NotNull(onMap);
        var mapLine = onMap!;
        Assert.Contains("38/66", mapLine);
        Assert.False(
            mapLine.Contains("能量", StringComparison.Ordinal),
            "energy is a combat fact and must not be reported as 0 on the map");
        Assert.False(mapLine.Contains("手牌", StringComparison.Ordinal));
    }

    public static void ADownedTeammateSaysSoInsteadOfShowingEnergy()
    {
        var state = """
            {"screen":"COMBAT","combat":{"hand":[],"players":[
            {"player_id":"p1","is_local":true,"is_alive":true,"current_hp":70,"max_hp":70,"energy":3},
            {"player_id":"p2","is_local":false,"is_alive":false,"current_hp":0,"max_hp":66,"energy":0}]}}
            """;

        var text = TeammateStatus.Parse(state).Describe();

        Assert.NotNull(text);
        var line = text!;
        Assert.Contains("0/66", line);
        Assert.Contains("倒下", line);
        Assert.False(line.Contains("0 能量", StringComparison.Ordinal));
    }

    public static void NothingReadableProducesNoLineRatherThanAGuess()
    {
        // A companion that is restarting, answering an unfamiliar payload, or not there at all.
        foreach (var json in new[] { null, "", "not json", "[]", "{}", """{"screen":"COMBAT"}""" })
        {
            var status = TeammateStatus.Parse(json);
            Assert.Equal(0, status.Players.Count);
            Assert.Null(status.Describe());
        }

        // A run with only the local player is a solo payload, not a teammate with no health.
        var solo = """
            {"screen":"MAP","run":{"players":[{"player_id":"p1","is_local":true,"is_alive":true,"current_hp":70,"max_hp":70}]}}
            """;
        Assert.Null(TeammateStatus.Parse(solo).Describe());
    }

    public static void MissingHealthStaysUnknownRatherThanZero()
    {
        var state = """
            {"screen":"COMBAT","combat":{"hand":[{"i":0}],"players":[
            {"player_id":"p2","is_local":false,"is_alive":true}]}}
            """;

        var text = TeammateStatus.Parse(state).Describe();

        Assert.NotNull(text);
        var line = text!;
        Assert.Contains("未知", line);
        Assert.False(line.Contains("0/", StringComparison.Ordinal));
    }
}
