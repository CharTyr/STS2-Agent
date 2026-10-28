namespace STS2AIAgent.Game;

/// <summary>One enemy intent: <see cref="TotalDamage"/> already multiplies per-hit damage by hits.</summary>
internal sealed record IntentMath(int TotalDamage, int Hits, bool? Unblockable);

internal sealed record EnemyMath(int Index, int Hp, int Block, bool Alive, bool Hittable, IReadOnlyList<IntentMath> Intents);

/// <summary>The exact total damage (all hits) one card deals to one enemy, from the game's preview.</summary>
internal sealed record CardVsEnemy(int EnemyIndex, int Damage, bool IgnoresBlock);

/// <summary>A hand card as the arithmetic sees it. <see cref="EnergyCost"/> is null for X-cost cards.</summary>
internal sealed record CardMath(int Index, uint? Cid, int? EnergyCost, bool CostsX, bool Playable, int Block, IReadOnlyList<CardVsEnemy> Vs);

internal sealed record CombatMathInput(
    int PlayerHp,
    int PlayerBlock,
    int Energy,
    int EndOfTurnSelfDamage,
    IReadOnlyList<EnemyMath> Enemies,
    IReadOnlyList<CardMath> Hand);

internal sealed record EnemyThreat(int Index, int Incoming, int Hits);

internal sealed record ThreatSummary(
    int Incoming,
    int? IncomingUnblockable,
    int Block,
    int BlockNeeded,
    int Unblocked,
    int HpAfter,
    bool Lethal,
    IReadOnlyList<EnemyThreat> PerEnemy,
    int EndOfTurnSelfDamage);

/// <summary>
/// <see cref="AfterBlock"/>: HP damage this card alone would deal (its damage less the enemy's block,
/// or all of it when it ignores block). <see cref="Kills"/>: that is at least the enemy's HP.
/// </summary>
internal sealed record VsCell(int EnemyIndex, int Damage, int AfterBlock, bool Kills);

internal sealed record KillOption(
    int EnemyIndex,
    bool Killable,
    IReadOnlyList<int> CardIndices,
    IReadOnlyList<uint?> CardCids,
    int Energy,
    int Damage,
    int Overkill);

internal sealed record BlockPlan(int Block, int Energy, IReadOnlyList<int> CardIndices, IReadOnlyList<uint?> CardCids);

/// <summary>
/// The combat arithmetic models get wrong: how much is coming, how much block is still needed, which
/// enemy the hand can kill this turn and with which cards, and the most block the energy can buy.
/// </summary>
/// <remarks>
/// Pure arithmetic on numbers the game itself previewed (the game-side half is
/// <c>GameStateService.CombatMath.cs</c>), so it is tested offline. Kill options and max block are
/// <em>estimates</em>: they ignore card draw, mid-turn buffs and on-play effects, and they say so in the
/// payload. An X-cost card spends all current energy (its preview already reflects that), so it never
/// combines with a card that costs energy. The subset search is exhaustive over at most
/// <see cref="MaxCardsSearched"/> candidates (4,096 subsets).
/// </remarks>
internal static class CombatMathPolicy
{
    public const int MaxCardsSearched = 12;

    public static ThreatSummary Threat(CombatMathInput input)
    {
        var perEnemy = new List<EnemyThreat>();
        var incoming = 0;
        var unblockable = 0;
        var unblockableKnown = false;
        foreach (var enemy in input.Enemies)
        {
            if (!enemy.Alive)
            {
                continue;
            }

            var enemyIncoming = 0;
            var hits = 0;
            foreach (var intent in enemy.Intents)
            {
                var damage = Math.Max(0, intent.TotalDamage);
                enemyIncoming += damage;
                hits += Math.Max(0, intent.Hits);
                if (intent.Unblockable is { } isUnblockable)
                {
                    unblockableKnown = true;
                    if (isUnblockable)
                    {
                        unblockable += damage;
                    }
                }
            }

            incoming += enemyIncoming;
            perEnemy.Add(new EnemyThreat(enemy.Index, enemyIncoming, hits));
        }

        var block = Math.Max(0, input.PlayerBlock);
        var blockable = incoming - unblockable;
        var blockNeeded = Math.Max(0, blockable - block);
        var unblocked = blockNeeded + unblockable;
        var selfDamage = Math.Max(0, input.EndOfTurnSelfDamage);
        var hpAfter = input.PlayerHp - unblocked - selfDamage;
        return new ThreatSummary(
            incoming,
            unblockableKnown ? unblockable : null,
            block,
            blockNeeded,
            unblocked,
            hpAfter,
            hpAfter <= 0,
            perEnemy,
            selfDamage);
    }

    public static VsCell Cell(CardVsEnemy vs, EnemyMath enemy)
    {
        var damage = Math.Max(0, vs.Damage);
        var afterBlock = vs.IgnoresBlock ? damage : Math.Max(0, damage - Math.Max(0, enemy.Block));
        return new VsCell(vs.EnemyIndex, damage, afterBlock, enemy.Alive && afterBlock >= enemy.Hp);
    }

    public static IReadOnlyList<KillOption> KillOptions(CombatMathInput input)
    {
        var candidates = Candidates(input);
        var options = new List<KillOption>();
        foreach (var enemy in input.Enemies)
        {
            if (!enemy.Alive || !enemy.Hittable)
            {
                continue;
            }

            if (enemy.Hp <= 0)
            {
                options.Add(new KillOption(enemy.Index, true, Array.Empty<int>(), Array.Empty<uint?>(), 0, 0, 0));
                continue;
            }

            int[]? best = null;
            var bestEnergy = 0;
            var bestDamage = 0;
            var bestReach = 0;
            foreach (var (subset, energy) in AffordableSubsets(candidates, input.Energy))
            {
                var damage = HpDamage(subset, enemy);
                bestReach = Math.Max(bestReach, damage);
                if (damage < enemy.Hp)
                {
                    continue;
                }

                if (best == null || IsBetterKill(subset, energy, damage, best, bestEnergy, bestDamage))
                {
                    best = subset.Select(card => card.Index).ToArray();
                    bestEnergy = energy;
                    bestDamage = damage;
                }
            }

            if (best == null)
            {
                options.Add(new KillOption(enemy.Index, false, Array.Empty<int>(), Array.Empty<uint?>(), 0, bestReach, 0));
                continue;
            }

            options.Add(new KillOption(
                enemy.Index,
                true,
                best,
                best.Select(index => candidates.First(card => card.Index == index).Cid).ToArray(),
                bestEnergy,
                bestDamage,
                bestDamage - enemy.Hp));
        }

        return options;
    }

    public static BlockPlan MaxBlock(CombatMathInput input)
    {
        var candidates = Candidates(input).Where(card => card.Block > 0).ToList();
        int[] best = Array.Empty<int>();
        var bestBlock = 0;
        var bestEnergy = 0;
        foreach (var (subset, energy) in AffordableSubsets(candidates, input.Energy))
        {
            var block = subset.Sum(card => card.Block);
            if (block == 0)
            {
                continue;
            }

            var indices = subset.Select(card => card.Index).ToArray();
            if (block > bestBlock
                || (block == bestBlock && (energy < bestEnergy
                    || (energy == bestEnergy && (indices.Length < best.Length
                        || (indices.Length == best.Length && Compare(indices, best) < 0))))))
            {
                best = indices;
                bestBlock = block;
                bestEnergy = energy;
            }
        }

        return new BlockPlan(
            bestBlock,
            bestBlock == 0 ? 0 : bestEnergy,
            best,
            best.Select(index => candidates.First(card => card.Index == index).Cid).ToArray());
    }

    private static List<CardMath> Candidates(CombatMathInput input) => input.Hand
        .Where(card => card.Playable && (card.CostsX || card.EnergyCost is >= 0))
        .Take(MaxCardsSearched)
        .ToList();

    private static int CostOf(CardMath card, int energy) => card.CostsX ? Math.Max(0, energy) : card.EnergyCost.GetValueOrDefault();

    /// <summary>Every non-empty subset the energy pays for, with what it costs.</summary>
    private static IEnumerable<(List<CardMath> Subset, int Energy)> AffordableSubsets(List<CardMath> candidates, int energy)
    {
        var available = Math.Max(0, energy);
        var count = candidates.Count;
        for (var mask = 1; mask < 1 << count; mask++)
        {
            var subset = new List<CardMath>();
            var cost = 0;
            var xCards = 0;
            for (var bit = 0; bit < count; bit++)
            {
                if ((mask & (1 << bit)) == 0)
                {
                    continue;
                }

                var card = candidates[bit];
                subset.Add(card);
                if (card.CostsX)
                {
                    xCards++;
                }
                else
                {
                    cost += CostOf(card, available);
                }
            }

            // An X card eats all the energy: at most one, and only beside free cards.
            if (xCards > 1 || (xCards == 1 && cost > 0))
            {
                continue;
            }

            var total = xCards == 1 ? available : cost;
            if (total <= available)
            {
                yield return (subset, total);
            }
        }
    }

    /// <summary>HP damage of a set of cards on one enemy: its block soaks once, across the whole set.</summary>
    private static int HpDamage(List<CardMath> subset, EnemyMath enemy)
    {
        var blockable = 0;
        var unblockable = 0;
        foreach (var card in subset)
        {
            foreach (var vs in card.Vs)
            {
                if (vs.EnemyIndex != enemy.Index)
                {
                    continue;
                }

                if (vs.IgnoresBlock)
                {
                    unblockable += Math.Max(0, vs.Damage);
                }
                else
                {
                    blockable += Math.Max(0, vs.Damage);
                }
            }
        }

        return Math.Max(0, blockable - Math.Max(0, enemy.Block)) + unblockable;
    }

    private static bool IsBetterKill(List<CardMath> subset, int energy, int damage, int[] best, int bestEnergy, int bestDamage)
    {
        if (energy != bestEnergy) return energy < bestEnergy;
        if (subset.Count != best.Length) return subset.Count < best.Length;
        if (damage != bestDamage) return damage < bestDamage;
        return Compare(subset.Select(card => card.Index).ToArray(), best) < 0;
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (left[i] != right[i]) return left[i].CompareTo(right[i]);
        }

        return left.Length.CompareTo(right.Length);
    }
}
