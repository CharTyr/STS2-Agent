using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// The combat arithmetic handed to the model (W4): threat, per-cell damage, kill options, max block.
/// </summary>
internal static class CombatMathPolicyTests
{
    private static EnemyMath Enemy(int index, int hp, int block = 0, bool alive = true, bool hittable = true, params int[] attacks) =>
        new(index, hp, block, alive, hittable, attacks.Select(damage => new IntentMath(damage, 1, null)).ToArray());

    private static CardMath Attack(int index, int cost, params (int Enemy, int Damage)[] vs) =>
        new(index, (uint)(100 + index), cost, false, true, 0, vs.Select(cell => new CardVsEnemy(cell.Enemy, cell.Damage, false)).ToArray());

    private static CardMath Defend(int index, int cost, int block) =>
        new(index, (uint)(100 + index), cost, false, true, block, Array.Empty<CardVsEnemy>());

    private static CombatMathInput Input(int hp, int block, int energy, IReadOnlyList<EnemyMath> enemies, IReadOnlyList<CardMath> hand, int self = 0) =>
        new(hp, block, energy, self, enemies, hand);

    public static void ThreatSumsLivingAttackersAndBlock()
    {
        var none = CombatMathPolicy.Threat(Input(30, 0, 3, Array.Empty<EnemyMath>(), Array.Empty<CardMath>()));
        Assert.Equal(0, none.Incoming);
        Assert.False(none.Lethal);

        var threat = CombatMathPolicy.Threat(Input(20, 5, 3, new[]
        {
            Enemy(0, 30, attacks: new[] { 6, 4 }),
            Enemy(1, 0, alive: false, attacks: 99),
            Enemy(2, 12, attacks: 8)
        }, Array.Empty<CardMath>()));
        Assert.Equal(18, threat.Incoming, "The dead enemy's intent does not count.");
        Assert.Null(threat.IncomingUnblockable);
        Assert.Equal(13, threat.BlockNeeded);
        Assert.Equal(13, threat.Unblocked);
        Assert.Equal(7, threat.HpAfter);
        Assert.False(threat.Lethal);
        Assert.Equal(2, threat.PerEnemy.Count);
        Assert.Equal(10, threat.PerEnemy[0].Incoming);
        Assert.Equal(2, threat.PerEnemy[0].Hits);
        Assert.Equal(2, threat.PerEnemy[1].Index);
    }

    public static void ThreatCountsUnblockableAndSelfDamage()
    {
        var enemies = new[]
        {
            new EnemyMath(0, 20, 0, true, true, new[] { new IntentMath(10, 1, false), new IntentMath(5, 1, true) })
        };
        var threat = CombatMathPolicy.Threat(Input(12, 20, 3, enemies, Array.Empty<CardMath>(), self: 4));
        Assert.Equal(5, threat.IncomingUnblockable);
        Assert.Equal(0, threat.BlockNeeded, "Block covers the blockable 10.");
        Assert.Equal(5, threat.Unblocked);
        Assert.Equal(3, threat.HpAfter, "12 − 5 unblockable − 4 from own powers.");
        Assert.False(threat.Lethal);

        var lethal = CombatMathPolicy.Threat(Input(9, 20, 3, enemies, Array.Empty<CardMath>(), self: 4));
        Assert.True(lethal.Lethal);
        Assert.Equal(0, lethal.HpAfter);
    }

    public static void CellsSoakBlockAndKillExactly()
    {
        var shielded = Enemy(0, 10, block: 4);
        Assert.Equal(6, CombatMathPolicy.Cell(new CardVsEnemy(0, 10, false), shielded).AfterBlock);
        Assert.False(CombatMathPolicy.Cell(new CardVsEnemy(0, 10, false), shielded).Kills);
        Assert.True(CombatMathPolicy.Cell(new CardVsEnemy(0, 14, false), shielded).Kills, "Exactly HP after block kills.");
        Assert.Equal(10, CombatMathPolicy.Cell(new CardVsEnemy(0, 10, true), shielded).AfterBlock);
        Assert.False(CombatMathPolicy.Cell(new CardVsEnemy(0, 99, false), Enemy(0, 5, alive: false)).Kills);
    }

    public static void KillOptionsPreferTheCheapestSet()
    {
        var input = Input(30, 0, 3, new[] { Enemy(0, 12, block: 3), Enemy(1, 40), Enemy(2, 5, hittable: false) }, new[]
        {
            Attack(0, 2, (0, 15), (1, 15)),
            Attack(1, 1, (0, 8), (1, 8)),
            Attack(2, 1, (0, 7), (1, 7)),
            new CardMath(3, 103, 1, false, false, 0, new[] { new CardVsEnemy(0, 50, false) })
        });
        var options = CombatMathPolicy.KillOptions(input);
        Assert.Equal(2, options.Count, "The unhittable enemy is not listed.");

        var first = options[0];
        Assert.True(first.Killable);
        Assert.Equal(2, first.Energy);
        Assert.Equal(1, first.CardIndices.Count, "Card 0 alone and cards 1+2 both cost 2; fewer cards wins.");
        Assert.Equal(0, first.CardIndices[0]);
        Assert.Equal(12, first.Damage);
        Assert.Equal(0, first.Overkill);
        Assert.Equal((uint?)100, first.CardCids[0]);
        Assert.False(first.CardIndices.Contains(3), "An unplayable card is never part of a plan.");

        var second = options[1];
        Assert.False(second.Killable, "40 HP is out of reach for 3 energy.");
        Assert.Equal(23, second.Damage, "The best reachable damage is reported: cards 0 + 1 for all 3 energy.");
        Assert.Equal(0, second.CardIndices.Count);
    }

    public static void KillOptionsSoakEnemyBlockOnce()
    {
        var input = Input(30, 0, 2, new[] { Enemy(0, 10, block: 5) }, new[] { Attack(0, 1, (0, 8)), Attack(1, 1, (0, 7)) });
        var option = CombatMathPolicy.KillOptions(input)[0];
        Assert.True(option.Killable, "8 + 7 − 5 block = 10: block soaks once across the set.");
        Assert.Equal(2, option.CardIndices.Count);
    }

    public static void XCostCardsSpendEverything()
    {
        var x = new CardMath(0, 100, null, true, true, 0, new[] { new CardVsEnemy(0, 15, false) });
        var oneCost = Attack(1, 1, (0, 6));
        var input = Input(30, 0, 3, new[] { Enemy(0, 20) }, new[] { x, oneCost });
        var option = CombatMathPolicy.KillOptions(input)[0];
        Assert.False(option.Killable, "X (15) cannot be combined with a 1-cost card.");
        Assert.Equal(15, option.Damage);

        var alone = CombatMathPolicy.KillOptions(Input(30, 0, 3, new[] { Enemy(0, 15) }, new[] { x }))[0];
        Assert.True(alone.Killable);
        Assert.Equal(3, alone.Energy);

        var trivial = CombatMathPolicy.KillOptions(Input(30, 0, 0, new[] { Enemy(0, 0) }, Array.Empty<CardMath>()))[0];
        Assert.True(trivial.Killable);
        Assert.Equal(0, trivial.CardIndices.Count);
    }

    public static void MaxBlockPicksTheBestAffordableSet()
    {
        var input = Input(30, 0, 2, Array.Empty<EnemyMath>(), new[]
        {
            Defend(0, 1, 5), Defend(1, 1, 5), Defend(2, 2, 12), Attack(3, 0, (0, 6))
        });
        var plan = CombatMathPolicy.MaxBlock(input);
        Assert.Equal(12, plan.Block, "One 12 for 2 energy beats two 5s.");
        Assert.Equal(2, plan.Energy);
        Assert.Equal(2, plan.CardIndices[0]);

        var tie = CombatMathPolicy.MaxBlock(Input(30, 0, 2, Array.Empty<EnemyMath>(), new[] { Defend(0, 2, 10), Defend(1, 1, 10) }));
        Assert.Equal(1, tie.Energy, "A tie in block goes to the cheaper set.");

        var none = CombatMathPolicy.MaxBlock(Input(30, 0, 3, Array.Empty<EnemyMath>(), new[] { Attack(0, 1, (0, 6)) }));
        Assert.Equal(0, none.Block);
        Assert.Equal(0, none.CardIndices.Count);
    }
}
