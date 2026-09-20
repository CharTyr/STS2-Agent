namespace STS2AIAgent.Server;

/// <summary>
/// The subscriber bookkeeping for <c>/events/stream</c>: who is connected, and what the last
/// sampled state was. It is deliberately free of Godot and game types so the ordering rules around
/// subscribing can be exercised offline.
/// </summary>
/// <remarks>
/// Two rules live here because both were races when they lived in the service:
/// <list type="number">
/// <item>A subscriber that arrives while a snapshot exists is written that snapshot <em>before</em>
/// it is visible to publishers, so no <c>screen_changed</c> can overtake the initial
/// <c>stream_ready</c>.</item>
/// <item>The snapshot is dropped when the last subscriber leaves. Keeping it would let a connection
/// arriving after a long idle period receive a stale snapshot as its first frame, which is exactly
/// what the demand-driven loop stopped building.</item>
/// </list>
/// </remarks>
internal sealed class EventStreamSubscribers<TEnvelope, TSnapshot>
    where TSnapshot : class
{
    private readonly object _gate = new();
    private readonly GameEventSubscriberHub<TEnvelope> _hub;

    public EventStreamSubscribers(int capacity)
    {
        _hub = new GameEventSubscriberHub<TEnvelope>(capacity);
    }

    /// <summary>The last sampled snapshot, or <c>null</c> when there is no lifecycle in progress.</summary>
    public TSnapshot? Snapshot { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _hub.Count;
            }
        }
    }

    public SubscriberLease Subscribe(TSnapshot? snapshot, Func<TSnapshot, TEnvelope> snapshotFrame)
    {
        lock (_gate)
        {
            var subscription = _hub.Subscribe();
            if (snapshot != null)
            {
                subscription.Writer.TryWrite(snapshotFrame(snapshot));
            }

            return new SubscriberLease(subscription.Id, subscription.Reader);
        }
    }

    public bool Unsubscribe(long subscriberId)
    {
        lock (_gate)
        {
            if (!_hub.Unsubscribe(subscriberId))
            {
                return false;
            }

            if (_hub.Count == 0)
            {
                Snapshot = null;
            }

            return true;
        }
    }

    /// <summary>Stores the newest snapshot and delivers it to every current subscriber.</summary>
    public void PublishSnapshot(TSnapshot snapshot, TEnvelope frame)
    {
        lock (_gate)
        {
            Snapshot = snapshot;
            _hub.Publish(frame);
        }
    }

    /// <summary>
    /// Stores the newest snapshot without sending it. Every poll updates what the next subscriber
    /// will be handed; only the first sample of a lifecycle announces it, and only to the clients
    /// already attached.
    /// </summary>
    public void RecordSnapshot(TSnapshot snapshot)
    {
        lock (_gate)
        {
            Snapshot = snapshot;
        }
    }

    public int Publish(TEnvelope envelope)
    {
        lock (_gate)
        {
            return _hub.Publish(envelope);
        }
    }

    public void CompleteAll()
    {
        lock (_gate)
        {
            Snapshot = null;
            _hub.CompleteAll();
        }
    }

    internal readonly record struct SubscriberLease(
        long Id,
        System.Threading.Channels.ChannelReader<TEnvelope> Reader);
}
