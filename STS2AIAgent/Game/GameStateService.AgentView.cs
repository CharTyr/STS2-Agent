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
/// The compact <c>agent_view</c>: what MCP <c>get_game_state</c> returns by default.
/// </summary>
/// <remarks>
/// This is a rewrite of the raw <c>/state</c> payload, not a second reading of the game. Every
/// method here takes a payload the raw builders in <see cref="GameStateService"/> already
/// produced and renames, folds or drops fields for a model's context window -- so it shares
/// nothing with those builders except their output, which is why it can sit in its own file.
///
/// It was extracted from <c>GameStateService.cs</c> on 2026-09-17 with the members moved byte
/// for byte; the class is the same <c>partial</c> class, so no call site changed. The point was
/// the file, not the code: <c>GameStateService.cs</c> held 27% of the mod, and a file nobody can
/// read is where the next regression hides.
///
/// Keep the concerns apart. A raw payload field belongs to the <c>Build*</c> builders in
/// <c>GameStateService.cs</c>; its compact projection belongs to the matching
/// <c>BuildAgent*</c> method here, and to the rename table in <c>docs/api.md</c> that the
/// <c>api-facts</c> gate checks against these method bodies.
/// </remarks>
internal static partial class GameStateService
{
    private const int AgentViewVersion = 10;

    private static string GetPreferredCardRulesText(string rulesText, string? resolvedRulesText)
    {
        return string.IsNullOrWhiteSpace(resolvedRulesText) ? rulesText : resolvedRulesText;
    }

    private static object BuildAgentViewPayload(
        string screen,
        SessionPayload session,
        int nativeProfileId,
        string runId,
        int? turn,
        string[] availableActions,
        CombatState? combatState,
        RunState? runState,
        CombatPayload? combat,
        RunPayload? run,
        MultiplayerPayload? multiplayer,
        MultiplayerLobbyPayload? multiplayerLobby,
        MapPayload? map,
        SelectionPayload? selection,
        CharacterSelectPayload? characterSelect,
        TimelinePayload? timeline,
        ChestPayload? chest,
        EventPayload? eventPayload,
        CrystalSpherePayload? crystalSphere,
        ShopPayload? shop,
        RestPayload? rest,
        RewardPayload? reward,
        BundlePayload[]? bundles,
        object? capstone,
        UnlockPayload? unlock,
        ModalPayload? modal,
        GameOverPayload? gameOver)
    {
        var glossaryTerms = new HashSet<string>(StringComparer.Ordinal);

        return new
        {
            version = AgentViewVersion,
            screen,
            native_profile_id = nativeProfileId,
            profiles = new[]
            {
                new { id = 1, current = nativeProfileId == 1 },
                new { id = 2, current = nativeProfileId == 2 },
                new { id = 3, current = nativeProfileId == 3 }
            },
            run_id = runId,
            session,
            turn,
            actions = availableActions,
            available_actions = availableActions,
            combat = BuildAgentCombatPayload(combatState, combat, glossaryTerms),
            run = BuildAgentRunPayload(combatState, runState, run, glossaryTerms),
            multiplayer = BuildAgentMultiplayerPayload(multiplayer),
            multiplayer_lobby = BuildAgentMultiplayerLobbyPayload(multiplayerLobby),
            map = BuildAgentMapPayload(map),
            selection = BuildAgentSelectionPayload(selection, glossaryTerms),
            character_select = BuildAgentCharacterSelectPayload(characterSelect),
            timeline = BuildAgentTimelinePayload(timeline),
            chest = BuildAgentChestPayload(chest),
            @event = BuildAgentEventPayload(eventPayload),
            crystal_sphere = crystalSphere,
            shop = BuildAgentShopPayload(shop, glossaryTerms),
            rest = BuildAgentRestPayload(rest),
            reward = BuildAgentRewardPayload(reward, glossaryTerms),
            bundles = BuildAgentBundlePayload(bundles, glossaryTerms),
            capstone,
            unlock = unlock == null
                ? null
                : new
                {
                    unlock_type = unlock.unlock_type,
                    items = unlock.items,
                    can_confirm = unlock.can_confirm
                },
            modal = BuildAgentModalPayload(modal),
            game_over = BuildAgentGameOverPayload(gameOver),
            glossary = BuildAgentGlossary(glossaryTerms)
        };
    }

    private static object? BuildAgentMultiplayerPayload(MultiplayerPayload? multiplayer)
    {
        if (multiplayer == null)
        {
            return null;
        }

        return new
        {
            is_multiplayer = multiplayer.is_multiplayer,
            net_game_type = multiplayer.net_game_type,
            local_player_id = multiplayer.local_player_id,
            player_count = multiplayer.player_count,
            connected_player_ids = multiplayer.connected_player_ids
        };
    }

    private static object? BuildAgentMultiplayerLobbyPayload(MultiplayerLobbyPayload? lobby)
    {
        if (lobby == null)
        {
            return null;
        }

        return new
        {
            has_lobby = lobby.has_lobby,
            is_host = lobby.is_host,
            is_client = lobby.is_client,
            local_ready = lobby.local_ready,
            can_host = lobby.can_host,
            can_join = lobby.can_join,
            can_ready = lobby.can_ready,
            can_disconnect = lobby.can_disconnect,
            join_host = lobby.join_host,
            join_port = lobby.join_port,
            selected_character_id = lobby.selected_character_id,
            player_count = lobby.player_count,
            max_players = lobby.max_players,
            players = lobby.players.Select((player, index) => new
            {
                i = index,
                player_id = player.player_id,
                is_local = player.is_local,
                character = player.character_name ?? player.character_id,
                ready = player.is_ready
            }).ToArray(),
            characters = lobby.characters.Select(character => new
            {
                i = character.index,
                line = character.name,
                locked = character.is_locked,
                selected = character.is_selected
            }).ToArray()
        };
    }

    private static object? BuildAgentCombatPayload(
        CombatState? combatState,
        CombatPayload? combat,
        HashSet<string> glossaryTerms)
    {
        if (combat == null)
        {
            return null;
        }

        var liveHand = GetLocalPlayer(combatState)?.PlayerCombatState?.Hand.Cards.ToList()
            ?? new List<CardModel>();
        var playerCombatState = GetLocalPlayer(combatState)?.PlayerCombatState;

        return new
        {
            action_readiness = combat.action_readiness,
            player = new
            {
                hp = $"{combat.player.current_hp}/{combat.player.max_hp}",
                block = combat.player.block,
                energy = combat.player.energy,
                stars = combat.player.stars,
                focus = combat.player.focus,
                orbs = combat.player.orbs.Select(orb => FormatOrbLine(orb)).ToArray(),
                pets = combat.player.pets.Select(pet => FormatPetLine(pet)).ToArray(),
                pet_missing = combat.player.pet_missing,
                cards_played_this_turn = combat.player.cards_played_this_turn,
                attacks_played_this_turn = combat.player.attacks_played_this_turn,
                skills_played_this_turn = combat.player.skills_played_this_turn,
                powers = combat.player.powers.Select(power => FormatPowerLine(power)).ToArray()
            },
            players = combat.players.Select(other => new
            {
                player_id = other.player_id,
                slot_index = other.slot_index,
                is_local = other.is_local,
                is_connected = other.is_connected,
                character_id = other.character_id,
                character_name = other.character_name,
                current_hp = other.current_hp,
                max_hp = other.max_hp,
                block = other.block,
                energy = other.energy,
                stars = other.stars,
                focus = other.focus,
                is_alive = other.is_alive
            }).ToArray(),
            end_turn_will_kill_player = combat.end_turn_will_kill_player,
            lethal_risks = combat.lethal_risks.Select(risk => new
            {
                risk_id = risk.risk_id,
                source = risk.source,
                will_kill_player = risk.will_kill_player,
                reason = risk.reason,
                incoming_damage = risk.incoming_damage,
                damage_after_block = risk.damage_after_block,
                player_hp = risk.player_hp,
                player_block = risk.player_block,
                power_id = risk.power_id,
                power_amount = risk.power_amount
            }).ToArray(),
            hand = combat.hand.Select(card =>
                BuildAgentHandCardPayload(
                    card,
                    card.index >= 0 && card.index < liveHand.Count ? liveHand[card.index] : null,
                    glossaryTerms)).ToArray(),
            draw = BuildAgentCardStacks(PileCards(playerCombatState?.DrawPile), glossaryTerms),
            discard = BuildAgentCardStacks(PileCards(playerCombatState?.DiscardPile), glossaryTerms),
            exhaust = BuildAgentCardStacks(PileCards(playerCombatState?.ExhaustPile), glossaryTerms),
            draw_cards = BuildStructuredPileCards(PileCards(playerCombatState?.DrawPile)),
            discard_cards = BuildStructuredPileCards(PileCards(playerCombatState?.DiscardPile)),
            exhaust_cards = BuildStructuredPileCards(PileCards(playerCombatState?.ExhaustPile)),
            enemies = combat.enemies.Select(enemy => new
            {
                i = enemy.index,
                enemy_id = enemy.enemy_id,
                name = enemy.name,
                hp = $"{enemy.current_hp}/{enemy.max_hp}",
                // Unscaled base roll (same dimension as monsters.min_hp/max_hp metadata); hp above is scaled live.
                base_max_hp = enemy.base_max_hp,
                block = enemy.block,
                intent = enemy.intent,
                move_id = enemy.move_id,
                powers = enemy.powers.Select(power => FormatPowerLine(power)).ToArray(),
                intents = enemy.intents.Select(intent => new
                {
                    i = intent.index,
                    intent_type = intent.intent_type,
                    label = intent.label,
                    damage = intent.damage,
                    hits = intent.hits,
                    total_damage = intent.total_damage,
                    status_card_count = intent.status_card_count
                }).ToArray(),
                alive = enemy.is_alive,
                hittable = enemy.is_hittable
            }).ToArray()
        };
    }

    private static object? BuildAgentRunPayload(
        CombatState? combatState,
        RunState? runState,
        RunPayload? run,
        HashSet<string> glossaryTerms)
    {
        if (run == null)
        {
            return null;
        }

        var player = GetLocalPlayer(runState);
        var deckCards = player?.Deck.Cards.ToArray() ?? Array.Empty<CardModel>();
        var combatPlayer = GetLocalPlayer(combatState)?.PlayerCombatState;

        foreach (var effect in run.ascension_effects)
        {
            CollectGlossaryTerms(glossaryTerms, effect.name);
            CollectGlossaryTerms(glossaryTerms, effect.description);
        }

        return new
        {
            character = run.character_name,
            ascension = run.ascension,
            ascension_effects = run.ascension_effects,
            floor = run.floor,
            act_id = run.act_id,
            boss_id = run.boss_id,
            hp = $"{run.current_hp}/{run.max_hp}",
            gold = run.gold,
            max_energy = run.max_energy,
            base_orb_slots = run.base_orb_slots,
            deck = deckCards.Length > 0
                ? BuildAgentCardStacks(deckCards, glossaryTerms)
                : BuildAgentCardStacks(run.deck, glossaryTerms),
            relics = run.relics
                .Select(relic => relic.is_melted ? Loc.T("{0} (熔毁)", relic.name) : relic.name)
                .ToArray(),
            relic_ids = run.relics.Select(relic => relic.relic_id).ToArray(),
            players = run.players.Select(other => new
            {
                player_id = other.player_id,
                slot_index = other.slot_index,
                is_local = other.is_local,
                is_connected = other.is_connected,
                character_id = other.character_id,
                character_name = other.character_name,
                current_hp = other.current_hp,
                max_hp = other.max_hp,
                gold = other.gold,
                is_alive = other.is_alive
            }).ToArray(),
            potions = run.potions.Select(potion => new
            {
                i = potion.index,
                potion_id = potion.potion_id,
                line = FormatPotionLine(potion),
                usable = potion.can_use,
                discard = potion.can_discard,
                target = NormalizeTargetHint(potion.target_type),
                targets = potion.valid_target_indices
            }).ToArray(),
            piles = new
            {
                draw = BuildAgentCardStacks(PileCards(combatPlayer?.DrawPile), glossaryTerms),
                discard = BuildAgentCardStacks(PileCards(combatPlayer?.DiscardPile), glossaryTerms),
                exhaust = BuildAgentCardStacks(PileCards(combatPlayer?.ExhaustPile), glossaryTerms),
                draw_cards = BuildStructuredPileCards(PileCards(combatPlayer?.DrawPile)),
                discard_cards = BuildStructuredPileCards(PileCards(combatPlayer?.DiscardPile)),
                exhaust_cards = BuildStructuredPileCards(PileCards(combatPlayer?.ExhaustPile))
            }
        };
    }

    private static object? BuildAgentSelectionPayload(SelectionPayload? selection, HashSet<string> glossaryTerms)
    {
        if (selection == null)
        {
            return null;
        }

        CollectGlossaryTerms(glossaryTerms, selection.prompt);

        return new
        {
            kind = selection.kind,
            prompt = selection.prompt,
            min = selection.min_select,
            max = selection.max_select,
            selected = selection.selected_count,
            confirm = selection.can_confirm,
            cards = selection.cards.Select(card => BuildAgentChoiceCardPayload(card.index, card.card_id, card.name, card.upgraded, card.energy_cost, card.star_cost, card.costs_x, card.star_costs_x, GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text), glossaryTerms, card.selected)).ToArray()
        };
    }

    private static object? BuildAgentRewardPayload(RewardPayload? reward, HashSet<string> glossaryTerms)
    {
        if (reward == null)
        {
            return null;
        }

        foreach (var option in reward.rewards)
        {
            CollectGlossaryTerms(glossaryTerms, option.description);
        }

        return new
        {
            pending_card_choice = reward.pending_card_choice,
            can_proceed = reward.can_proceed,
            rewards = reward.rewards.Select(option => new
            {
                i = option.index,
                line = $"{option.reward_type}: {option.description}",
                claimable = option.claimable
            }).ToArray(),
            cards = reward.card_options.Select(card => BuildAgentChoiceCardPayload(card.index, card.card_id, card.name, card.upgraded, null, null, false, false, GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text), glossaryTerms)).ToArray(),
            alternatives = reward.alternatives.Select(option => new
            {
                i = option.index,
                line = option.label
            }).ToArray()
        };
    }

    private static object? BuildAgentBundlePayload(BundlePayload[]? bundles, HashSet<string> glossaryTerms)
    {
        if (bundles == null || bundles.Length == 0)
        {
            return null;
        }

        return bundles.Select(bundle => new
        {
            i = bundle.index,
            cards = bundle.cards.Select(card =>
                BuildAgentChoiceCardPayload(
                    card.index, card.card_id, card.name, card.upgraded,
                    card.energy_cost, null, false, false,
                    GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text),
                    glossaryTerms)).ToArray()
        }).ToArray();
    }

    private static object? BuildAgentEventPayload(EventPayload? eventPayload)
    {
        if (eventPayload == null)
        {
            return null;
        }

        return new
        {
            id = eventPayload.event_id,
            title = eventPayload.title,
            finished = eventPayload.is_finished,
            options = eventPayload.options.Select(option => new
            {
                i = option.index,
                line = FormatEventOptionLine(option),
                locked = option.is_locked,
                proceed = option.is_proceed,
                kill = option.will_kill_player
            }).ToArray()
        };
    }

    private static object? BuildAgentShopPayload(ShopPayload? shop, HashSet<string> glossaryTerms)
    {
        if (shop == null)
        {
            return null;
        }

        return new
        {
            open = shop.is_open,
            can_open = shop.can_open,
            can_close = shop.can_close,
            cards = shop.cards.Select(card =>
                BuildAgentPricedCardPayload(
                    card.index,
                    card.card_id,
                    card.name,
                    card.upgraded,
                    card.energy_cost,
                    card.star_cost,
                    card.costs_x,
                    card.star_costs_x,
                    GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text),
                    card.price,
                    card.enough_gold,
                    glossaryTerms)).ToArray(),
            relics = shop.relics.Select(relic => new
            {
                i = relic.index,
                line = $"{relic.name} [{relic.rarity}] | {relic.price}g",
                affordable = relic.enough_gold,
                stocked = relic.is_stocked
            }).ToArray(),
            potions = shop.potions.Select(potion => new
            {
                i = potion.index,
                line = FormatShopPotionLine(potion),
                affordable = potion.enough_gold,
                stocked = potion.is_stocked
            }).ToArray(),
            remove = shop.card_removal == null
                ? null
                : new
                {
                    price = shop.card_removal.price,
                    affordable = shop.card_removal.enough_gold,
                    available = shop.card_removal.available,
                    used = shop.card_removal.used
                }
        };
    }

    private static object? BuildAgentRestPayload(RestPayload? rest)
    {
        if (rest == null)
        {
            return null;
        }

        return new
        {
            options = rest.options.Select(option => new
            {
                i = option.index,
                line = (string.IsNullOrWhiteSpace(option.description)
                    ? option.title
                    : $"{option.title}: {option.description}") +
                    (option.requires_target
                        ? $" [target {option.target_index_space}: {string.Join(",", option.valid_target_indices)}]"
                        : string.Empty),
                requires_target = option.requires_target,
                target_index_space = option.target_index_space,
                valid_target_indices = option.valid_target_indices,
                enabled = option.is_enabled
            }).ToArray()
        };
    }

    private static object? BuildAgentMapPayload(MapPayload? map)
    {
        if (map == null)
        {
            return null;
        }

        return new
        {
            current = map.current_node == null ? null : $"{map.current_node.row},{map.current_node.col}",
            local_vote = map.local_vote == null ? null : $"{map.local_vote.row},{map.local_vote.col}",
            votes = map.player_votes
                .Where(vote => vote.coord != null)
                .Select(vote => new
                {
                    player_id = vote.player_id,
                    local = vote.is_local,
                    coord = $"{vote.coord!.row},{vote.coord.col}"
                }).ToArray(),
            options = map.available_nodes.Select(node => new
            {
                i = node.index,
                line = $"{node.node_type} ({node.row},{node.col})" +
                    (node.has_local_vote
                        ? " [local vote]"
                        : node.vote_count > 0
                            ? $" [votes:{node.vote_count}]"
                            : string.Empty)
            }).ToArray()
        };
    }

    private static object? BuildAgentCharacterSelectPayload(CharacterSelectPayload? characterSelect)
    {
        if (characterSelect == null)
        {
            return null;
        }

        return new
        {
            selected = characterSelect.selected_character_id,
            embark = characterSelect.can_embark,
            ascension = characterSelect.ascension,
            characters = characterSelect.characters.Select(character => new
            {
                i = character.index,
                line = character.is_random ? Loc.T("{0} (随机)", character.name) : character.name,
                locked = character.is_locked,
                selected = character.is_selected
            }).ToArray()
        };
    }

    private static object? BuildAgentTimelinePayload(TimelinePayload? timeline)
    {
        if (timeline == null)
        {
            return null;
        }

        return new
        {
            back = timeline.back_enabled,
            confirm = timeline.can_confirm_overlay,
            tutorial = timeline.tutorial_open,
            slots = timeline.slots.Select(slot => new
            {
                i = slot.index,
                line = $"{slot.title} [{slot.state}]",
                actionable = slot.is_actionable
            }).ToArray()
        };
    }

    private static object? BuildAgentChestPayload(ChestPayload? chest)
    {
        if (chest == null)
        {
            return null;
        }

        return new
        {
            opened = chest.is_opened,
            claimed = chest.has_relic_been_claimed,
            relics = chest.relic_options.Select(relic => new
            {
                i = relic.index,
                relic_id = relic.relic_id,
                line = $"{relic.name} [{relic.rarity}]"
            }).ToArray()
        };
    }

    private static object? BuildAgentModalPayload(ModalPayload? modal)
    {
        if (modal == null)
        {
            return null;
        }

        return new
        {
            type = modal.type_name,
            underlying_screen = modal.underlying_screen,
            confirm = modal.can_confirm,
            dismiss = modal.can_dismiss,
            confirm_label = modal.confirm_label,
            dismiss_label = modal.dismiss_label
        };
    }

    private static object? BuildAgentGameOverPayload(GameOverPayload? gameOver)
    {
        if (gameOver == null)
        {
            return null;
        }

        return new
        {
            victory = gameOver.is_victory,
            floor = gameOver.floor,
            character = gameOver.character_id,
            phase = gameOver.phase,
            can_continue = gameOver.can_continue,
            can_return = gameOver.can_return_to_main_menu,
            waiting_for_other_players = gameOver.waiting_for_other_players,
            save_status = gameOver.save_status,
            save_verified = gameOver.save_verified,
            save_error = gameOver.save_error
        };
    }

    private static object BuildAgentHandCardPayload(
        CombatHandCardPayload card,
        CardModel? liveCard,
        HashSet<string> glossaryTerms)
    {
        var displayRulesText = GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text);
        var mods = GetCardModifierTags(liveCard);
        var keywords = GetGlossaryMatches(displayRulesText, mods);
        CollectGlossaryTerms(glossaryTerms, displayRulesText, mods);

        return new
        {
            i = card.index,
            card_id = card.card_id,
            line = FormatCardLine(card.name, card.upgraded, 1, card.energy_cost, card.star_cost, card.costs_x, card.star_costs_x, displayRulesText),
            playable = card.playable,
            can_play_result = card.can_play_result,
            target = card.requires_target ? NormalizeTargetHint(card.target_index_space ?? card.target_type) : null,
            targets = card.requires_target ? card.valid_target_indices : Array.Empty<int>(),
            why = card.playable ? null : card.unplayable_reason,
            unplayable_reason = card.unplayable_reason,
            unplayable_reason_raw = card.unplayable_reason_raw,
            unplayable_preventer_id = card.unplayable_preventer_id,
            unplayable_preventer_type = card.unplayable_preventer_type,
            keywords = TranslateKeywords(keywords),
            mods
        };
    }

    private static object BuildAgentChoiceCardPayload(
        int index,
        string cardId,
        string name,
        bool upgraded,
        int? energyCost,
        int? starCost,
        bool costsX,
        bool starCostsX,
        string rulesText,
        HashSet<string> glossaryTerms,
        bool selected = false)
    {
        var keywords = GetGlossaryMatches(rulesText);
        CollectGlossaryTerms(glossaryTerms, rulesText);

        return new
        {
            i = index,
            card_id = cardId,
            line = FormatCardLine(name, upgraded, 1, energyCost, starCost, costsX, starCostsX, rulesText),
            selected,
            keywords = TranslateKeywords(keywords),
            mods = Array.Empty<string>()
        };
    }

    private static object BuildAgentPricedCardPayload(
        int index,
        string cardId,
        string name,
        bool upgraded,
        int energyCost,
        int starCost,
        bool costsX,
        bool starCostsX,
        string rulesText,
        int price,
        bool enoughGold,
        HashSet<string> glossaryTerms)
    {
        var keywords = GetGlossaryMatches(rulesText);
        CollectGlossaryTerms(glossaryTerms, rulesText);

        return new
        {
            i = index,
            card_id = cardId,
            line = $"{FormatCardLine(name, upgraded, 1, energyCost, starCost, costsX, starCostsX, rulesText)} | {price}g",
            affordable = enoughGold,
            keywords = TranslateKeywords(keywords),
            mods = Array.Empty<string>()
        };
    }

    private static object[] BuildAgentCardStacks(IEnumerable<CardModel> cards, HashSet<string> glossaryTerms)
    {
        var descriptors = cards
            .Select(card => BuildAgentCardDescriptor(card, glossaryTerms))
            .ToArray();

        return BuildAgentCardStacks(descriptors);
    }

    private static object[] BuildAgentCardStacks(IEnumerable<DeckCardPayload> cards, HashSet<string> glossaryTerms)
    {
        var descriptors = cards
            .Select(card => BuildAgentCardDescriptor(card, glossaryTerms))
            .ToArray();

        return BuildAgentCardStacks(descriptors);
    }

    private static object[] BuildAgentCardStacks(IEnumerable<AgentCardDescriptor> descriptors)
    {
        return descriptors
            .GroupBy(descriptor => descriptor.GroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var line = FormatCardLine(first.name, first.upgraded, group.Count(), first.energy_cost, first.star_cost, first.costs_x, first.star_costs_x, first.rules_text);

                return new
                {
                    line,
                    card_ids = group
                        .Select(descriptor => descriptor.card_id)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray(),
                    keywords = TranslateKeywords(first.keywords),
                    mods = first.mods
                };
            })
            .OrderBy(item => item.line, StringComparer.Ordinal)
            .Cast<object>()
            .ToArray();
    }

    private static AgentCardDescriptor BuildAgentCardDescriptor(CardModel card, HashSet<string> glossaryTerms)
    {
        var rulesText = GetResolvedCardRulesText(card);
        var mods = GetCardModifierTags(card);
        var keywords = GetGlossaryMatches(rulesText, mods);
        CollectGlossaryTerms(glossaryTerms, rulesText, mods);

        return new AgentCardDescriptor(
            card.Title,
            card.IsUpgraded,
            card.EnergyCost.GetWithModifiers(CostModifiers.All),
            Math.Max(0, card.GetStarCostWithModifiers()),
            card.EnergyCost.CostsX,
            card.HasStarCostX,
            rulesText,
            keywords,
            mods,
            card.Id.Entry);
    }

    private static AgentCardDescriptor BuildAgentCardDescriptor(DeckCardPayload card, HashSet<string> glossaryTerms)
    {
        var rulesText = GetPreferredCardRulesText(card.rules_text, card.resolved_rules_text);
        var keywords = GetGlossaryMatches(rulesText);
        CollectGlossaryTerms(glossaryTerms, rulesText);

        return new AgentCardDescriptor(
            card.name,
            card.upgraded,
            card.energy_cost,
            card.star_cost,
            card.costs_x,
            card.star_costs_x,
            rulesText,
            keywords,
            Array.Empty<string>(),
            card.card_id);
    }

    private static string FormatCardLine(
        string name,
        bool upgraded,
        int count,
        int? energyCost,
        int? starCost,
        bool costsX,
        bool starCostsX,
        string rulesText)
    {
        var title = upgraded && !name.EndsWith("+", StringComparison.Ordinal) ? $"{name}+" : name;
        if (count > 1)
        {
            title = $"{title}*{count}";
        }

        var cost = FormatCardCost(energyCost, starCost, costsX, starCostsX);
        var prefix = string.IsNullOrWhiteSpace(cost) ? title : $"{title} [{cost}]";
        return string.IsNullOrWhiteSpace(rulesText)
            ? prefix
            : Loc.T("{0}：{1}", prefix, rulesText);
    }

    private static string FormatCardCost(int? energyCost, int? starCost, bool costsX, bool starCostsX)
    {
        var parts = new List<string>();
        if (costsX)
        {
            parts.Add(Loc.T("X费"));
        }
        else if (energyCost.HasValue)
        {
            parts.Add(Loc.T("{0}费", Math.Max(0, energyCost.Value)));
        }

        if (starCostsX)
        {
            parts.Add(Loc.T("X星"));
        }
        else if (starCost.HasValue && starCost.Value > 0)
        {
            parts.Add(Loc.T("{0}星", starCost.Value));
        }

        return string.Join("/", parts);
    }

    private static string FormatOrbLine(CombatOrbPayload orb)
    {
        return Loc.T("{0} 被动{1}/激发{2}", orb.name, orb.passive_value, orb.evoke_value);
    }

    private static string FormatPetLine(CombatPetPayload pet)
    {
        return Loc.T("{0} {1}/{2} 格挡{3}", pet.name, pet.current_hp, pet.max_hp, pet.block);
    }

    // Powers decide combat math (Strength scales every hit, Vulnerable/Weak move the numbers,
    // Thorns punishes multi-hit lines), so the compact view reports them as short id+amount lines
    // rather than dropping them. The raw payload keeps the full object shape for /state consumers.
    private static string FormatPowerLine(CombatPowerPayload power)
    {
        var amount = power.amount is int value ? $" {value}" : string.Empty;
        var debuff = power.is_debuff ? " [debuff]" : string.Empty;
        return $"{power.power_id}{amount}{debuff}";
    }

    private static string FormatPotionLine(RunPotionPayload potion)
    {
        if (!potion.occupied)
        {
            return Loc.T("{0}: 空", potion.index);
        }

        var usage = string.IsNullOrWhiteSpace(potion.usage) ? string.Empty : Loc.T("：{0}", potion.usage);
        return Loc.T("{0}: {1}{2}", potion.index, potion.name, usage);
    }

    private static string FormatShopPotionLine(ShopPotionPayload potion)
    {
        var name = string.IsNullOrWhiteSpace(potion.name) ? Loc.T("空") : potion.name;
        var usage = string.IsNullOrWhiteSpace(potion.usage) ? string.Empty : Loc.T("：{0}", potion.usage);
        return Loc.T("{0}{1} | {2}g", name, usage, potion.price);
    }

    private static string FormatEventOptionLine(EventOptionPayload option)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(option.title))
        {
            segments.Add(option.title);
        }

        if (!string.IsNullOrWhiteSpace(option.description))
        {
            segments.Add(option.description);
        }

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(option.text_key))
        {
            segments.Add(option.text_key);
        }

        if (option.is_locked)
        {
            segments.Add("LOCKED");
        }

        if (option.will_kill_player)
        {
            segments.Add("LETHAL");
        }

        return string.Join(" | ", segments);
    }

    private static object[] BuildStructuredPileCards(CardModel[] cards)
    {
        return cards.Select(card => new
        {
            card_id = card.Id.Entry,
            upgraded = card.IsUpgraded,
            card_type = card.Type.ToString()
        }).ToArray();
    }

    private static string[] GetCardModifierTags(CardModel? card)
    {
        if (card == null)
        {
            return Array.Empty<string>();
        }

        // Read by type. The by-name version tried Enchantments, Enchants, Modifiers, ModifierIds,
        // Affixes, Augments and Keywords: CardModel has only Keywords, a set of CardKeyword enum
        // values that the token extractor could not turn into text, so every card reported no mods.
        // The enchantment is CardModel.Enchantment, singular, which the list never named.
        var values = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var keyword in card.Keywords)
            {
                if (keyword != CardKeyword.None)
                {
                    // The enum name is the English keyword ("Exhaust", "Retain"), which is what
                    // AgentKeywordAliases matches the Chinese glossary terms against.
                    values.Add(keyword.ToString());
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            // A card mid-transform can briefly have no keyword set; it has no tags this frame.
        }

        if (card.Enchantment is { } enchantment)
        {
            values.Add("Enchantment");
            var title = SafeReadString(() => enchantment.Title.GetFormattedText());
            if (!string.IsNullOrWhiteSpace(title))
            {
                values.Add(NormalizeCardRulesText(title));
            }
        }

        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static CardModel[] PileCards(CardPile? pile) =>
        pile?.Cards.Where(card => card != null).ToArray() ?? Array.Empty<CardModel>();

    private static string[] GetGlossaryMatches(string text, params string[][] modifierGroups)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (keyword, _) in AgentKeywordDefinitions)
        {
            if (ContainsKeyword(text, keyword) || modifierGroups.Any(group => ContainsKeyword(group, keyword)))
            {
                values.Add(keyword);
            }
        }

        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// True when the text carries this keyword in any spelling the glossary knows. Card text
    /// arrives in whatever language the game is running in, so the English spellings count too.
    /// </summary>
    private static bool ContainsKeyword(string? text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains(keyword, StringComparison.Ordinal))
        {
            return true;
        }

        if (!AgentKeywordAliases.TryGetValue(keyword, out var aliases))
        {
            return false;
        }

        return aliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsKeyword(IEnumerable<string> texts, string keyword)
    {
        return texts.Any(text => ContainsKeyword(text, keyword));
    }

    /// <summary>
    /// The keyword labels the model reads follow the game language, and the glossary is built from
    /// the same spellings so the two stay in step. Chinese is the identity case.
    /// </summary>
    private static string[] TranslateKeywords(string[] keywords)
    {
        if (Loc.IsChinese || keywords.Length == 0)
        {
            return keywords;
        }

        return keywords.Select(keyword => Loc.T(keyword)).ToArray();
    }

    private static void CollectGlossaryTerms(HashSet<string> glossaryTerms, string? text, params string[][] modifierGroups)
    {
        if (glossaryTerms.Count >= AgentKeywordDefinitions.Length)
        {
            return;
        }

        foreach (var keyword in GetGlossaryMatches(text ?? string.Empty, modifierGroups))
        {
            glossaryTerms.Add(keyword);
        }
    }

    private static Dictionary<string, string> BuildAgentGlossary(HashSet<string> glossaryTerms)
    {
        var glossary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (keyword, definition) in AgentKeywordDefinitions)
        {
            if (glossaryTerms.Contains(keyword))
            {
                glossary[Loc.T(keyword)] = Loc.T(definition);
            }
        }

        return glossary;
    }

    private static string? NormalizeTargetHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("enemy", StringComparison.Ordinal))
        {
            return "enemy";
        }

        if (normalized.Contains("player", StringComparison.Ordinal) || normalized.Contains("self", StringComparison.Ordinal))
        {
            return "player";
        }

        return normalized;
    }

    private static readonly (string keyword, string definition)[] AgentKeywordDefinitions =
    {
        ("力量", "每层力量通常使攻击额外造成 1 点伤害。"),
        ("敏捷", "每层敏捷通常使获得的格挡额外增加 1。"),
        ("易伤", "易伤单位会承受更多攻击伤害。"),
        ("虚弱", "虚弱单位造成的攻击伤害会降低。"),
        ("脆弱", "脆弱单位获得的格挡会减少。"),
        ("格挡", "格挡会优先抵消即将受到的伤害。"),
        ("消耗", "消耗牌打出后会移出本场战斗。"),
        ("保留", "保留牌在回合结束时不会被弃掉。"),
        ("中毒", "中毒会在回合结束时造成等量生命损失，然后层数减少。"),
        ("眩晕", "眩晕通常是无法主动打出的状态牌。"),
        ("灼伤", "灼伤通常会在手中或结算时带来额外伤害。"),
        ("虚空", "虚空通常会在抽到时消耗能量或妨碍出牌。"),
        ("力量流失", "力量流失会临时降低力量。"),
        ("集中", "集中通常会强化充能球的被动与激发效果。"),
        ("球位", "球位决定你能同时容纳多少个充能球。"),
        ("附魔", "附魔是卡牌附着的额外词条或效果层。"),
        ("灌注", "灌注表示卡牌带有额外附着效果。"),
        ("临时", "临时牌通常会在回合结束或打出后离开牌组流转。")
    };

    /// <summary>
    /// Latin spellings the same keywords take in the English card text. The Chinese spelling stays
    /// the key everywhere else; these only widen the search so an English client still matches.
    /// </summary>
    private static readonly Dictionary<string, string[]> AgentKeywordAliases = new(StringComparer.Ordinal)
    {
        ["力量"] = new[] { "Strength" },
        ["敏捷"] = new[] { "Dexterity" },
        ["易伤"] = new[] { "Vulnerable" },
        ["虚弱"] = new[] { "Weak" },
        ["脆弱"] = new[] { "Frail" },
        ["格挡"] = new[] { "Block" },
        ["消耗"] = new[] { "Exhaust" },
        ["保留"] = new[] { "Retain" },
        ["中毒"] = new[] { "Poison" },
        ["眩晕"] = new[] { "Dazed" },
        ["灼伤"] = new[] { "Burn" },
        ["虚空"] = new[] { "Void" },
        ["力量流失"] = new[] { "Strength Down" },
        ["集中"] = new[] { "Focus" },
        ["球位"] = new[] { "Orb Slot" },
        ["附魔"] = new[] { "Enchantment", "Enchant" },
        ["灌注"] = new[] { "Imbued", "Imbue", "Infused" },
        ["临时"] = new[] { "Temporary" }
    };
}

internal readonly record struct AgentCardDescriptor(
    string name,
    bool upgraded,
    int energy_cost,
    int star_cost,
    bool costs_x,
    bool star_costs_x,
    string rules_text,
    string[] keywords,
    string[] mods,
    string card_id)
{
    public string GroupKey =>
        string.Join(
            "\u001f",
            name,
            upgraded ? "1" : "0",
            energy_cost.ToString(),
            star_cost.ToString(),
            costs_x ? "1" : "0",
            star_costs_x ? "1" : "0",
            rules_text,
            string.Join("\u001e", mods));
}
