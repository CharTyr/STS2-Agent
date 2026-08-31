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
    private readonly Dictionary<long, Channel<GameEventEnvelope>> _subscribers = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long _nextSubscriberId;
    private long _nextEventId;
    private StateDigest? _lastState;
    private readonly TimeSpan _pollInterval;

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
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loopTask != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
            GameFactEventSource.Instance.Start();
            Log.Info($"{LogPrefix} Started with poll interval {_pollInterval.TotalMilliseconds:0}ms");
        }
    }

    public void Stop()
    {
        GameFactEventSource.Instance.Stop();
        CancellationTokenSource? cts;
        Task? loopTask;
        List<Channel<GameEventEnvelope>> channels;

        lock (_gate)
        {
            cts = _cts;
            loopTask = _loopTask;
            _cts = null;
            _loopTask = null;
            _lastState = null;
            channels = _subscribers.Values.ToList();
            _subscribers.Clear();
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Failed to cancel loop: {ex}");
        }

        try
        {
            loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException or OperationCanceledException))
        {
            Log.Info($"{LogPrefix} Poll loop stopped during shutdown.");
        }

        foreach (var channel in channels)
        {
            channel.Writer.TryComplete();
        }

        cts?.Dispose();
        Log.Info($"{LogPrefix} Stopped");
    }

    public GameEventSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<GameEventEnvelope>(new BoundedChannelOptions(SubscriberQueueCapacity)
        {
            // Never silently discard fact events. A lagging subscriber is closed so its client can
            // observe the interruption and reconnect instead of persisting a plausible but incomplete timeline.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        long subscriberId;
        lock (_gate)
        {
            subscriberId = ++_nextSubscriberId;
            _subscribers[subscriberId] = channel;
            if (_lastState is { } snapshot)
            {
                // stream_ready is a subscription cursor, not a new global fact. Reusing the latest
                // sequence keeps existing subscribers gap-free and guarantees that the next envelope
                // observed by this subscriber has a greater sequence.
                var cursor = Volatile.Read(ref _nextEventId);
                channel.Writer.TryWrite(new GameEventEnvelope
                {
                    event_id = cursor,
                    sequence = cursor,
                    protocol_version = ProtocolContract.Version,
                    type = "stream_ready",
                    timestamp_utc = DateTime.UtcNow.ToString("O"),
                    data = new
                    {
                        latest_sequence = cursor,
                        run_id = snapshot.RunId,
                        screen = snapshot.Screen,
                        in_combat = snapshot.InCombat,
                        turn = snapshot.Turn,
                        action_window_open = snapshot.PlayerActionWindowOpen
                    }
                });
            }
        }

        return new GameEventSubscription(subscriberId, channel.Reader, Unsubscribe);
    }

    private void Unsubscribe(long subscriberId)
    {
        Channel<GameEventEnvelope>? channel = null;
        lock (_gate)
        {
            if (_subscribers.Remove(subscriberId, out var removed))
            {
                channel = removed;
            }
        }

        channel?.Writer.TryComplete();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload);
                ProcessState(state);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not TaskCanceledException)
            {
                Log.Warn($"{LogPrefix} Poll failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessState(GameStatePayload state)
    {
        lock (_gate)
        {
            ProcessStateLocked(StateDigest.FromState(state));
        }
    }

    private void ProcessStateLocked(StateDigest current)
    {
        var previous = _lastState;

        if (previous == null)
        {
            _lastState = current;
            Publish("session_started", new
            {
                run_id = current.RunId,
                screen = current.Screen,
                session_phase = current.SessionPhase
            });
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

        _lastState = current;
    }

    internal void RecordDamageFact(DamageFact fact) => GameFactEventSource.Instance.RecordDamage(fact);

    internal void RecordAiDecisionFact(AiDecisionFact fact) => GameFactEventSource.Instance.RecordAiDecision(fact);

    internal void Publish(
        string eventType,
        object data,
        string? correlationId = null,
        string? runId = null,
        string? combatId = null)
    {
        List<long> staleSubscriberIds = new();

        lock (_gate)
        {
            // Allocate the sequence while holding the same lock used for channel writes so concurrent
            // AI-decision and combat callbacks cannot appear out of sequence on the wire.
            var envelope = BuildEnvelope(eventType, data, correlationId, runId, combatId);
            foreach (var (subscriberId, channel) in _subscribers)
            {
                if (!channel.Writer.TryWrite(envelope))
                {
                    staleSubscriberIds.Add(subscriberId);
                }
            }

            foreach (var subscriberId in staleSubscriberIds)
            {
                if (_subscribers.Remove(subscriberId, out var stale))
                {
                    stale.Writer.TryComplete();
                }
            }
        }
    }

    private GameEventEnvelope BuildEnvelope(
        string eventType,
        object data,
        string? correlationId = null,
        string? runId = null,
        string? combatId = null)
    {
        var eventId = Interlocked.Increment(ref _nextEventId);
        return new GameEventEnvelope
        {
            event_id = eventId,
            sequence = eventId,
            protocol_version = ProtocolContract.Version,
            correlation_id = correlationId,
            run_id = runId,
            combat_id = combatId,
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

    public long sequence { get; init; }

    public string protocol_version { get; init; } = ProtocolContract.Version;

    public string? correlation_id { get; init; }

    public string? run_id { get; init; }

    public string? combat_id { get; init; }

    public string type { get; init; } = string.Empty;

    public string timestamp_utc { get; init; } = string.Empty;

    public object? data { get; init; }
}
