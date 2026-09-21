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

    private static RunPayload? BuildRunPayload(
        IScreenContext? currentScreen,
        CombatState? combatState,
        RunState? runState,
        CombatActionGate combatActionGate)
    {
        if (runState == null)
        {
            return null;
        }

        var player = GetLocalPlayer(runState);
        if (player == null)
        {
            return null;
        }

        var connectedPlayerIds = GetConnectedPlayerIds(runState);

        return new RunPayload
        {
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            ascension = runState.AscensionLevel,
            ascension_effects = BuildAscensionEffectPayloads(runState.AscensionLevel),
            floor = runState.TotalFloor,
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            max_energy = player.MaxEnergy,
            base_orb_slots = player.BaseOrbSlotCount,
            // RunState has no ActId; the fallback that asked for one never resolved.
            act_id = runState.CurrentActIndex.ToString(),
            boss_id = ResolveBossId(runState),
            deck = player.Deck.Cards.Select((card, index) => BuildDeckCardPayload(card, index)).ToArray(),
            relics = player.Relics.Select((relic, index) => BuildRunRelicPayload(relic, index)).ToArray(),
            players = runState!.Players
                .OrderBy(runState.GetPlayerSlotIndex)
                .Select(otherPlayer => BuildRunPlayerSummaryPayload(runState, otherPlayer, connectedPlayerIds, player.NetId))
                .ToArray(),
            potions = player.PotionSlots.Select((potion, index) =>
                BuildRunPotionPayload(currentScreen, combatState, player, potion, index, combatActionGate)).ToArray()
        };
    }

    private static string? ResolveBossId(RunState runState)
    {
        // RunState has no BossId, so the reflective fallback that used to follow never resolved.
        return runState.Act?.BossEncounter?.Id.Entry is { Length: > 0 } bossId ? bossId : null;
    }

    private static RunRelicPayload BuildRunRelicPayload(RelicModel relic, int index)
    {
        return new RunRelicPayload
        {
            index = index,
            relic_id = relic.Id.Entry,
            name = relic.Title.GetFormattedText(),
            description = RawTextOrNull(() => relic.DynamicDescription),
            // RelicModel has no Amount, which is what this read, so stack was null for every relic. The
            // number a player sees on a relic is DisplayAmount, shown when ShowCounter says so.
            stack = SafeReadBool(() => relic.ShowCounter) ? SafeReadNullableInt(() => relic.DisplayAmount) : null,
            is_melted = relic.IsMelted
        };
    }

    private static RunPotionPayload BuildRunPotionPayload(
        IScreenContext? currentScreen,
        CombatState? combatState,
        Player player,
        PotionModel? potion,
        int index,
        CombatActionGate combatActionGate)
    {
        var requiresTarget = potion != null && PotionRequiresTarget(combatState, potion);
        var targetIndexSpace = potion != null ? GetPotionTargetIndexSpace(combatState, potion) : null;
        var validTargetIndices = potion != null ? GetPotionTargetIndices(combatState, potion) : Array.Empty<int>();

        return new RunPotionPayload
        {
            index = index,
            potion_id = potion?.Id.Entry,
            name = potion?.Title.GetFormattedText(),
            description = potion != null ? RawTextOrNull(() => potion.DynamicDescription) : null,
            rarity = potion?.Rarity.ToString(),
            occupied = potion != null,
            usage = potion?.Usage.ToString(),
            target_type = potion?.TargetType.ToString(),
            is_queued = potion?.IsQueued ?? false,
            requires_target = requiresTarget,
            target_index_space = targetIndexSpace,
            valid_target_indices = validTargetIndices,
            can_use = IsPotionUsable(currentScreen, combatState, player, potion, combatActionGate),
            can_discard = CanDiscardPotionsInCurrentScreen(currentScreen) && IsPotionDiscardable(player, potion)
        };
    }

    private static DeckCardPayload BuildDeckCardPayload(CardModel card, int index)
    {
        var resolvedRulesText = GetResolvedCardRulesText(card);
        var dynamicValues = BuildCardDynamicValuePayloads(card);
        return new DeckCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            card_type = card.Type.ToString(),
            rarity = card.Rarity.ToString(),
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card),
            resolved_rules_text = resolvedRulesText,
            dynamic_values = dynamicValues
        };
    }

    private static RunPlayerSummaryPayload BuildRunPlayerSummaryPayload(
        RunState runState,
        Player player,
        IReadOnlyCollection<ulong> connectedPlayerIds,
        ulong localPlayerId)
    {
        return new RunPlayerSummaryPayload
        {
            player_id = NetIdToString(player.NetId),
            slot_index = runState.GetPlayerSlotIndex(player),
            is_local = player.NetId == localPlayerId,
            is_connected = connectedPlayerIds.Contains(player.NetId),
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            is_alive = player.Creature.IsAlive
        };
    }

    private static CardDynamicValuePayload[] BuildCardDynamicValuePayloads(CardModel? card)
    {
        if (card == null)
        {
            return Array.Empty<CardDynamicValuePayload>();
        }

        try
        {
            var previewSet = card.DynamicVars.Clone(card);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, card.CurrentTarget, previewSet);

            return previewSet.Values
                .Select(dynamicVar => new CardDynamicValuePayload
                {
                    name = dynamicVar.Name,
                    base_value = (int)dynamicVar.BaseValue,
                    current_value = (int)dynamicVar.PreviewValue,
                    enchanted_value = (int)dynamicVar.EnchantedValue,
                    is_modified = (int)dynamicVar.PreviewValue != (int)dynamicVar.BaseValue
                        || (int)dynamicVar.EnchantedValue != (int)dynamicVar.BaseValue,
                    was_just_upgraded = dynamicVar.WasJustUpgraded
                })
                .OrderBy(payload => payload.name, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<CardDynamicValuePayload>();
        }
    }

    private static AscensionEffectPayload[] BuildAscensionEffectPayloads(int ascensionLevel)
    {
        if (ascensionLevel <= 0)
        {
            return Array.Empty<AscensionEffectPayload>();
        }

        return Enumerable.Range(1, ascensionLevel)
            .Select(level => new AscensionEffectPayload
            {
                id = $"LEVEL_{level:D2}",
                name = AscensionHelper.GetTitle(level).GetFormattedText(),
                description = AscensionHelper.GetDescription(level).GetFormattedText()
            })
            .ToArray();
    }

    private static string GetCardRulesText(CardModel? card)
    {
        if (card == null)
        {
            return string.Empty;
        }

        try
        {
            var rawDescription = card.Description?.GetRawText();
            if (!string.IsNullOrWhiteSpace(rawDescription))
            {
                return NormalizeCardRulesText(rawDescription);
            }
        }
        catch
        {
        }

        // The same Description, coerced another way when its raw text is empty. This used to try five
        // more names -- RulesText, Body, Text, RawText, DescriptionText -- none of which CardModel has.
        var coerced = TryCoerceText(card.Description);
        return string.IsNullOrWhiteSpace(coerced) ? string.Empty : NormalizeCardRulesText(coerced);
    }

    private static string GetResolvedCardRulesText(CardModel? card)
    {
        if (card == null)
        {
            return string.Empty;
        }

        try
        {
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, card.CurrentTarget, card.DynamicVars);
            var pileType = card.Pile?.Type ?? PileType.None;
            var resolved = card.GetDescriptionForPile(pileType, card.CurrentTarget);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return NormalizeCardRulesText(resolved);
            }
        }
        catch
        {
        }

        return GetCardRulesText(card);
    }
}
