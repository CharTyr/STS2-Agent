using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Game;

/// <summary>
/// Combat arithmetic for the compact view (W3/W4): the stable per-combat card id, the card type, the
/// exact damage each attack deals to each enemy, and the threat summary built on top of them.
/// </summary>
/// <remarks>
/// Every number comes from the game's own preview path -- <c>UpdateDynamicVarPreview</c> with the
/// enemy as the target runs the same damage hooks as the real attack (Strength, Weak, the target's
/// Vulnerable, caps) -- on a cloned variable set, so the cards the player sees are never touched. The
/// existing hand preview passes <c>CurrentTarget</c>, which is usually null, so it under-reports a
/// Vulnerable target; that is the gap this fills.
///
/// Hit counts are only reported when they are known: from the <c>CalculatedHits</c> or <c>Repeat</c>
/// variable, or from the few cards whose code hard-codes two hits (listed below, read from the game's
/// own card classes). A card whose hits are computed at play time gets <c>hits: null</c> and no total
/// -- a wrong number is worse than none. Each preview is wrapped so a failure omits that cell.
///
/// The work is cached per frame-state key (round, energy, hand, enemy HP/block/powers, player powers):
/// a state build happens after every action and on every SSE tick, while the numbers only change when
/// the fight does.
/// </remarks>
internal static partial class GameStateService
{
    /// <summary>
    /// Cards whose class calls <c>WithHitCount(2)</c> with a literal (decompiled sts2.dll, 2026-08-16
    /// build), keyed by class name so a localized id cannot mismatch.
    /// </summary>
    private static readonly HashSet<string> LiteralTwoHitCards = new(StringComparer.Ordinal)
    {
        "AstralPulse", "DaggerSpray", "Maul", "Refract", "RipAndTear", "Thrash", "TwinStrike", "Uproar"
    };

    private static string? _combatMathKey;
    private static CombatMathFrame? _combatMathFrame;
    private static bool _combatMathFailureLogged;

    /// <summary>The compact threat object and each hand card's <c>vs</c> list, for one frame.</summary>
    internal sealed record CombatMathFrame(object Threat, IReadOnlyDictionary<int, object[]> VsByHandIndex);

    internal static uint? TryGetCombatCardId(CardModel card)
    {
        try
        {
            return NetCombatCardDb.Instance.TryGetCardId(card, out var id) ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string? FormatCardType(CardModel card)
    {
        try
        {
            return card.Type switch
            {
                CardType.Attack => "attack",
                CardType.Skill => "skill",
                CardType.Power => "power",
                CardType.Status => "status",
                CardType.Curse => "curse",
                CardType.Quest => "quest",
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool CombatMathEnabled()
    {
        try
        {
            return AgentRuntime.Instance?.Settings.CombatMathEnabled != false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// The arithmetic for the frame the compact view is rewriting, or null when it is switched off or
    /// there is no local fight. Game thread only.
    /// </summary>
    private static CombatMathFrame? BuildCombatMath(CombatState? combatState, CombatPayload combat, IReadOnlyList<CardModel> liveHand)
    {
        if (combatState == null || !CombatMathEnabled())
        {
            _combatMathKey = null;
            _combatMathFrame = null;
            return null;
        }

        try
        {
            var enemies = combatState.Enemies.ToList();
            var me = GetLocalPlayer(combatState);
            // The full combat payload covers HP, intents, hittability, targeting, costs and dynamic
            // values. Frame + combat identity also prevent reuse across fights or hidden hook changes.
            var key = Godot.Engine.GetProcessFrames() + "|"
                + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(combatState) + "|"
                + System.Text.Json.JsonSerializer.Serialize(combat);
            if (key == _combatMathKey && _combatMathFrame != null)
            {
                return _combatMathFrame;
            }

            var frame = ComputeCombatMath(combat, liveHand, enemies, me?.Creature);
            _combatMathKey = key;
            _combatMathFrame = frame;
            return frame;
        }
        catch (Exception ex)
        {
            if (!_combatMathFailureLogged)
            {
                _combatMathFailureLogged = true;
                Log.Warn($"[STS2AIAgent] Combat math unavailable: {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }

    private static CombatMathFrame ComputeCombatMath(
        CombatPayload combat,
        IReadOnlyList<CardModel> liveHand,
        IReadOnlyList<Creature> enemies,
        Creature? playerCreature)
    {
        var enemyMath = combat.enemies.Select(enemy => new EnemyMath(
            enemy.index,
            enemy.current_hp,
            enemy.block,
            enemy.is_alive,
            enemy.is_hittable,
            enemy.intents
                .Select(intent => new IntentMath(Math.Max(0, intent.total_damage.GetValueOrDefault()), Math.Max(0, intent.hits.GetValueOrDefault()), null))
                .ToArray())).ToArray();

        var vsByHand = new Dictionary<int, object[]>();
        var cards = new List<CardMath>();
        foreach (var card in combat.hand)
        {
            var live = card.index >= 0 && card.index < liveHand.Count ? liveHand[card.index] : null;
            var cells = live != null && card.playable ? PreviewCardAgainstEnemies(live, card, enemies) : new List<(int Enemy, int PerHit, int? Hits, bool IgnoresBlock)>();
            var known = new List<CardVsEnemy>();
            var rendered = new List<object>();
            foreach (var cell in cells)
            {
                var enemy = enemyMath.FirstOrDefault(candidate => candidate.Index == cell.Enemy);
                if (enemy == null)
                {
                    continue;
                }

                if (cell.Hits is { } hits)
                {
                    var vs = new CardVsEnemy(cell.Enemy, cell.PerHit * hits, cell.IgnoresBlock);
                    known.Add(vs);
                    var math = CombatMathPolicy.Cell(vs, enemy);
                    rendered.Add(new { enemy = cell.Enemy, per_hit = cell.PerHit, hits, damage = math.Damage, after_block = math.AfterBlock, kills = math.Kills });
                }
                else
                {
                    rendered.Add(new { enemy = cell.Enemy, per_hit = cell.PerHit, hits = (int?)null, damage = (int?)null, after_block = (int?)null, kills = (bool?)null });
                }
            }

            if (rendered.Count > 0)
            {
                vsByHand[card.index] = rendered.ToArray();
            }

            cards.Add(new CardMath(
                card.index,
                card.combat_card_id,
                card.costs_x ? null : card.energy_cost,
                card.costs_x,
                card.playable,
                ReadPreviewBlock(card) * (1 + Math.Max(0, live?.GetEnchantedReplayCount() ?? 0)),
                known));
        }

        var input = new CombatMathInput(
            combat.player.current_hp,
            combat.player.block,
            combat.player.energy,
            playerCreature == null ? 0 : ReadEndOfTurnSelfDamage(playerCreature),
            enemyMath,
            cards);
        var threat = CombatMathPolicy.Threat(input);
        var kills = CombatMathPolicy.KillOptions(input);
        var block = CombatMathPolicy.MaxBlock(input);

        return new CombatMathFrame(new
        {
            incoming = threat.Incoming,
            incoming_unblockable = threat.IncomingUnblockable,
            block = threat.Block,
            block_needed = threat.BlockNeeded,
            unblocked = threat.Unblocked,
            hp_after = threat.HpAfter,
            // Never weaker than the flag the strategy text tells the model to trust.
            lethal = threat.Lethal || combat.end_turn_will_kill_player,
            end_of_turn_self_damage = threat.EndOfTurnSelfDamage,
            per_enemy = threat.PerEnemy.Select(enemy => new { i = enemy.Index, incoming = enemy.Incoming, hits = enemy.Hits }).ToArray(),
            max_block = new { block = block.Block, energy = block.Energy, cids = block.CardCids, cards = block.CardIndices },
            kill_options = kills.Select(option => new
            {
                enemy = option.EnemyIndex,
                killable = option.Killable,
                cids = option.CardCids,
                cards = option.CardIndices,
                energy = option.Energy,
                damage = option.Damage,
                overkill = option.Overkill
            }).ToArray(),
            estimate = true
        }, vsByHand);
    }

    /// <summary>
    /// One (enemy, per-hit damage, hits, ignores-block) cell per enemy this card can hit, from the
    /// game's preview against that enemy. Empty for a card with no damage variable.
    /// </summary>
    private static List<(int Enemy, int PerHit, int? Hits, bool IgnoresBlock)> PreviewCardAgainstEnemies(
        CardModel card,
        CombatHandCardPayload payload,
        IReadOnlyList<Creature> enemies)
    {
        var cells = new List<(int, int, int?, bool)>();
        if (!card.DynamicVars.ContainsKey("Damage") && !card.DynamicVars.ContainsKey("CalculatedDamage"))
        {
            return cells;
        }

        // Untargeted does NOT mean AoE: random-target and self-damage cards have no guaranteed
        // per-enemy total. Omit them rather than advertising phantom lethal damage.
        if (card.TargetType is not (TargetType.AnyEnemy or TargetType.AllEnemies)) return cells;
        IEnumerable<int> candidates = payload.requires_target
            ? payload.valid_target_indices
            : Enumerable.Range(0, enemies.Count);
        foreach (var index in candidates)
        {
            if (index < 0 || index >= enemies.Count)
            {
                continue;
            }

            var enemy = enemies[index];
            if (!enemy.IsAlive || !enemy.IsHittable)
            {
                continue;
            }

            try
            {
                var preview = card.DynamicVars.Clone(card);
                card.UpdateDynamicVarPreview(CardPreviewMode.Normal, enemy, preview);
                decimal perHit;
                ValueProp props;
                if (preview.TryGetValue("CalculatedDamage", out var calculated) && calculated is CalculatedDamageVar calculatedDamage)
                {
                    perHit = calculatedDamage.PreviewValue;
                    props = calculatedDamage.Props;
                }
                else if (preview.TryGetValue("Damage", out var plain) && plain is DamageVar damage)
                {
                    perHit = damage.PreviewValue;
                    props = damage.Props;
                }
                else
                {
                    continue;
                }

                var hits = ReadHitCount(card, preview);
                if (hits is { } known)
                {
                    hits = known * (1 + Math.Max(0, card.GetEnchantedReplayCount()));
                }

                cells.Add((index, Math.Max(0, (int)perHit), hits, props.HasFlag(ValueProp.Unblockable)));
            }
            catch (Exception)
            {
                // This cell is omitted rather than guessed.
            }
        }

        return cells;
    }

    private static int? ReadHitCount(CardModel card, DynamicVarSet preview)
    {
        if (preview.TryGetValue("CalculatedHits", out var calculatedHits))
        {
            return Math.Max(0, (int)calculatedHits.PreviewValue);
        }

        if (card.Type == CardType.Attack && preview.TryGetValue("Repeat", out var repeat))
        {
            return Math.Max(0, (int)repeat.PreviewValue);
        }

        if (LiteralTwoHitCards.Contains(card.GetType().Name))
        {
            return 2;
        }

        // No generic one-hit assumption: card play code may compute repetitions without exposing
        // a variable. Only the inspected basic Strike implementations and Bash are known single hits.
        if (card.EnergyCost.CostsX || ComputedHitCards.Contains(card.GetType().Name)) return null;
        return card.GetType().Name is "StrikeIronclad" or "StrikeSilent" or "StrikeDefect"
            or "StrikeRegent" or "StrikeNecrobinder" or "Bash" ? 1 : null;
    }

    /// <summary>Cards whose hit count is computed in their play code from live counters.</summary>
    private static readonly HashSet<string> ComputedHitCards = new(StringComparer.Ordinal)
    {
        "Dismantle", "HeavenlyDrill", "MadScience", "Spite", "Whirlwind"
    };

    private static int ReadPreviewBlock(CombatHandCardPayload card)
    {
        var value = card.dynamic_values.FirstOrDefault(entry => entry.name == "CalculatedBlock")
            ?? card.dynamic_values.FirstOrDefault(entry => entry.name == "Block");
        return value == null ? 0 : Math.Max(0, value.current_value);
    }

    /// <summary>
    /// Damage the player takes from their own powers between ending the turn and acting again: the
    /// same three sources <c>BuildCombatLethalRiskPayloads</c> reads (Poison next turn, Constrict and
    /// Magic Bomb at turn end), summed. Doom is not damage and reaches the threat through
    /// <c>end_turn_will_kill_player</c>.
    /// </summary>
    private static int ReadEndOfTurnSelfDamage(Creature playerCreature)
    {
        var total = 0;
        foreach (var power in playerCreature.Powers)
        {
            var damage = power switch
            {
                PoisonPower poison => SafeReadNullableInt(() => poison.CalculateTotalDamageNextTurn()),
                ConstrictPower constrict => SafeReadNullableInt(() => constrict.Amount),
                MagicBombPower magicBomb => SafeReadNullableInt(() => magicBomb.Amount),
                _ => null
            };
            total += Math.Max(0, damage.GetValueOrDefault());
        }

        return total;
    }

}
