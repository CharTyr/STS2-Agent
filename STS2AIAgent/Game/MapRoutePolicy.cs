namespace STS2AIAgent.Game;

/// <summary>One map node as the route arithmetic sees it. <see cref="Coord"/> is the game's "row,col".</summary>
internal sealed record MapRouteNode(string Coord, string NodeType, bool Visited, IReadOnlyList<string> Children);

/// <summary>One currently selectable map option: the handler's <c>option_index</c> and its node coord.</summary>
internal readonly record struct MapRouteStart(int Index, string Coord);

/// <summary>The run facts that change which route is worth taking, all already numbers.</summary>
internal sealed record MapRouteInput(
    IReadOnlyList<MapRouteNode> Nodes,
    IReadOnlyList<MapRouteStart> Starts,
    int Hp,
    int MaxHp,
    int Gold,
    int Act);

/// <summary>The [least, most] times a room type appears across the paths behind one option.</summary>
internal readonly record struct MapRouteRange(int Min, int Max)
{
    public override string ToString() => Min == Max ? Min.ToString() : $"{Min}-{Max}";
}

internal sealed record MapRouteOption(
    int Index,
    MapRouteRange Elites,
    MapRouteRange Rests,
    MapRouteRange Shops,
    MapRouteRange Unknowns,
    MapRouteRange Combats,
    MapRouteRange Treasures,
    int Score,
    string Why);

internal sealed record MapRouteAdvice(IReadOnlyList<MapRouteOption> Options, int? Recommended, string Note);

/// <summary>
/// Turns the map graph into "what is behind each of these doors": for every currently selectable node,
/// every path to the boss is enumerated and the room types on those paths are summarised as a
/// [least, most] range, plus a deterministic score and a one-line reason.
/// </summary>
/// <remarks>
/// Pure graph work on names the raw map payload already publishes (<c>node_type</c>, <c>children</c>,
/// <c>available_nodes</c>), so it is tested offline; the game-side half only maps payloads to
/// <see cref="MapRouteNode"/>. Advisory by construction: the numbers are counts of room types on a
/// path, not a claim about what happens in those rooms, and <see cref="MapRouteAdvice.Note"/> says so.
///
/// Bounds, because a map is a DAG the game does not have to promise stays one: a path never revisits a
/// coordinate, never exceeds <see cref="MaxPathLength"/> nodes, and at most
/// <see cref="MaxPathsPerStart"/> paths per option are enumerated (children are visited in coordinate
/// order, so the cap truncates the same way every frame). The score is integers only, so the same map
/// and the same HP/gold always produce the same number:
/// <list type="bullet">
/// <item>a rest is worth <c>6</c>, plus another <c>6</c> below <c>45%</c> HP;</item>
/// <item>a shop is worth <c>3</c>, or <c>7</c> with at least <c>120</c> gold;</item>
/// <item>an unknown room is worth <c>2</c> and a treasure <c>2</c>; a plain combat costs <c>2</c>;</item>
/// <item>an elite costs <c>16</c> below <c>45%</c> HP, <c>9</c> below <c>70%</c>, else <c>6</c>, times
/// the act number, because an act-2 elite is not an act-1 elite.</item>
/// </list>
/// The recommendation is the highest score; a tie recommends nothing rather than picking arbitrarily.
/// </remarks>
internal static class MapRoutePolicy
{
    /// <summary>What <see cref="MapRouteAdvice.Note"/> always says: these are counts, not a promise.</summary>
    public const string AdvisoryNote = "advisory";

    internal const int MaxPathsPerStart = 128;

    internal const int MaxPathLength = 40;

    internal const int RestWeight = 6;

    internal const int LowHpRestBonus = 6;

    internal const int ShopWeight = 3;

    internal const int FundedShopWeight = 7;

    internal const int FundedShopGold = 120;

    internal const int UnknownWeight = 2;

    internal const int TreasureWeight = 2;

    internal const int CombatWeight = -2;

    internal const int ElitePenaltyLowHp = 16;

    internal const int ElitePenaltyMidHp = 9;

    internal const int ElitePenaltyHighHp = 6;

    internal const int LowHpPercent = 45;

    internal const int MidHpPercent = 70;

    /// <summary>
    /// The advice for one map frame, or null when there is nothing to advise on: fewer than two
    /// selectable nodes (no choice to make) or no resolvable graph.
    /// </summary>
    public static MapRouteAdvice? Advise(MapRouteInput input)
    {
        if (input == null || input.Starts.Count < 2 || input.Nodes.Count == 0)
        {
            return null;
        }

        var nodes = new Dictionary<string, MapRouteNode>(StringComparer.Ordinal);
        foreach (var node in input.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Coord))
            {
                continue;
            }

            // First writer wins: a duplicated coordinate is a malformed frame, not a second node.
            nodes.TryAdd(node.Coord, node);
        }

        var hpPercent = input.MaxHp > 0
            ? Math.Clamp((int)((long)Math.Max(0, input.Hp) * 100 / input.MaxHp), 0, 100)
            : 100;
        var act = Math.Clamp(input.Act, 1, 3);

        var options = new List<MapRouteOption>();
        foreach (var start in input.Starts.OrderBy(item => item.Index))
        {
            if (string.IsNullOrWhiteSpace(start.Coord) || !nodes.ContainsKey(start.Coord))
            {
                continue;
            }

            var tallies = new List<PathTally>();
            var path = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Walk(nodes, start.Coord, path, seen, tallies);
            if (tallies.Count == 0)
            {
                // No children at all (the map is the last row): the option itself is the path.
                tallies.Add(Tally(new[] { nodes[start.Coord] }));
            }

            var elites = Range(tallies, tally => tally.Elites);
            var rests = Range(tallies, tally => tally.Rests);
            var shops = Range(tallies, tally => tally.Shops);
            var unknowns = Range(tallies, tally => tally.Unknowns);
            var combats = Range(tallies, tally => tally.Combats);
            var treasures = Range(tallies, tally => tally.Treasures);
            var score = Score(elites, rests, shops, unknowns, combats, treasures, hpPercent, input.Gold, act);
            options.Add(new MapRouteOption(
                start.Index,
                elites,
                rests,
                shops,
                unknowns,
                combats,
                treasures,
                score,
                Explain(elites, rests, shops, unknowns, score, hpPercent)));
        }

        if (options.Count < 2)
        {
            return null;
        }

        int? recommended = null;
        var best = options.Max(option => option.Score);
        var leaders = options.Where(option => option.Score == best).ToList();
        if (leaders.Count == 1)
        {
            recommended = leaders[0].Index;
        }

        return new MapRouteAdvice(options, recommended, AdvisoryNote);
    }

    private sealed class PathTally
    {
        public int Elites;
        public int Rests;
        public int Shops;
        public int Unknowns;
        public int Combats;
        public int Treasures;
    }

    private static void Walk(
        IReadOnlyDictionary<string, MapRouteNode> nodes,
        string coord,
        List<string> path,
        HashSet<string> seen,
        List<PathTally> tallies)
    {
        if (tallies.Count >= MaxPathsPerStart || path.Count >= MaxPathLength || !seen.Add(coord))
        {
            return;
        }

        path.Add(coord);
        var node = nodes[coord];
        var children = ResolvedChildren(nodes, node, seen);
        if (children.Count == 0 || IsBoss(node.NodeType))
        {
            tallies.Add(Tally(path.Select(item => nodes[item])));
        }
        else
        {
            foreach (var child in children)
            {
                Walk(nodes, child, path, seen, tallies);
                if (tallies.Count >= MaxPathsPerStart)
                {
                    break;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        seen.Remove(coord);
    }

    /// <summary>Children in coordinate order, ignoring duplicates and coordinates the frame omits.</summary>
    private static List<string> ResolvedChildren(
        IReadOnlyDictionary<string, MapRouteNode> nodes,
        MapRouteNode node,
        HashSet<string> seen)
    {
        var children = new List<string>();
        foreach (var child in node.Children ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(child) || seen.Contains(child) || !nodes.ContainsKey(child))
            {
                continue;
            }

            if (!children.Contains(child, StringComparer.Ordinal))
            {
                children.Add(child);
            }
        }

        children.Sort(StringComparer.Ordinal);
        return children;
    }

    private static bool IsBoss(string nodeType)
    {
        return string.Equals(nodeType, "Boss", StringComparison.OrdinalIgnoreCase);
    }

    private static PathTally Tally(IEnumerable<MapRouteNode> path)
    {
        var tally = new PathTally();
        foreach (var node in path)
        {
            if (string.Equals(node.NodeType, "Elite", StringComparison.OrdinalIgnoreCase))
            {
                tally.Elites++;
            }
            else if (string.Equals(node.NodeType, "RestSite", StringComparison.OrdinalIgnoreCase))
            {
                tally.Rests++;
            }
            else if (string.Equals(node.NodeType, "Shop", StringComparison.OrdinalIgnoreCase))
            {
                tally.Shops++;
            }
            else if (string.Equals(node.NodeType, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                tally.Unknowns++;
            }
            else if (string.Equals(node.NodeType, "Monster", StringComparison.OrdinalIgnoreCase))
            {
                tally.Combats++;
            }
            else if (string.Equals(node.NodeType, "Treasure", StringComparison.OrdinalIgnoreCase))
            {
                tally.Treasures++;
            }
        }

        return tally;
    }

    private static MapRouteRange Range(IReadOnlyList<PathTally> tallies, Func<PathTally, int> read)
    {
        var min = int.MaxValue;
        var max = int.MinValue;
        foreach (var tally in tallies)
        {
            var value = read(tally);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return tallies.Count == 0 ? new MapRouteRange(0, 0) : new MapRouteRange(min, max);
    }

    private static int Score(
        MapRouteRange elites,
        MapRouteRange rests,
        MapRouteRange shops,
        MapRouteRange unknowns,
        MapRouteRange combats,
        MapRouteRange treasures,
        int hpPercent,
        int gold,
        int act)
    {
        var elitePenalty = hpPercent < LowHpPercent
            ? ElitePenaltyLowHp
            : hpPercent < MidHpPercent
                ? ElitePenaltyMidHp
                : ElitePenaltyHighHp;
        var restWeight = RestWeight + (hpPercent < LowHpPercent ? LowHpRestBonus : 0);
        var shopWeight = gold >= FundedShopGold ? FundedShopWeight : ShopWeight;

        return Expected(rests) * restWeight
            + Expected(shops) * shopWeight
            + Expected(unknowns) * UnknownWeight
            + Expected(treasures) * TreasureWeight
            + Expected(combats) * CombatWeight
            - Expected(elites) * elitePenalty * act;
    }

    /// <summary>The middle of a range, rounded up, so every count stays an integer.</summary>
    /// <remarks>
    /// Rounding up rather than down is deliberate: a [0,1] room count means half the paths have it, and
    /// flooring that to 0 would score every mixed path as if the room were never there.
    /// </remarks>
    private static int Expected(MapRouteRange range) => (range.Min + range.Max + 1) / 2;

    private static string Explain(
        MapRouteRange elites,
        MapRouteRange rests,
        MapRouteRange shops,
        MapRouteRange unknowns,
        int score,
        int hpPercent)
    {
        var parts = new List<string>();
        if (elites.Max == 0)
        {
            parts.Add("no elites");
        }
        else if (hpPercent < LowHpPercent)
        {
            parts.Add($"{elites} elite at {hpPercent}% HP");
        }
        else
        {
            parts.Add($"{elites} elite for rewards");
        }

        if (rests.Max > 0)
        {
            parts.Add($"{rests} rest");
        }

        if (shops.Max > 0)
        {
            parts.Add($"{shops} shop");
        }

        if (unknowns.Max > 0)
        {
            parts.Add($"{unknowns} unknown");
        }

        parts.Add($"score {score}");
        return string.Join(" | ", parts);
    }
}
