using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// The route advice behind each selectable map node (W5.2): room counts as [least, most] over every
/// path to the boss, plus the deterministic score and the honest tie behaviour.
/// </summary>
internal static class MapRoutePolicyTests
{
    /// <summary>One registration for the group, so the shared test catalog grows by a line, not eight.</summary>
    public static void RunAll()
    {
        CountsEveryRoomTypeOnEveryPath();
        HealthyRunPrefersTheDoorWithoutAnElite();
        LaterActsPunishElitesHarder();
        TiesRecommendNothing();
        NeedsAChoiceToAdvise();
        CyclesDoNotLoop();
        WideGraphsStayBounded();
        LowHealthWeighsRestsMoreAndElitesLess();
    }

    private static MapRouteNode Node(string coord, string type, params string[] children) =>
        new(coord, type, false, children);

    private static MapRouteStart Start(int index, string coord) => new(index, coord);

    /// <summary>
    /// One elite door and one monster door above the same pair of exits. Both paths reach the boss
    /// through a rest or a shop, so the counts below are ranges, not constants.
    /// </summary>
    private static MapRouteInput Fork(int hp, int gold = 300, int act = 1) => new(
        new[]
        {
            Node("0,0", "Monster", "1,0", "1,1"),
            Node("1,0", "Elite", "2,0", "2,1"),
            Node("1,1", "Monster", "2,0", "2,1", "2,2"),
            Node("2,0", "RestSite", "3,0"),
            Node("2,1", "Shop", "3,0"),
            Node("2,2", "Monster", "3,0"),
            Node("3,0", "Boss")
        },
        new[] { Start(0, "1,0"), Start(1, "1,1") },
        hp,
        80,
        gold,
        act);

    public static void CountsEveryRoomTypeOnEveryPath()
    {
        var advice = MapRoutePolicy.Advise(Fork(hp: 70));
        Assert.NotNull(advice);
        var elite = advice!.Options.First(option => option.Index == 0);

        Assert.Equal(new MapRouteRange(1, 1), elite.Elites, "the elite door always fights one elite");
        Assert.Equal(new MapRouteRange(0, 1), elite.Rests);
        Assert.Equal(new MapRouteRange(0, 1), elite.Shops);
        Assert.Equal(new MapRouteRange(0, 0), elite.Combats, "every elite path leaves through a rest or a shop");
        Assert.Equal(new MapRouteRange(0, 0), elite.Unknowns);
        Assert.Equal(new MapRouteRange(0, 0), elite.Treasures);

        var monster = advice.Options.First(option => option.Index == 1);
        Assert.Equal(new MapRouteRange(0, 0), monster.Elites, "the monster door has no path through an elite");
        Assert.Equal(new MapRouteRange(1, 2), monster.Combats, "every path fights, and one of the three exits adds a second");
    }

    /// <summary>
    /// At healthy HP with gold, the elite door's single elite costs more than the extra rest/shop
    /// variety is worth; the plain door is recommended.
    /// </summary>
    public static void HealthyRunPrefersTheDoorWithoutAnElite()
    {
        var advice = MapRoutePolicy.Advise(Fork(hp: 70));
        Assert.NotNull(advice);
        Assert.Equal(1, advice!.Recommended);
        Assert.True(advice.Note == MapRoutePolicy.AdvisoryNote, "the advice always says it is advisory");
        Assert.Contains("elite", advice.Options[0].Why, StringComparison.Ordinal);
        Assert.Contains("no elites", advice.Options[1].Why, StringComparison.Ordinal);
    }

    /// <summary>Act 2 makes the same elite hurt more, so the gap only widens.</summary>
    public static void LaterActsPunishElitesHarder()
    {
        var actOne = MapRoutePolicy.Advise(Fork(hp: 70, act: 1))!;
        var actTwo = MapRoutePolicy.Advise(Fork(hp: 70, act: 2))!;
        Assert.True(
            actTwo.Options[0].Score < actOne.Options[0].Score,
            "the elite door cannot score better in a later act");
    }

    /// <summary>Two doors with the same numbers recommend neither: a false preference is worse.</summary>
    public static void TiesRecommendNothing()
    {
        var input = new MapRouteInput(
            new[]
            {
                Node("1,0", "Monster", "2,0"),
                Node("1,1", "Monster", "2,0"),
                Node("2,0", "Boss")
            },
            new[] { Start(0, "1,0"), Start(1, "1,1") },
            70,
            80,
            100,
            1);

        var advice = MapRoutePolicy.Advise(input);
        Assert.NotNull(advice);
        Assert.Null(advice!.Recommended);
        Assert.Equal(advice.Options[0].Score, advice.Options[1].Score);
    }

    public static void NeedsAChoiceToAdvise()
    {
        var single = new MapRouteInput(
            new[] { Node("1,0", "Monster", "2,0"), Node("2,0", "Boss") },
            new[] { Start(0, "1,0") },
            70,
            80,
            100,
            1);
        Assert.Null(MapRoutePolicy.Advise(single));

        Assert.Null(MapRoutePolicy.Advise(new MapRouteInput(
            Array.Empty<MapRouteNode>(),
            new[] { Start(0, "1,0"), Start(1, "1,1") },
            70,
            80,
            100,
            1)));
    }

    /// <summary>A frame whose children point backwards must not loop forever; the path ends there.</summary>
    public static void CyclesDoNotLoop()
    {
        var input = new MapRouteInput(
            new[]
            {
                Node("1,0", "Monster", "2,0"),
                Node("1,1", "RestSite", "2,0"),
                Node("2,0", "Elite", "1,0")
            },
            new[] { Start(0, "1,0"), Start(1, "1,1") },
            50,
            80,
            0,
            1);

        var advice = MapRoutePolicy.Advise(input);
        Assert.NotNull(advice);
        var looped = advice!.Options.First(option => option.Index == 0);
        Assert.Equal(new MapRouteRange(1, 1), looped.Combats);
        Assert.Equal(new MapRouteRange(1, 1), looped.Elites);
    }

    /// <summary>A wide map must stay bounded: the policy caps paths per option instead of exploding.</summary>
    public static void WideGraphsStayBounded()
    {
        var nodes = new List<MapRouteNode> { Node("0,0", "Monster") };
        var firstRow = new List<string>();
        for (var column = 0; column < 6; column++)
        {
            firstRow.Add($"1,{column}");
        }

        for (var row = 1; row < 8; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                var children = row == 7
                    ? Array.Empty<string>()
                    : Enumerable.Range(0, 6).Select(next => $"{row + 1},{next}").ToArray();
                nodes.Add(Node($"{row},{column}", column % 2 == 0 ? "Monster" : "Unknown", children));
            }
        }

        var input = new MapRouteInput(
            nodes,
            firstRow.Take(2).Select((coord, index) => Start(index, coord)).ToArray(),
            40,
            80,
            0,
            2);

        var advice = MapRoutePolicy.Advise(input);
        Assert.NotNull(advice);
        Assert.Equal(2, advice!.Options.Count);
        foreach (var option in advice.Options)
        {
            Assert.True(option.Combats.Max <= MapRoutePolicy.MaxPathLength, "a path never exceeds the node cap");
            Assert.True(option.Unknowns.Max <= MapRoutePolicy.MaxPathLength);
        }
    }

    /// <summary>HP below the low-health line puts the heavier weight on rests and away from elites.</summary>
    public static void LowHealthWeighsRestsMoreAndElitesLess()
    {
        var healthy = MapRoutePolicy.Advise(Fork(hp: 78))!;
        var wounded = MapRoutePolicy.Advise(Fork(hp: 16))!;

        Assert.True(
            wounded.Options[0].Score < healthy.Options[0].Score,
            "the elite door is worth less at 20% HP");
        Assert.Contains("20% HP", wounded.Options[0].Why, StringComparison.Ordinal);
    }
}
