using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Game;

/// <summary>
/// The <c>end_turn_will_kill_player</c> question: what happens to the player between the moment they
/// stop acting and the moment they may act again.
/// </summary>
/// <remarks>
/// This was the tail of <c>BuildCombatPayload</c> inside <c>GameStateService.Combat.cs</c> until
/// 2026-09-22, when adding damage-over-time risks pushed that file past its 1000-line budget. The
/// budget asks for a move rather than a raise, and the concern stands on its own: the intent sum, the
/// Sandpit countdown and the two damage-over-time powers are all answers to one question, and the
/// next risk source has an obvious place to land.
/// </remarks>
internal static partial class GameStateService
{
    private static CombatLethalRiskPayload[] BuildCombatLethalRiskPayloads(
        Creature playerCreature,
        CombatPlayerPayload player,
        CombatEnemyPayload[] enemies)
    {
        var risks = new List<CombatLethalRiskPayload>();
        var incomingDamage = enemies
            .Where(enemy => enemy.is_alive)
            .SelectMany(enemy => enemy.intents)
            .Sum(intent => Math.Max(0, intent.total_damage.GetValueOrDefault()));

        if (incomingDamage > 0)
        {
            var damageAfterBlock = Math.Max(0, incomingDamage - Math.Max(0, player.block));
            if (damageAfterBlock >= player.current_hp)
            {
                risks.Add(new CombatLethalRiskPayload
                {
                    risk_id = "incoming_damage",
                    source = "enemy_intents",
                    will_kill_player = true,
                    reason = "Enemy intent damage after current block is at least current HP.",
                    incoming_damage = incomingDamage,
                    damage_after_block = damageAfterBlock,
                    player_hp = player.current_hp,
                    player_block = player.block
                });
            }
        }

        foreach (var power in player.powers)
        {
            if (!IsSandpitPower(power) || power.amount is not int amount || amount > 1)
            {
                continue;
            }

            risks.Add(new CombatLethalRiskPayload
            {
                risk_id = "sandpit_countdown",
                source = "player_power",
                will_kill_player = true,
                reason = "SANDPIT_POWER is at or below 1; ending turn is treated as lethal unless the boss dies first or Frantic Escape has already raised the counter.",
                player_hp = player.current_hp,
                player_block = player.block,
                power_id = power.power_id,
                power_amount = amount
            });
        }

        // Damage that lands after the player has stopped acting, which the intent sum above cannot see.
        // Poison resolves at the start of the player's OWN next turn and is Unblockable, so no amount
        // of block saves it and it lands before a card can be played; Constrict resolves when the
        // player's own turn ends and is only blockable. strategy.md tells a model to trust
        // end_turn_will_kill_player, so both have to be in it -- they were the way a run ended with
        // the flag still false. Read live rather than from the payload because the exact poison total
        // (Accelerant multiplies the trigger count, every damage modifier applies) only exists on the
        // model.
        foreach (var power in playerCreature.Powers)
        {
            // Doom is not damage. BeforeTurnEnd kills the owner when CurrentHp <= Amount, and block
            // does not enter that comparison. A damage row would under-report it.
            if (power is DoomPower doom
                && SafeReadNullableInt(() => doom.Amount) is int doomAmount
                && doomAmount >= player.current_hp)
            {
                risks.Add(new CombatLethalRiskPayload
                {
                    risk_id = "doom_turn_end",
                    source = "player_power",
                    will_kill_player = true,
                    reason = "Doom kills you at the end of your own turn once its amount is at least your current HP. Block does not count.",
                    player_hp = player.current_hp,
                    player_block = player.block,
                    power_id = SafeReadString(() => doom.Id.Entry),
                    power_amount = SafeReadNullableInt(() => doom.Amount)
                });
                continue;
            }

            var blockable = power is ConstrictPower;
            var riskId = power switch
            {
                PoisonPower => "poison_next_turn",
                ConstrictPower => "constrict_turn_end",
                MagicBombPower => "magic_bomb_turn_end",
                _ => null
            };
            var damage = power switch
            {
                PoisonPower poison => SafeReadNullableInt(() => poison.CalculateTotalDamageNextTurn()),
                ConstrictPower constrict => SafeReadNullableInt(() => constrict.Amount),
                // AfterTurnEnd damages the owner for Amount. The player-played bomb is deliberately
                // absent: its BeforeTurnEnd damages hittable enemies, not the player who played it.
                MagicBombPower magicBomb => SafeReadNullableInt(() => magicBomb.Amount),
                _ => null
            };
            var damageAfterBlock = blockable
                ? Math.Max(0, damage.GetValueOrDefault() - Math.Max(0, player.block))
                : damage.GetValueOrDefault();
            if (riskId == null || damage is not > 0 || damageAfterBlock < player.current_hp)
            {
                continue;
            }

            risks.Add(new CombatLethalRiskPayload
            {
                risk_id = riskId,
                source = "player_power",
                will_kill_player = true,
                reason = power switch
                {
                    ConstrictPower => "Constrict damages you when your own turn ends; current block does not cover it.",
                    MagicBombPower => "Magic Bomb damages you after your own turn ends and ignores block.",
                    _ => "Poison resolves at the start of your next turn and ignores block, so it lands before you can play another card; ending the fight this turn avoids it."
                },
                incoming_damage = damage,
                damage_after_block = damageAfterBlock,
                player_hp = player.current_hp,
                player_block = player.block,
                power_id = SafeReadString(() => power.Id.Entry),
                power_amount = SafeReadNullableInt(() => power.Amount)
            });
        }

        return risks.ToArray();
    }

    private static bool IsSandpitPower(CombatPowerPayload power)
    {
        return string.Equals(power.power_id, "SANDPIT_POWER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(power.name, "Sandpit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(power.name, "沙坑", StringComparison.OrdinalIgnoreCase);
    }
}
