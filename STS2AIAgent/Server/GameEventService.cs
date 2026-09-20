using System.Text.Json;
using System.Threading.Channels;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Game;

namespace STS2AIAgent.Server;

internal sealed class GameEventService
{
    private const string LogPrefix = "[STS2AIAgent.GameEventService]";
    private const int DefaultPollIntervalMs = 120;
    private const int SubscriberQueueCapacity = 256;

    private static readonly Lazy<GameEventService> LazyInstance = new(() => new GameEventService());

    private readonly object _gate = new();
    private readonly EventStreamSubscribers<GameEventEnvelope, StateDigest> _subscribers = new(SubscriberQueueCapacity);
    private readonly ConsecutiveRepeatSuppressor _repeatGuard = new();

    private readonly EventPollingCoordinator _polling;
    private long _nextEventId;
    private readonly TimeSpan _pollInterval;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static GameEventService Instance => LazyInstance.Value;

    private GameEventService()
    {
        var pollMs = DefaultPollIntervalMs;
        var rawPollMs = Environment.GetEnvironmentVariable("STS2_EVENT_POLL_MS");
        if (!string.IsNullOrWhiteSpace(rawPollMs) &&
            int.TryParse(rawPollMs.Trim(), out var configuredPollMs) &&
            configuredPollMs is >= 16 and <= 5000)
        {
            pollMs = configuredPollMs;
        }

        _pollInterval = TimeSpan.FromMilliseconds(pollMs);
        _polling = new EventPollingCoordinator(PollOnceAsync, _pollInterval);
    }

    public void Start()
    {
        _polling.Start();
        Log.Info($"{LogPrefix} Started with poll interval {_pollInterval.TotalMilliseconds:0}ms");
    }

    public void Stop()
    {
        // Stop the loop first, then clear under the gate. Reversing these two would let a poll that
        // was already past its staleness check republish a snapshot after the clear, and a
        // subscriber arriving after shutdown would then be handed that lifecycle's state.
        _polling.SetSubscriberCount(0);
        _polling.Stop();

        lock (_gate)
        {
            _subscribers.CompleteAll();
        }

        Log.Info($"{LogPrefix} Stopped");
    }

    public GameEventSubscription Subscribe()
    {
        lock (_gate)
        {
            var lease = _subscribers.Subscribe(
                _subscribers.Snapshot,
                digest => BuildSnapshotEnvelope(digest, "stream_ready"));
            _polling.SetSubscriberCount(_subscribers.Count);
            return new GameEventSubscription(lease.Id, lease.Reader, Unsubscribe);
        }
    }

    private void Unsubscribe(long subscriberId)
    {
        lock (_gate)
        {
            if (_subscribers.Unsubscribe(subscriberId))
            {
                // The session drops the snapshot when the last subscriber leaves, so the next
                // connection starts a fresh lifecycle instead of being handed an idle-period
                // snapshot as its first frame.
                _polling.SetSubscriberCount(_subscribers.Count);
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var generation = _polling.CurrentGeneration;
            var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_polling.IsGenerationCurrent(generation))
            {
                // The lifecycle that asked for this sample ended while it was in flight. Committing
                // it would resurrect state the new lifecycle never observed.
                return;
            }

            ProcessState(state);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TaskCanceledException)
        {
            Log.Warn($"{LogPrefix} Poll failed: {ex.Message}");
        }
    }

    private void ProcessState(GameStatePayload state)
    {
        lock (_gate)
        {
            ProcessStateLocked(state);
        }
    }

    private void ProcessStateLocked(GameStatePayload state)
    {
        var current = StateDigest.FromState(state);
        var previous = _subscribers.Snapshot;

        if (previous == null)
        {
            Publish("session_started", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                session_phase = current.SessionPhase
            });
            // A subscriber that connected on a lifecycle with no snapshot yet -- the normal case
            // now that polling only runs on demand -- has had nothing to orient on. Publish the same
            // snapshot every later subscriber gets, after session_started so the ordering contract
            // in docs/api.md holds for this connection too.
            PublishSnapshot("stream_ready", current);
            return;
        }

        if (!string.Equals(previous.Screen, current.Screen, StringComparison.Ordinal))
        {
            Publish("screen_changed", new
            {
                from = previous.Screen,
                to = current.Screen,
                run_id = current.RunId
            });
        }

        if (!previous.InCombat && current.InCombat)
        {
            Publish("combat_started", new
            {
                run_id = current.RunId,
                turn = current.Turn
            });
        }
        else if (previous.InCombat && !current.InCombat)
        {
            Publish("combat_ended", new
            {
                run_id = current.RunId
            });
        }
        else if (current.InCombat && previous.Turn != current.Turn)
        {
            Publish("combat_turn_changed", new
            {
                run_id = current.RunId,
                from = previous.Turn,
                to = current.Turn
            });
        }

        if (previous.PlayerActionWindowOpen != current.PlayerActionWindowOpen)
        {
            Publish(current.PlayerActionWindowOpen ? "player_action_window_opened" : "player_action_window_closed", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                actions = current.AvailableActions
            });
        }

        if (!previous.RouteDecisionRequired && current.RouteDecisionRequired)
        {
            Publish("route_decision_required", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                available_nodes = current.AvailableMapNodes
            });
        }

        if (!previous.RewardDecisionRequired && current.RewardDecisionRequired)
        {
            Publish("reward_decision_required", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                reward_count = current.RewardCount,
                card_option_count = current.RewardCardOptionCount
            });
        }

        if (!string.Equals(previous.EventId, current.EventId, StringComparison.Ordinal) ||
            previous.EventOptionCount != current.EventOptionCount ||
            previous.EventFinished != current.EventFinished)
        {
            if (!string.IsNullOrEmpty(current.EventId) || current.EventOptionCount > 0)
            {
                Publish("event_state_changed", new
                {
                    run_id = current.RunId,
                    event_id = current.EventId,
                    option_count = current.EventOptionCount,
                    is_finished = current.EventFinished
                });
            }
        }

        if (!string.Equals(previous.ActionSignature, current.ActionSignature, StringComparison.Ordinal))
        {
            Publish("available_actions_changed", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                actions = current.AvailableActions
            });
        }

        // The snapshot is recorded, not re-announced. It is the frame a *new* subscriber is handed so
        // it can orient without polling /state; broadcasting it every poll turned a 120 ms loop into a
        // stream of stream_ready frames that told a client nothing it had not already been told.
        _subscribers.RecordSnapshot(current);
    }

    private void Publish(string eventType, object data)
    {
        // The id is allocated only for a frame that is actually sent. Building the envelope first
        // burned an id on every suppressed repeat, which left gaps in the stream that a client would
        // read as loss -- the one signal the docs promise means something.
        if (!_repeatGuard.ShouldPublish(eventType + "\n" + JsonSerializer.Serialize(data, JsonOptions)))
        {
            return;
        }

        var staleCount = _subscribers.Publish(BuildEnvelope(eventType, data));
        if (staleCount > 0)
        {
            _polling.SetSubscriberCount(_subscribers.Count);
            Log.Warn($"{LogPrefix} Disconnected {staleCount} slow event subscriber(s); reconnect to resynchronize state.");
        }
    }

    /// <summary>
    /// Records <paramref name="snapshot"/> as the newest state and sends it to everyone attached.
    /// The literal event name lives in the only caller's argument list so the api-facts gate can
    /// still see which types this service publishes.
    /// </summary>
    private void PublishSnapshot(string eventType, StateDigest snapshot)
    {
        _subscribers.PublishSnapshot(snapshot, BuildSnapshotEnvelope(snapshot, eventType));
    }

    private GameEventEnvelope BuildSnapshotEnvelope(StateDigest snapshot, string eventType)
    {
        return BuildEnvelope(eventType, new
        {
            run_id = snapshot.RunId,
            screen = snapshot.Screen,
            in_combat = snapshot.InCombat,
            turn = snapshot.Turn,
            action_window_open = snapshot.PlayerActionWindowOpen
        });
    }

    private GameEventEnvelope BuildEnvelope(string eventType, object data)
    {
        var eventId = Interlocked.Increment(ref _nextEventId);
        return new GameEventEnvelope
        {
            event_id = eventId,
            type = eventType,
            timestamp_utc = DateTime.UtcNow.ToString("O"),
            data = data
        };
    }

    private sealed class StateDigest
    {
        public string RunId { get; init; } = "run_unknown";
        public string Screen { get; init; } = "UNKNOWN";
        public string SessionPhase { get; init; } = "menu";
        public bool InCombat { get; init; }
        public int? Turn { get; init; }
        public string[] AvailableActions { get; init; } = Array.Empty<string>();
        public string ActionSignature { get; init; } = string.Empty;
        public bool PlayerActionWindowOpen { get; init; }
        public bool RouteDecisionRequired { get; init; }
        public int AvailableMapNodes { get; init; }
        public bool RewardDecisionRequired { get; init; }
        public int RewardCount { get; init; }
        public int RewardCardOptionCount { get; init; }
        public string EventId { get; init; } = string.Empty;
        public int EventOptionCount { get; init; }
        public bool EventFinished { get; init; }

        public static StateDigest FromState(GameStatePayload state)
        {
            var actions = (state.available_actions ?? Array.Empty<string>())
                .Where(static action => !string.IsNullOrWhiteSpace(action))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static action => action, StringComparer.Ordinal)
                .ToArray();

            var actionSet = new HashSet<string>(actions, StringComparer.Ordinal);
            var actionWindowOpen = actionSet.Contains("play_card") ||
                                   actionSet.Contains("end_turn") ||
                                   actionSet.Contains("confirm_selection");
            var routeDecisionRequired = actionSet.Contains("choose_map_node");
            var rewardDecisionRequired = state.screen == "REWARD" ||
                                         actionSet.Contains("collect_rewards_and_proceed") ||
                                         actionSet.Contains("claim_reward") ||
                                         actionSet.Contains("choose_reward_card");

            return new StateDigest
            {
                RunId = state.run_id,
                Screen = state.screen,
                SessionPhase = state.session.phase,
                InCombat = state.in_combat,
                Turn = state.turn,
                AvailableActions = actions,
                ActionSignature = string.Join("|", actions),
                PlayerActionWindowOpen = actionWindowOpen,
                RouteDecisionRequired = routeDecisionRequired,
                AvailableMapNodes = state.map?.available_nodes?.Length ?? 0,
                RewardDecisionRequired = rewardDecisionRequired,
                RewardCount = state.reward?.rewards?.Length ?? 0,
                RewardCardOptionCount = state.reward?.card_options?.Length ?? 0,
                EventId = state.@event?.event_id ?? string.Empty,
                EventOptionCount = state.@event?.options?.Length ?? 0,
                EventFinished = state.@event?.is_finished ?? false
            };
        }
    }
}

internal sealed class GameEventSubscription : IDisposable
{
    private readonly Action<long> _onDispose;
    private readonly long _subscriberId;
    private int _disposed;

    public GameEventSubscription(long subscriberId, ChannelReader<GameEventEnvelope> reader, Action<long> onDispose)
    {
        _subscriberId = subscriberId;
        _onDispose = onDispose;
        Reader = reader;
    }

    public ChannelReader<GameEventEnvelope> Reader { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _onDispose(_subscriberId);
    }
}

internal sealed class GameEventEnvelope
{
    public long event_id { get; init; }

    public string type { get; init; } = string.Empty;

    public string timestamp_utc { get; init; } = string.Empty;

    public object? data { get; init; }
}
