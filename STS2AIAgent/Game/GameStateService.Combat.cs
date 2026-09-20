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

    /// <summary>
    /// Every combat-action answer one state build reports -- available_actions, the readiness payload
    /// and the potion flags -- comes from this one evaluation. The readiness probe advances a shared
    /// 200ms stability sampler, so evaluating it more than once per payload let a response straddle
    /// that window and contradict itself: the actions serialized first said "not yet" while the
    /// readiness payload built later in the same response said "ready".
    /// </summary>
    public sealed class CombatActionGate
    {
        /// <summary>Combat state this gate was evaluated against; null outside a running fight.</summary>
        public CombatState? Source { get; init; }

        public bool Usable { get; init; }

        public string Reason { get; init; } = "combat_screen_unavailable";

        public Player? Me { get; init; }

        public NCombatRoom? Room { get; init; }

        public bool ModalOpen { get; init; }

        public string? ModalType { get; init; }

        public bool ActionsSettled { get; init; }

        public string? RunningActionType { get; init; }

        public string? ReadyActionType { get; init; }

        public bool? HandInCardPlay { get; init; }

        public bool? HandInCardSelection { get; init; }

        public string? HandMode { get; init; }

        public bool LocalTurnReady { get; init; }

        public bool SnapshotStable { get; init; }

        public bool PlayerActionPhase { get; init; }
    }

    private static CombatActionGate EvaluateCombatActionGate(IScreenContext? currentScreen, CombatState? combatState)
    {
        var modal = GetOpenModal();
        var room = FindActiveCombatRoom(currentScreen);
        var hand = room?.Ui?.Hand;
        var me = GetLocalPlayer(combatState);
        // Outside a fight there is no action queue to read: RunManager's executor and queue set are
        // empty on the main menu, and reading them there failed every /state request with a
        // NullReferenceException. The old readiness probe only ever ran inside combat, but this gate
        // runs before the screen checks, so the queue is only touched when a fight is up.
        MegaCrit.Sts2.Core.GameActions.GameAction? runningAction = null;
        MegaCrit.Sts2.Core.GameActions.GameAction? readyAction = null;
        var actionQueueHasExecutingAction = false;
        if (combatState != null && CombatManager.Instance.IsInProgress)
        {
            runningAction = RunManager.Instance.ActionExecutor?.CurrentlyRunningAction;
            try
            {
                readyAction = RunManager.Instance.ActionQueueSet?.GetReadyAction();
            }
            catch (InvalidOperationException)
            {
                actionQueueHasExecutingAction = true;
            }
        }

        var actionsSettled = !actionQueueHasExecutingAction && runningAction == null && readyAction == null;
        var localTurnReady = me != null && IsLocalCombatTurnReady(me, room);
        var playerActionPhase = IsPlayerActionPhase(combatState, me);
        var snapshotStable = false;
        string reason;

        if (modal != null)
        {
            reason = "modal_open";
        }
        else if (combatState == null || room == null)
        {
            ResetCombatActionReadiness();
            reason = "combat_screen_unavailable";
        }
        else if (!CombatManager.Instance.IsInProgress)
        {
            ResetCombatActionReadiness();
            reason = "combat_not_in_progress";
        }
        else if (CombatManager.Instance.IsOverOrEnding)
        {
            ResetCombatActionReadiness();
            reason = "combat_over_or_ending";
        }
        else if (CombatManager.Instance.IsPaused)
        {
            ResetCombatActionReadiness();
            reason = "combat_paused";
        }
        else if (CombatManager.Instance.PlayerActionsDisabled)
        {
            ResetCombatActionReadiness();
            reason = "player_actions_disabled";
        }
        else if (room.Mode != CombatRoomMode.ActiveCombat)
        {
            ResetCombatActionReadiness();
            reason = "combat_room_not_active";
        }
        else if (hand == null)
        {
            ResetCombatActionReadiness();
            reason = "hand_unavailable";
        }
        else if (hand.InCardPlay)
        {
            ResetCombatActionReadiness();
            reason = "hand_in_card_play";
        }
        else if (hand.IsInCardSelection)
        {
            ResetCombatActionReadiness();
            reason = "hand_in_card_selection";
        }
        else if (hand.CurrentMode != MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Mode.Play)
        {
            ResetCombatActionReadiness();
            reason = "hand_mode_not_play";
        }
        else if (me == null || !me.Creature.IsAlive)
        {
            ResetCombatActionReadiness();
            reason = "local_player_dead";
        }
        else if (!localTurnReady)
        {
            ResetCombatActionReadiness();
            reason = "local_turn_not_ready";
        }
        else if (runningAction != null)
        {
            reason = "game_action_running";
        }
        else if (readyAction != null)
        {
            reason = "game_action_queued";
        }
        else if (!actionsSettled)
        {
            reason = "action_queue_unsettled";
        }
        else if (!playerActionPhase)
        {
            ResetCombatActionReadiness();
            reason = "not_player_action_phase";
        }
        else if (!IsCombatActionSnapshotStable(combatState, me!))
        {
            reason = "snapshot_stabilizing";
        }
        else
        {
            snapshotStable = true;
            reason = "ready";
        }

        return new CombatActionGate
        {
            Source = combatState,
            Usable = reason == "ready",
            Reason = reason,
            Me = me,
            Room = room,
            ModalOpen = modal != null,
            ModalType = modal?.GetType().FullName,
            ActionsSettled = actionsSettled,
            RunningActionType = runningAction?.GetType().FullName,
            ReadyActionType = readyAction?.GetType().FullName,
            HandInCardPlay = hand?.InCardPlay,
            HandInCardSelection = hand?.IsInCardSelection,
            HandMode = hand?.CurrentMode.ToString(),
            LocalTurnReady = localTurnReady,
            SnapshotStable = snapshotStable,
            PlayerActionPhase = playerActionPhase
        };
    }

    private static bool IsCombatActionSnapshotStable(CombatState combatState, Player me)
    {
        if (!GameActionService.AreGameActionsSettled())
        {
            ResetCombatActionReadiness();
            return false;
        }

        var signature = BuildCombatActionReadinessSignature(combatState, me);
        var now = DateTime.UtcNow;

        if (!string.Equals(signature, _lastCombatActionReadinessSignature, StringComparison.Ordinal))
        {
            _lastCombatActionReadinessSignature = signature;
            _lastCombatActionReadinessSinceUtc = now;
            return false;
        }

        return now - _lastCombatActionReadinessSinceUtc >= CombatActionSnapshotStableDelay;
    }

    

        private static CombatActionReadinessPayload BuildCombatActionReadinessPayload(CombatActionGate gate)
    {
        var me = gate.Me;

        return new CombatActionReadinessPayload
        {
            can_use_combat_actions = gate.Usable,
            reason = gate.Reason,
            actions_settled = gate.ActionsSettled,
            running_action_type = gate.RunningActionType,
            ready_action_type = gate.ReadyActionType,
            modal_open = gate.ModalOpen,
            modal_type = gate.ModalType,
            player_actions_disabled = CombatManager.Instance.PlayerActionsDisabled,
            is_paused = CombatManager.Instance.IsPaused,
            local_ready_to_end_turn = me != null && CombatManager.Instance.IsPlayerReadyToEndTurn(me),
            all_players_ready_to_end_turn = CombatManager.Instance.AllPlayersReadyToEndTurn(),
            ending_turn_phase_one = CombatManager.Instance.EndingPlayerTurnPhaseOne,
            ending_turn_phase_two = CombatManager.Instance.EndingPlayerTurnPhaseTwo,
            end_turn_kick = GameActionService.EndTurnKickDetail,
            combat_in_progress = CombatManager.Instance.IsInProgress,
            combat_over_or_ending = CombatManager.Instance.IsOverOrEnding,
            combat_room_mode = gate.Room?.Mode.ToString(),
            hand_in_card_play = gate.HandInCardPlay,
            hand_in_card_selection = gate.HandInCardSelection,
            hand_mode = gate.HandMode,
            local_turn_ready = gate.LocalTurnReady,
            snapshot_stable = gate.SnapshotStable,
            player_action_phase = gate.PlayerActionPhase
        };
    }

    private static string BuildCombatActionReadinessSignature(CombatState combatState, Player me)
    {
        var playerCombatState = me.PlayerCombatState;
        var handCards = playerCombatState?.Hand.Cards.ToList() ?? new List<CardModel>();
        var handSignature = string.Join(
            ",",
            handCards.Select(card => $"{card.Id.Entry}:{card.Pile?.Type.ToString() ?? "Unknown"}"));

        return string.Join(
            "|",
            combatState.RoundNumber,
            playerCombatState?.TurnNumber ?? 0,
            playerCombatState?.Energy ?? 0,
            playerCombatState?.Stars ?? 0,
            handCards.Count,
            handSignature);
    }

    private static void ResetCombatActionReadiness()
    {
        _lastCombatActionReadinessSignature = null;
        _lastCombatActionReadinessSinceUtc = DateTime.MinValue;
    }

    private static bool IsLocalCombatTurnReady(Player me, NCombatRoom? combatRoom)
    {
        var playerCombatState = me.PlayerCombatState;
        if (playerCombatState == null)
        {
            return false;
        }

        return CombatTurnReadinessPolicy.IsLocallyReady(
            playerCombatState.TurnNumber,
            playerCombatState.Hand.Cards.Count,
            GameActionService.CardsPlayedThisTurn,
            IsEndTurnButtonReady(GetEndTurnButton(combatRoom)));
    }

    private static CombatPayload? BuildCombatPayload(CombatState? combatState, CombatActionGate combatActionGate)
    {
        var me = GetLocalPlayer(combatState);
        if (combatState == null || me?.PlayerCombatState == null)
        {
            return null;
        }

        var hand = me.PlayerCombatState.Hand.Cards.ToList();
        var enemies = combatState.Enemies.ToList();
        var orbQueue = me.PlayerCombatState.OrbQueue;
        var orbs = orbQueue.Orbs.ToList();
        var connectedPlayerIds = GetConnectedPlayerIds(combatState.RunState as RunState);
        GameActionService.SyncCardPlayCounters(combatState.RoundNumber);

        var playerPayload = new CombatPlayerPayload
        {
            current_hp = me.Creature.CurrentHp,
            max_hp = me.Creature.MaxHp,
            block = me.Creature.Block,
            energy = me.PlayerCombatState.Energy,
            stars = me.PlayerCombatState.Stars,
            focus = me.Creature.GetPowerAmount<FocusPower>(),
            powers = BuildCreaturePowerPayloads(me.Creature),
            base_orb_slots = me.BaseOrbSlotCount,
            orb_capacity = orbQueue.Capacity,
            empty_orb_slots = Math.Max(0, orbQueue.Capacity - orbs.Count),
            orbs = orbs.Select((orb, index) => BuildCombatOrbPayload(orb, index)).ToArray(),
            pets = me.PlayerCombatState.Pets.Select(BuildCombatPetPayload).ToArray(),
            pet_missing = PlayerSpawnsPets(me) && me.PlayerCombatState.Pets.Count == 0,
            cards_played_this_turn = GameActionService.CardsPlayedThisTurn,
            attacks_played_this_turn = GameActionService.AttacksPlayedThisTurn,
            skills_played_this_turn = GameActionService.SkillsPlayedThisTurn
        };
        var enemyPayloads = enemies.Select((enemy, index) => BuildEnemyPayload(enemy, index)).ToArray();
        var lethalRisks = BuildCombatLethalRiskPayloads(playerPayload, enemyPayloads);

        return new CombatPayload
        {
            action_readiness = BuildCombatActionReadinessPayload(combatActionGate),
            player = playerPayload,
            players = GetOrderedCombatPlayers(combatState)
                .Select(player => BuildCombatPlayerSummaryPayload(player, combatState, connectedPlayerIds, me.NetId))
                .ToArray(),
            hand = hand.Select((card, index) => BuildHandCardPayload(combatState, card, index)).ToArray(),
            enemies = enemyPayloads,
            end_turn_will_kill_player = lethalRisks.Any(risk => risk.will_kill_player),
            lethal_risks = lethalRisks
        };
    }

    private static CombatLethalRiskPayload[] BuildCombatLethalRiskPayloads(
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

        return risks.ToArray();
    }

    private static bool IsSandpitPower(CombatPowerPayload power)
    {
        return string.Equals(power.power_id, "SANDPIT_POWER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(power.name, "Sandpit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(power.name, "沙坑", StringComparison.OrdinalIgnoreCase);
    }

    private static CombatHandCardPayload BuildHandCardPayload(CombatState combatState, CardModel card, int index)
    {
        var canPlay = card.CanPlay(out var reason, out var preventer);
        var targetSupported = IsCardTargetSupported(card);
        var targetIndexSpace = GetCardTargetIndexSpace(card);
        var validTargetIndices = GetCardTargetIndices(combatState, card);
        var resolvedRulesText = GetResolvedCardRulesText(card);
        var dynamicValues = BuildCardDynamicValuePayloads(card);

        return new CombatHandCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            target_type = card.TargetType.ToString(),
            requires_target = CardRequiresTarget(card),
            target_index_space = targetIndexSpace,
            valid_target_indices = validTargetIndices,
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card),
            resolved_rules_text = resolvedRulesText,
            dynamic_values = dynamicValues,
            playable = targetSupported && canPlay,
            can_play_result = canPlay,
            unplayable_reason = targetSupported
                ? GetUnplayableReasonCode(reason)
                : "unsupported_target_type",
            unplayable_reason_raw = reason == UnplayableReason.None ? null : reason.ToString(),
            unplayable_preventer_id = GetModelIdEntry(preventer),
            unplayable_preventer_type = preventer?.GetType().FullName
        };
    }

    private static CombatEnemyPayload BuildEnemyPayload(Creature enemy, int index)
    {
        var moveId = enemy.Monster?.NextMove?.Id;
        var intents = BuildEnemyIntentPayloads(enemy);

        return new CombatEnemyPayload
        {
            index = index,
            enemy_id = enemy.ModelId.Entry,
            name = enemy.Name,
            current_hp = enemy.CurrentHp,
            max_hp = enemy.MaxHp,
            // Unscaled base roll: multiplayer scaling is applied before the live MaxHp is set, so this
            // value shares its dimension with the monsters.min_hp/max_hp metadata while max_hp is scaled.
            base_max_hp = enemy.MonsterMaxHpBeforeModification,
            block = enemy.Block,
            is_alive = enemy.IsAlive,
            is_hittable = enemy.IsHittable,
            powers = BuildCreaturePowerPayloads(enemy),
            intent = moveId,
            move_id = moveId,
            intents = intents
        };
    }

    private static CombatPowerPayload[] BuildCreaturePowerPayloads(Creature creature)
    {
        // Every member read here is public on Creature and PowerModel, so the compiler checks it. This
        // used to reach each one by name through reflection, which a rename would have emptied silently.
        var result = new List<CombatPowerPayload>();
        var index = 0;

        foreach (var power in creature.Powers)
        {
            if (power == null)
            {
                continue;
            }

            var idEntry = SafeReadString(() => power.Id.Entry);
            var title = SafeReadString(() => power.Title.GetFormattedText());
            var amount = SafeReadNullableInt(() => power.Amount);
            var isDebuff = SafeReadBool(() => power.TypeForCurrentAmount == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff);

            result.Add(new CombatPowerPayload
            {
                index = index,
                power_id = string.IsNullOrWhiteSpace(idEntry) ? "unknown_power" : idEntry,
                name = string.IsNullOrWhiteSpace(title) ? idEntry : title,
                amount = amount,
                is_debuff = isDebuff
            });
            index += 1;
        }

        return result.ToArray();
    }

    // A pet (Necrobinder's Osty, a Byrdpip bird) is a creature on the player's own side, so it never
    // appears in the enemies list and the raw player payload used to say nothing about it. Report pets
    // from the player's combat state, and report that one is missing separately: the game removes a dead
    // pet from that list, so an empty list alone cannot tell "it died" apart from "this character never
    // had one". SpawnsPets comes from the relics that create the pet and is stable across its death.
    private static bool PlayerSpawnsPets(Player player)
    {
        return player.Relics.Any(relic => relic.SpawnsPets);
    }

    private static CombatPetPayload BuildCombatPetPayload(Creature pet, int index)
    {
        return new CombatPetPayload
        {
            index = index,
            pet_id = pet.Monster?.Id.Entry ?? string.Empty,
            name = pet.Name,
            current_hp = pet.CurrentHp,
            max_hp = pet.MaxHp,
            block = pet.Block,
            powers = BuildCreaturePowerPayloads(pet)
        };
    }

    private static CombatEnemyIntentPayload[] BuildEnemyIntentPayloads(Creature enemy)
    {
        var nextMove = enemy.Monster?.NextMove;
        if (nextMove == null)
        {
            return Array.Empty<CombatEnemyIntentPayload>();
        }

        var targets = enemy.CombatState?.Players
            .Select(player => player.Creature)
            .ToArray() ?? Array.Empty<Creature>();

        return nextMove.Intents
            .Select((intent, index) => BuildEnemyIntentPayload(intent, enemy, targets, index))
            .ToArray();
    }

    private static CombatEnemyIntentPayload BuildEnemyIntentPayload(
        AbstractIntent intent,
        Creature owner,
        Creature[] targets,
        int index)
    {
        int? damage = null;
        int? hits = null;
        int? totalDamage = null;
        int? statusCardCount = null;

        if (intent is AttackIntent attackIntent)
        {
            damage = SafeReadNullableInt(() => attackIntent.GetSingleDamage(targets, owner));
            hits = SafeReadNullableInt(() => Math.Max(1, attackIntent.Repeats));
            totalDamage = SafeReadNullableInt(() => attackIntent.GetTotalDamage(targets, owner));
        }

        if (intent is StatusIntent statusIntent)
        {
            statusCardCount = SafeReadNullableInt(() => statusIntent.CardCount);
        }

        var label = SafeReadString(() => intent.GetIntentLabel(targets, owner).GetFormattedText(), string.Empty);

        return new CombatEnemyIntentPayload
        {
            index = index,
            intent_type = intent.IntentType.ToString(),
            label = string.IsNullOrWhiteSpace(label) ? null : label,
            damage = damage,
            hits = hits,
            total_damage = totalDamage,
            status_card_count = statusCardCount
        };
    }

    private static CombatOrbPayload BuildCombatOrbPayload(OrbModel orb, int slotIndex)
    {
        return new CombatOrbPayload
        {
            slot_index = slotIndex,
            orb_id = orb.Id.Entry,
            name = orb.Title.GetFormattedText(),
            passive_value = orb.PassiveVal,
            evoke_value = orb.EvokeVal,
            is_front = slotIndex == 0
        };
    }

    private static CombatPlayerSummaryPayload BuildCombatPlayerSummaryPayload(
        Player player,
        CombatState combatState,
        IReadOnlyCollection<ulong> connectedPlayerIds,
        ulong localPlayerId)
    {
        return new CombatPlayerSummaryPayload
        {
            player_id = NetIdToString(player.NetId),
            slot_index = combatState.RunState is RunState runState ? runState.GetPlayerSlotIndex(player) : 0,
            is_local = player.NetId == localPlayerId,
            is_connected = connectedPlayerIds.Contains(player.NetId),
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            block = player.Creature.Block,
            energy = player.PlayerCombatState?.Energy ?? 0,
            stars = player.PlayerCombatState?.Stars ?? 0,
            focus = player.Creature.GetPowerAmount<FocusPower>(),
            is_alive = player.Creature.IsAlive
        };
    }

    public static NEndTurnButton? GetEndTurnButton(NCombatRoom? combatRoom)
    {
        if (combatRoom == null || !GodotObject.IsInstanceValid(combatRoom))
        {
            return null;
        }

        return combatRoom.Ui?.EndTurnButton
            ?? FindDescendants<NEndTurnButton>(combatRoom).FirstOrDefault(GodotObject.IsInstanceValid);
    }

    public static bool IsEndTurnButtonReady(NEndTurnButton? button)
    {
        if (button == null || !GodotObject.IsInstanceValid(button) || !button.IsEnabled)
        {
            return false;
        }

        var property = ReflectedGameMembers.Property(typeof(NEndTurnButton), "CanTurnBeEnded");
        return property?.GetValue(button) is not bool canTurnBeEnded || canTurnBeEnded;
    }

    public static bool TryGetCombatHandSelection(IScreenContext? currentScreen, out NPlayerHand? hand)
    {
        hand = null;

        if (currentScreen is not NCombatRoom combatRoom)
        {
            return false;
        }

        hand = combatRoom.Ui?.Hand;
        return hand != null &&
            GodotObject.IsInstanceValid(hand) &&
            hand.IsInCardSelection &&
            hand.CurrentMode is NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect;
    }

    private static CardSelectorPrefs? TryGetCombatHandSelectionPrefs(NPlayerHand hand)
    {
        var field = ReflectedGameMembers.Field(typeof(NPlayerHand), "_prefs");
        if (field?.GetValue(hand) is CardSelectorPrefs prefs)
        {
            return prefs;
        }

        return null;
    }

    public static bool TryGetCombatHandSelectionMetadata(
        IScreenContext? currentScreen,
        out NPlayerHand? hand,
        out CombatHandSelectionMetadata metadata)
    {
        metadata = default;
        if (!TryGetCombatHandSelection(currentScreen, out hand) || hand == null)
        {
            return false;
        }

        var prefs = TryGetCombatHandSelectionPrefs(hand);
        var requiresConfirmation = prefs?.RequireManualConfirmation ?? false;
        var canConfirm = requiresConfirmation &&
            TryGetCombatHandConfirmButton(hand, out var confirmButton) &&
            confirmButton!.Visible &&
            confirmButton.IsEnabled;

        metadata = new CombatHandSelectionMetadata(
            prefs?.MinSelect ?? 1,
            prefs?.MaxSelect ?? 1,
            GetCombatHandSelectedCount(hand),
            requiresConfirmation,
            canConfirm);
        return true;
    }

    private static int GetCombatHandSelectedCount(NPlayerHand hand)
    {
        var field = ReflectedGameMembers.Field(typeof(NPlayerHand), "_selectedCards");
        return field?.GetValue(hand) is System.Collections.ICollection collection ? collection.Count : 0;
    }

    private static bool TryGetCombatHandConfirmButton(NPlayerHand hand, out NConfirmButton? confirmButton)
    {
        confirmButton = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton")
            ?? hand.GetNodeOrNull<NConfirmButton>("SelectModeConfirmButton");
        return confirmButton != null && GodotObject.IsInstanceValid(confirmButton);
    }

    private static IReadOnlyList<Player> GetOrderedCombatPlayers(CombatState combatState)
    {
        return combatState.Players
            .OrderBy(player => combatState.RunState is RunState runState ? runState.GetPlayerSlotIndex(player) : 0)
            .ToArray();
    }

    public static Creature? ResolveEnemyTarget(CombatState combatState, int targetIndex)
    {
        var enemies = combatState.Enemies.ToList();
        if (targetIndex < 0 || targetIndex >= enemies.Count)
        {
            return null;
        }

        var enemy = enemies[targetIndex];
        return enemy.IsAlive && enemy.IsHittable ? enemy : null;
    }

    public static Creature? ResolvePlayerTarget(CombatState combatState, int targetIndex)
    {
        var players = GetOrderedCombatPlayers(combatState);
        if (targetIndex < 0 || targetIndex >= players.Count)
        {
            return null;
        }

        var player = players[targetIndex];
        return player.Creature.IsAlive ? player.Creature : null;
    }

    public static Player? ResolveRunPlayerTarget(RunState? runState, int targetIndex)
    {
        if (runState == null || targetIndex < 0)
        {
            return null;
        }

        var players = runState.Players
            .OrderBy(runState.GetPlayerSlotIndex)
            .ToArray();
        if (targetIndex >= players.Length)
        {
            return null;
        }

        var player = players[targetIndex];
        return player.Creature.IsAlive ? player : null;
    }

    public static bool CardRequiresTarget(CardModel card)
    {
        return RequiresIndexedCardTarget(card.TargetType);
    }

    public static bool IsCardTargetSupported(CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.None => true,
            TargetType.Self => true,
            TargetType.AnyEnemy => true,
            TargetType.AllEnemies => true,
            TargetType.RandomEnemy => true,
            TargetType.AnyAlly => true,
            TargetType.AllAllies => true,
            _ => false
        };
    }

    private static bool RequiresIndexedCardTarget(TargetType targetType)
    {
        return targetType == TargetType.AnyEnemy || targetType == TargetType.AnyAlly;
    }

    private static string? GetCardTargetIndexSpace(CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => "enemies",
            TargetType.AnyAlly => "players",
            _ => null
        };
    }

    private static int[] GetCardTargetIndices(CombatState combatState, CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState),
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, card.Owner, allowSelf: false),
            _ => Array.Empty<int>()
        };
    }

    private static string? GetPotionTargetIndexSpace(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => "enemies",
            TargetType.AnyPlayer when PotionRequiresExplicitPlayerSelection(combatState, potion) => "players",
            TargetType.AnyAlly => "players",
            _ => null
        };
    }

    private static int[] GetPotionTargetIndices(CombatState? combatState, PotionModel potion)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return potion.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState),
            TargetType.AnyPlayer when PotionRequiresExplicitPlayerSelection(combatState, potion) => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: true),
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false),
            _ => Array.Empty<int>()
        };
    }

    public static int[] GetTargetableEnemyIndices(CombatState? combatState)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return combatState.Enemies
            .Select((enemy, index) => new { enemy, index })
            .Where(entry => entry.enemy.IsAlive && entry.enemy.IsHittable)
            .Select(entry => entry.index)
            .ToArray();
    }

    public static int[] GetTargetablePlayerIndices(CombatState? combatState, Player owner, bool allowSelf)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return GetOrderedCombatPlayers(combatState)
            .Select((player, index) => new { player, index })
            .Where(entry => entry.player.Creature.IsAlive)
            .Where(entry => allowSelf || entry.player.NetId != owner.NetId)
            .Select(entry => entry.index)
            .ToArray();
    }

    public static string? GetUnplayableReasonCode(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return GetUnplayableReasonCode(reason);
    }

    public static string? GetUnplayableReasonCode(UnplayableReason reason)
    {
        if (reason == UnplayableReason.None)
        {
            return null;
        }

        if (reason.HasFlag(UnplayableReason.EnergyCostTooHigh))
        {
            return "not_enough_energy";
        }

        if (reason.HasFlag(UnplayableReason.StarCostTooHigh))
        {
            return "not_enough_stars";
        }

        if (reason.HasFlag(UnplayableReason.NoLivingAllies))
        {
            return "no_living_allies";
        }

        if (reason.HasFlag(UnplayableReason.BlockedByHook))
        {
            return "blocked_by_hook";
        }

        if (reason.HasFlag(UnplayableReason.HasUnplayableKeyword) || reason.HasFlag(UnplayableReason.BlockedByCardLogic))
        {
            return "unplayable";
        }

        return reason.ToString();
    }
}
