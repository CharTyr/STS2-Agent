using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2AIAgent.Game;

namespace STS2AIAgent.Server;

internal sealed class GameFactEventSource
{
    private const string LogPrefix = "[STS2AIAgent.GameFactEventSource]";
    private static readonly Lazy<GameFactEventSource> LazyInstance = new(() => new GameFactEventSource());

    private readonly object _gate = new();
    private ConditionalWeakTable<CardPlay, CardActionAccumulator> _cardActions = new();
    private readonly Dictionary<string, string> _pendingAiCardCorrelations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _observedAiCardCorrelations = new(StringComparer.Ordinal);
    private readonly HashSet<CombatRoom> _wonRooms = new(ReferenceEqualityComparer.Instance);
    private CombatFactContext? _combat;
    private int _historyCursor;
    private long _nextCorrelationId;
    private bool _started;

    public static GameFactEventSource Instance => LazyInstance.Value;

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            var manager = CombatManager.Instance;
            manager.CombatSetUp += OnCombatSetUp;
            manager.CombatBegan += OnCombatBegan;
            manager.TurnStarted += OnTurnStarted;
            manager.CombatWon += OnCombatWon;
            manager.CombatEnded += OnCombatEnded;
            manager.History.Changed += OnHistoryChanged;
            _historyCursor = manager.History.Entries.Count();
        }

        Log.Info($"{LogPrefix} Subscribed to authoritative combat lifecycle and history events.");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            var manager = CombatManager.Instance;
            manager.CombatSetUp -= OnCombatSetUp;
            manager.CombatBegan -= OnCombatBegan;
            manager.TurnStarted -= OnTurnStarted;
            manager.CombatWon -= OnCombatWon;
            manager.CombatEnded -= OnCombatEnded;
            manager.History.Changed -= OnHistoryChanged;
            _historyCursor = 0;
            _combat = null;
            _wonRooms.Clear();
            _pendingAiCardCorrelations.Clear();
            _observedAiCardCorrelations.Clear();
            _cardActions = new ConditionalWeakTable<CardPlay, CardActionAccumulator>();
        }
    }

    public void RecordDamage(DamageFact fact)
    {
        CombatFactContext? combat;
        CardActionAccumulator? action = null;
        string correlationId;
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            combat = _combat ?? TryBuildCurrentCombatContext();
            if (fact.CardPlay != null)
            {
                _cardActions.TryGetValue(fact.CardPlay, out action);
            }

            correlationId = action?.CorrelationId ?? NextCorrelationId(combat?.CombatId, "effect");
            action?.AddDamage(fact.Results);
        }

        var source = fact.EffectSource ?? fact.CardSource;
        var dealer = fact.Dealer ?? ResolveSourceOwner(source);
        var targets = fact.Results.Select(result => new
        {
            target = BuildCreatureRef(result.Receiver),
            blocked_damage = result.BlockedDamage,
            hp_loss = result.UnblockedDamage,
            overkill_damage = result.OverkillDamage,
            total_damage = result.TotalDamage,
            was_block_broken = result.WasBlockBroken,
            was_fully_blocked = result.WasFullyBlocked,
            was_target_killed = result.WasTargetKilled
        }).ToArray();

        GameEventService.Instance.Publish(
            "damage_resolved",
            new
            {
                action_type = fact.CardPlay == null ? "effect" : "card_play",
                requested_amount = fact.RequestedAmount,
                value_props = fact.Props,
                source_attribution = source == null ? "unavailable" : "exact",
                source = BuildModelRef(source),
                actor = BuildCreatureRef(dealer),
                targets,
                total_blocked_damage = targets.Sum(target => target.blocked_damage),
                total_hp_loss = targets.Sum(target => target.hp_loss),
                total_overkill_damage = targets.Sum(target => target.overkill_damage)
            },
            correlationId,
            combat?.RunId,
            combat?.CombatId);
    }

    public void RecordAiDecision(AiDecisionFact fact)
    {
        CombatFactContext? combat;
        var correlationId = $"decision_{SanitizeId(fact.RequestId)}";
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            combat = _combat;
            if (!string.IsNullOrWhiteSpace(fact.Option?.CardInstanceId))
            {
                if (_observedAiCardCorrelations.Remove(fact.Option.CardInstanceId, out var observedCorrelation))
                {
                    correlationId = observedCorrelation;
                }
                else
                {
                    _pendingAiCardCorrelations[fact.Option.CardInstanceId] = correlationId;
                }
            }
        }

        GameEventService.Instance.Publish(
            "ai_decision",
            new
            {
                request_id = fact.RequestId,
                snapshot_id = fact.SnapshotId,
                actor_id = fact.ActorId,
                actor_role = "ai_teammate",
                chosen_action_id = fact.ChosenActionId,
                ranked_action_ids = fact.RankedActionIds,
                reason = fact.Reason,
                chosen_action = fact.Option == null ? null : new
                {
                    action_id = fact.Option.ActionId,
                    action_type = fact.Option.ActionType,
                    description = fact.Option.Description,
                    label = fact.Option.Label,
                    summary = fact.Option.Summary,
                    card_id = fact.Option.CardId,
                    card_instance_id = fact.Option.CardInstanceId,
                    target_id = fact.Option.TargetId,
                    target_label = fact.Option.TargetLabel,
                    energy_cost = fact.Option.EnergyCost,
                    priority_tags = fact.Option.PriorityTags,
                    metadata = fact.Option.Metadata
                },
                provenance = "optional_ai_teammate_decision_backend"
            },
            correlationId,
            combat?.RunId,
            combat?.CombatId);
    }

    private void OnCombatSetUp(CombatState state)
    {
        lock (_gate)
        {
            _combat = BuildCombatContext(state);
            _historyCursor = CombatManager.Instance.History.Entries.Count();
            _cardActions = new ConditionalWeakTable<CardPlay, CardActionAccumulator>();
            _pendingAiCardCorrelations.Clear();
            _observedAiCardCorrelations.Clear();
        }
    }

    private void OnCombatBegan(CombatState state)
    {
        CombatFactContext combat;
        lock (_gate)
        {
            combat = _combat ?? BuildCombatContext(state);
            _combat = combat;
        }

        GameEventService.Instance.Publish(
            "combat_started",
            new
            {
                run_id = combat.RunId,
                combat_id = combat.CombatId,
                turn = state.RoundNumber,
                encounter = new
                {
                    encounter_id = combat.EncounterId,
                    encounter_type = combat.EncounterType
                }
            },
            combat.CombatId,
            combat.RunId,
            combat.CombatId);
    }

    private void OnTurnStarted(CombatState state)
    {
        CombatFactContext combat;
        int? previousTurn;
        lock (_gate)
        {
            combat = _combat ?? BuildCombatContext(state);
            previousTurn = combat.LastTurn;
            combat.LastTurn = state.RoundNumber;
            _combat = combat;
        }

        GameEventService.Instance.Publish(
            "combat_turn_changed",
            new
            {
                run_id = combat.RunId,
                combat_id = combat.CombatId,
                from = previousTurn,
                to = state.RoundNumber,
                side = state.CurrentSide.ToString().ToLowerInvariant()
            },
            $"{combat.CombatId}_turn_{state.RoundNumber}",
            combat.RunId,
            combat.CombatId);
    }

    private void OnCombatWon(CombatRoom room)
    {
        lock (_gate)
        {
            _wonRooms.Add(room);
        }
    }

    private void OnCombatEnded(CombatRoom room)
    {
        CombatFactContext combat;
        bool victory;
        lock (_gate)
        {
            combat = _combat ?? BuildCombatContext(room.CombatState);
            victory = _wonRooms.Remove(room);
            _combat = null;
            _historyCursor = 0;
            _cardActions = new ConditionalWeakTable<CardPlay, CardActionAccumulator>();
            _pendingAiCardCorrelations.Clear();
            _observedAiCardCorrelations.Clear();
        }

        GameEventService.Instance.Publish(
            "combat_ended",
            new
            {
                run_id = combat.RunId,
                combat_id = combat.CombatId,
                outcome = victory ? "victory" : "defeat",
                victory,
                outcome_source = victory
                    ? "combat_manager.combat_won"
                    : "combat_manager.combat_ended_without_combat_won",
                encounter = new
                {
                    encounter_id = room.Encounter.Id.Entry,
                    encounter_type = ResolveEncounterType(room.RoomType)
                }
            },
            combat.CombatId,
            combat.RunId,
            combat.CombatId);
    }

    private void OnHistoryChanged()
    {
        CombatHistoryEntry[] appended;
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            var entries = CombatManager.Instance.History.Entries.ToArray();
            if (entries.Length < _historyCursor)
            {
                _historyCursor = 0;
            }

            appended = entries.Skip(_historyCursor).ToArray();
            _historyCursor = entries.Length;
        }

        foreach (var entry in appended)
        {
            switch (entry)
            {
                case CardPlayStartedEntry started:
                    RecordCardPlayStarted(started.CardPlay);
                    break;
                case CardPlayFinishedEntry finished:
                    RecordCardPlayFinished(finished.CardPlay);
                    break;
            }
        }
    }

    private void RecordCardPlayStarted(CardPlay cardPlay)
    {
        CombatFactContext? combat;
        CardActionAccumulator action;
        lock (_gate)
        {
            combat = _combat ?? TryBuildCurrentCombatContext();
            var cardInstanceId = GetCardInstanceId(cardPlay.Card);
            var correlationId = cardInstanceId != null &&
                                !LocalContext.IsMe(cardPlay.Player.Creature) &&
                                _pendingAiCardCorrelations.Remove(cardInstanceId, out var decisionCorrelation)
                ? decisionCorrelation
                : NextCorrelationId(combat?.CombatId, "card");
            if (cardInstanceId != null && !LocalContext.IsMe(cardPlay.Player.Creature))
            {
                _observedAiCardCorrelations[cardInstanceId] = correlationId;
            }
            action = new CardActionAccumulator(correlationId);
            _cardActions.Remove(cardPlay);
            _cardActions.Add(cardPlay, action);
        }

        GameEventService.Instance.Publish(
            "action_started",
            new
            {
                action_type = "card_play",
                actor = BuildCreatureRef(cardPlay.Player.Creature),
                source = BuildModelRef(cardPlay.Card),
                target = BuildCreatureRef(cardPlay.Target),
                card = new
                {
                    card_id = cardPlay.Card.Id.Entry,
                    card_instance_id = GetCardInstanceId(cardPlay.Card),
                    name = cardPlay.Card.Title,
                    upgraded = cardPlay.Card.IsUpgraded,
                    auto_play = cardPlay.IsAutoPlay,
                    play_index = cardPlay.PlayIndex,
                    play_count = cardPlay.PlayCount
                }
            },
            action.CorrelationId,
            combat?.RunId,
            combat?.CombatId);
    }

    private void RecordCardPlayFinished(CardPlay cardPlay)
    {
        CombatFactContext? combat;
        CardActionAccumulator? action;
        lock (_gate)
        {
            combat = _combat;
            _cardActions.TryGetValue(cardPlay, out action);
            _cardActions.Remove(cardPlay);
            var cardInstanceId = GetCardInstanceId(cardPlay.Card);
            if (cardInstanceId != null && !LocalContext.IsMe(cardPlay.Player.Creature))
            {
                _observedAiCardCorrelations.Remove(cardInstanceId);
            }
        }

        var correlationId = action?.CorrelationId ?? NextCorrelationId(combat?.CombatId, "card");
        var totals = action?.Snapshot() ?? Array.Empty<CardTargetDamageTotal>();
        GameEventService.Instance.Publish(
            "action_finished",
            new
            {
                action_type = "card_play",
                actor = BuildCreatureRef(cardPlay.Player.Creature),
                source = BuildModelRef(cardPlay.Card),
                target = BuildCreatureRef(cardPlay.Target),
                card = new
                {
                    card_id = cardPlay.Card.Id.Entry,
                    card_instance_id = GetCardInstanceId(cardPlay.Card),
                    name = cardPlay.Card.Title,
                    upgraded = cardPlay.Card.IsUpgraded,
                    auto_play = cardPlay.IsAutoPlay,
                    play_index = cardPlay.PlayIndex,
                    play_count = cardPlay.PlayCount
                },
                damage = new
                {
                    targets = totals,
                    total_blocked_damage = totals.Sum(total => total.blocked_damage),
                    total_hp_loss = totals.Sum(total => total.hp_loss),
                    total_overkill_damage = totals.Sum(total => total.overkill_damage),
                    hit_count = totals.Sum(total => total.hit_count)
                }
            },
            correlationId,
            combat?.RunId,
            combat?.CombatId);
    }

    private string NextCorrelationId(string? combatId, string kind)
    {
        var sequence = Interlocked.Increment(ref _nextCorrelationId);
        return $"{combatId ?? "session"}_{kind}_{sequence}";
    }

    private static CombatFactContext BuildCombatContext(CombatState state)
    {
        var runId = (state.RunState as RunState)?.Rng.StringSeed ?? "run_unknown";
        var encounterId = state.Encounter?.Id.Entry ?? "encounter_unknown";
        var combatId = CombatManager.Instance.CurrentCombatId is { } id
            ? $"combat_{id.Value}"
            : $"combat_{SanitizeId(encounterId)}";
        return new CombatFactContext(
            runId,
            combatId,
            encounterId,
            ResolveEncounterType(state.Encounter?.RoomType ?? RoomType.Unassigned),
            state.RoundNumber);
    }

    private static CombatFactContext? TryBuildCurrentCombatContext()
    {
        var state = CombatManager.Instance.DebugOnlyGetState();
        return state == null ? null : BuildCombatContext(state);
    }

    internal static string ResolveEncounterType(RoomType roomType) => roomType switch
    {
        RoomType.Monster => "normal",
        RoomType.Elite => "elite",
        RoomType.Boss => "boss",
        _ => roomType.ToString().ToLowerInvariant()
    };

    private static object? BuildModelRef(AbstractModel? source)
    {
        if (source == null)
        {
            return null;
        }

        return new
        {
            source_type = ResolveModelType(source),
            source_id = source.Id.Entry,
            source_name = ResolveModelName(source),
            card_instance_id = source is CardModel sourceCard
                ? GetCardInstanceId(sourceCard)
                : null
        };
    }

    private static string ResolveModelType(AbstractModel model) => model switch
    {
        CardModel => "card",
        PowerModel => "power",
        RelicModel => "relic",
        PotionModel => "potion",
        OrbModel => "orb",
        MonsterModel => "monster",
        _ => "unknown"
    };

    private static string ResolveModelName(AbstractModel model)
    {
        try
        {
            return model switch
            {
                CardModel card => card.Title,
                PowerModel power => power.Title.GetFormattedText(),
                RelicModel relic => relic.Title.GetFormattedText(),
                PotionModel potion => potion.Title.GetFormattedText(),
                OrbModel orb => orb.Title.GetFormattedText(),
                MonsterModel monster => monster.Title.GetFormattedText(),
                _ => model.Id.Entry
            };
        }
        catch
        {
            return model.Id.Entry;
        }
    }

    private static Creature? ResolveSourceOwner(AbstractModel? source)
    {
        try
        {
            return source switch
            {
                CardModel card => card.Owner.Creature,
                PowerModel power => power.Owner,
                RelicModel relic => relic.Owner.Creature,
                PotionModel potion => potion.Owner.Creature,
                OrbModel orb => orb.Owner.Creature,
                MonsterModel monster => monster.Creature,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? BuildCreatureRef(Creature? creature)
    {
        if (creature == null)
        {
            return null;
        }

        return new
        {
            entity_id = creature.Player != null
                ? $"player_{creature.Player.NetId}"
                : creature.CombatId is { } combatId
                    ? $"creature_{combatId}"
                    : $"creature_{SanitizeId(creature.ModelId.Entry)}",
            entity_type = creature.Player != null ? "player" : "enemy",
            model_id = creature.ModelId.Entry,
            name = creature.Name,
            player_id = creature.Player?.NetId.ToString(),
            is_local = creature.Player != null && LocalContext.IsMe(creature)
        };
    }

    private static string? GetCardInstanceId(CardModel card) =>
        NetCombatCardDb.Instance.TryGetCardId(card, out var id) ? $"combat_{id}" : null;

    private static string SanitizeId(string value)
    {
        var chars = value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
            ? character
            : '_').ToArray();
        return new string(chars);
    }

    private sealed class CombatFactContext(
        string runId,
        string combatId,
        string encounterId,
        string encounterType,
        int? lastTurn)
    {
        public string RunId { get; } = runId;
        public string CombatId { get; } = combatId;
        public string EncounterId { get; } = encounterId;
        public string EncounterType { get; } = encounterType;
        public int? LastTurn { get; set; } = lastTurn;
    }

    private sealed class CardActionAccumulator(string correlationId)
    {
        private readonly Dictionary<string, CardTargetDamageTotal> _totals = new(StringComparer.Ordinal);
        public string CorrelationId { get; } = correlationId;

        public void AddDamage(IEnumerable<DamageResult> results)
        {
            foreach (var result in results)
            {
                var targetId = result.Receiver.Player != null
                    ? $"player_{result.Receiver.Player.NetId}"
                    : result.Receiver.CombatId is { } combatId
                        ? $"creature_{combatId}"
                        : $"creature_{SanitizeId(result.Receiver.ModelId.Entry)}";
                if (!_totals.TryGetValue(targetId, out var total))
                {
                    total = new CardTargetDamageTotal
                    {
                        target_id = targetId,
                        target_name = result.Receiver.Name
                    };
                    _totals[targetId] = total;
                }

                total.blocked_damage += result.BlockedDamage;
                total.hp_loss += result.UnblockedDamage;
                total.overkill_damage += result.OverkillDamage;
                total.hit_count += 1;
            }
        }

        public CardTargetDamageTotal[] Snapshot() => _totals.Values
            .OrderBy(static total => total.target_id, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed class CardTargetDamageTotal
{
    public string target_id { get; init; } = string.Empty;
    public string target_name { get; init; } = string.Empty;
    public int blocked_damage { get; set; }
    public int hp_loss { get; set; }
    public int overkill_damage { get; set; }
    public int hit_count { get; set; }
}
