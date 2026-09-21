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

internal static partial class GameStateService
{

    private static bool IsPotionUsable(
        IScreenContext? currentScreen,
        CombatState? combatState,
        Player player,
        PotionModel? potion,
        CombatActionGate? combatActionGate = null)
    {
        if (potion == null || !IsPotionDiscardable(player, potion))
        {
            return false;
        }

        if (!potion.PassesCustomUsabilityCheck || !IsPotionTargetSupported(combatState, potion))
        {
            return false;
        }

        return potion.Usage switch
        {
            PotionUsage.AnyTime => true,
            PotionUsage.CombatOnly => CanUseCombatActions(currentScreen, combatState, out _, out _, combatActionGate),
            _ => false
        };
    }

    private static bool CanDiscardPotionsInCurrentScreen(IScreenContext? currentScreen)
    {
        return currentScreen is not (NRewardsScreen or NCardRewardSelectionScreen);
    }

    private static bool IsPotionDiscardable(Player player, PotionModel? potion)
    {
        return potion != null &&
            !potion.IsQueued &&
            !potion.Owner.Creature.IsDead &&
            player.CanUseOrRemovePotions;
    }

    public static bool PotionRequiresTarget(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => true,
            TargetType.AnyPlayer => PotionRequiresExplicitPlayerSelection(combatState, potion),
            TargetType.AnyAlly => combatState != null && GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false).Length > 0,
            _ => false
        };
    }

    private static bool IsPotionTargetSupported(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState).Length > 0,
            TargetType.AnyPlayer => PotionRequiresExplicitPlayerSelection(combatState, potion)
                ? GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: true).Length > 0
                : true,
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false).Length > 0,
            TargetType.TargetedNoCreature => true,
            _ => true
        };
    }

    private static bool PotionRequiresExplicitPlayerSelection(CombatState? combatState, PotionModel potion)
    {
        return combatState != null &&
            CombatManager.Instance.IsInProgress &&
            potion.Owner.RunState.Players.Count > 1 &&
            combatState.PlayerCreatures.Count(creature => creature.IsAlive) > 1;
    }
}
