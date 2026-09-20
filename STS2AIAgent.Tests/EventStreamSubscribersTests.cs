using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The subscriber ordering rules that used to race in <c>GameEventService</c>, exercised through
/// the game-independent session the service now delegates to.
/// </summary>
internal static class EventStreamSubscribersTests
{
    private sealed class Snapshot(string runId)
    {
        public string RunId { get; } = runId;
    }

    private sealed class Frame(string runId, string type)
    {
        public string RunId { get; } = runId;

        public string Type { get; } = type;
    }

    private static EventStreamSubscribers<Frame, Snapshot> NewSession() => new(4);

    private static Frame FrameOf(Snapshot snapshot, string type) => new(snapshot.RunId, type);

    /// <summary>
    /// A subscriber arriving while a snapshot exists must read that snapshot before anything a
    /// publisher writes afterwards -- the ordering the old implementation lost by writing the
    /// initial frame outside the lock.
    /// </summary>
    public static void SubscribeAfterSnapshotKeepsInitialFrameFirst()
    {
        var session = NewSession();
        session.PublishSnapshot(new Snapshot("run_1"), new Frame("run_1", "session_started"));

        var lease = session.Subscribe(session.Snapshot, snapshot => FrameOf(snapshot, "stream_ready"));
        session.Publish(new Frame("run_1", "screen_changed"));

        Assert.True(lease.Reader.TryRead(out var first));
        Assert.True(lease.Reader.TryRead(out var second));
        Assert.Equal("stream_ready", first!.Type);
        Assert.Equal("screen_changed", second!.Type);
    }

    /// <summary>
    /// After the last subscriber leaves there is no snapshot, so the next connection behaves like a
    /// fresh lifecycle instead of receiving an idle-period snapshot as its first frame.
    /// </summary>
    public static void SnapshotIsDroppedWhenTheLastSubscriberLeaves()
    {
        var session = NewSession();
        session.PublishSnapshot(new Snapshot("run_1"), new Frame("run_1", "session_started"));

        var first = session.Subscribe(session.Snapshot, snapshot => FrameOf(snapshot, "stream_ready"));
        Assert.NotNull(session.Snapshot);
        Assert.True(session.Unsubscribe(first.Id));
        Assert.Null(session.Snapshot);
        Assert.Equal(0, session.Count);

        var second = session.Subscribe(session.Snapshot, snapshot => FrameOf(snapshot, "stream_ready"));
        Assert.False(second.Reader.TryRead(out _));

        // The first sample of the new lifecycle is what sends stream_ready, after session_started.
        session.Publish(new Frame("run_2", "session_started"));
        session.PublishSnapshot(new Snapshot("run_2"), new Frame("run_2", "stream_ready"));

        Assert.True(second.Reader.TryRead(out var started));
        Assert.True(second.Reader.TryRead(out var ready));
        Assert.Equal("session_started", started!.Type);
        Assert.Equal("stream_ready", ready!.Type);
    }

    /// <summary>
    /// One subscriber leaving must not disturb the others: they keep their queue, their order, and
    /// the shared snapshot.
    /// </summary>
    public static void PublishSnapshotReachesEverySubscriber()
    {
        var session = NewSession();
        var first = session.Subscribe(session.Snapshot, snapshot => FrameOf(snapshot, "stream_ready"));
        var second = session.Subscribe(session.Snapshot, snapshot => FrameOf(snapshot, "stream_ready"));

        session.PublishSnapshot(new Snapshot("run_1"), new Frame("run_1", "stream_ready"));
        Assert.True(session.Unsubscribe(first.Id));
        Assert.NotNull(session.Snapshot);

        session.Publish(new Frame("run_1", "combat_started"));

        Assert.True(second.Reader.TryRead(out var ready));
        Assert.True(second.Reader.TryRead(out var combat));
        Assert.Equal("stream_ready", ready!.Type);
        Assert.Equal("combat_started", combat!.Type);
    }
}
