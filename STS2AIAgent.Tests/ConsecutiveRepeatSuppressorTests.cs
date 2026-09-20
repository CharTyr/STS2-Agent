using System.Text.Json;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The 2026-09-20 live run caught the poll loop re-broadcasting an unchanged event every tick: a
/// client on a stationary screen received one frame per 120 ms poll. These pin the guard that
/// stopped it.
/// </summary>
internal static class ConsecutiveRepeatSuppressorTests
{
    public static void ImmediateRepeatIsSuppressed()
    {
        var guard = new ConsecutiveRepeatSuppressor();
        Assert.True(guard.ShouldPublish("screen_changed|{\"to\":\"MAP\"}"));
        Assert.False(guard.ShouldPublish("screen_changed|{\"to\":\"MAP\"}"));
        Assert.False(guard.ShouldPublish("screen_changed|{\"to\":\"MAP\"}"));
    }

    public static void DifferentEventOrPayloadIsPublished()
    {
        var guard = new ConsecutiveRepeatSuppressor();
        Assert.True(guard.ShouldPublish("screen_changed|{\"to\":\"MAP\"}"));
        // Same event, changed payload.
        Assert.True(guard.ShouldPublish("screen_changed|{\"to\":\"COMBAT\"}"));
        // Different event, same payload shape.
        Assert.True(guard.ShouldPublish("combat_started|{\"to\":\"COMBAT\"}"));
    }

    /// <summary>
    /// A state that returns after something else happened in between is news to the client, so it
    /// must be published again -- this is why the guard compares only with the previous event.
    /// </summary>
    public static void ReturnToAPreviousStateIsPublishedAgain()
    {
        var guard = new ConsecutiveRepeatSuppressor();
        Assert.True(guard.ShouldPublish("available_actions_changed|[play_card]"));
        Assert.True(guard.ShouldPublish("available_actions_changed|[]"));
        Assert.True(guard.ShouldPublish("available_actions_changed|[play_card]"));
    }

    public static void ResetForcesTheNextEventThrough()
    {
        var guard = new ConsecutiveRepeatSuppressor();
        Assert.True(guard.ShouldPublish("same"));
        Assert.False(guard.ShouldPublish("same"));
        guard.Reset();
        Assert.True(guard.ShouldPublish("same"));
    }

    /// <summary>
    /// The first sample of a lifecycle may announce the snapshot once -- a subscriber that connected
    /// before any sample existed has nothing to orient on -- but the steady-state path must only
    /// record it. Re-announcing there is what produced one frame per 120 ms poll on 2026-09-20.
    /// </summary>
    public static void SteadyStatePollRecordsTheSnapshotWithoutAnnouncingIt()
    {
        var state = AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs");
        var polled = AgentSourceFixture.MethodBody(state, "ProcessStateLocked");

        var firstSample = polled.IndexOf("if (previous == null)", StringComparison.Ordinal);
        Assert.True(firstSample >= 0, "ProcessStateLocked must keep its first-sample branch.");

        var announced = polled.IndexOf("PublishSnapshot(\"stream_ready\"", StringComparison.Ordinal);
        Assert.True(announced > firstSample, "the first sample announces the snapshot");
        Assert.Equal(
            -1,
            polled.IndexOf("PublishSnapshot(\"stream_ready\"", announced + 1, StringComparison.Ordinal));
        Assert.True(
            polled.IndexOf("_subscribers.RecordSnapshot(current)", StringComparison.Ordinal) > announced,
            "every later poll must record the snapshot instead of publishing it again");

        var subscribe = AgentSourceFixture.MethodBody(state, "Subscribe");
        Assert.Contains("stream_ready", subscribe, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reader loop must stay single-reader and bounded: one <c>WaitToReadAsync</c> per cycle,
    /// a heartbeat that reuses a cancellation source instead of racing a timer, and no
    /// <c>Task.WhenAny</c> that abandons a pending read. The 2026-09-20 live run measured the
    /// disconnect it depends on -- polling stopped 33 s after the client closed, which is two
    /// 15 s heartbeats, so shortening the heartbeat is a deliberate decision, not a side effect.
    /// </summary>
    public static void EventStreamReaderLoopStaysBounded()
    {
        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var loop = AgentSourceFixture.MethodBody(router, "HandleEventStreamAsync");

        Assert.Contains("WaitToReadAsync(heartbeatCts.Token)", loop, StringComparison.Ordinal);
        Assert.Contains("heartbeatCts.CancelAfter(TimeSpan.FromSeconds(15))", loop, StringComparison.Ordinal);
        Assert.False(
            loop.Contains("Task.WhenAny", StringComparison.Ordinal),
            "WhenAny left the reader task pending and stacked readers on a SingleReader channel");
        Assert.Contains("subscription.Reader.TryRead(out var envelope)", loop, StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// A suppressed repeat must not consume an event id either. Allocating the envelope first left
    /// gaps in an otherwise lossless stream, which is exactly the signal the docs say means events
    /// were dropped.
    /// </summary>
    public static void SuppressedRepeatsDoNotConsumeEventIds()
    {
        var service = AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs");
        var publish = AgentSourceFixture.MethodBody(service, "Publish");

        var guard = publish.IndexOf("_repeatGuard.ShouldPublish", StringComparison.Ordinal);
        var build = publish.IndexOf("BuildEnvelope(eventType, data)", StringComparison.Ordinal);
        Assert.True(guard > 0, "Publish must consult the repeat guard.");
        Assert.True(build > guard, "the envelope (and its id) must be built only after the guard passes");
    }

    /// <summary>
    /// The guard compares serialized payloads, so two identical payloads must serialize identically;
    /// otherwise suppression would silently stop working.
    /// </summary>
    public static void SignatureIsStableForIdenticalPayloads()
    {
        var first = JsonSerializer.Serialize(new { screen = "MAP", actions = new[] { "a", "b" } }, SerializerOptions);
        var second = JsonSerializer.Serialize(new { screen = "MAP", actions = new[] { "a", "b" } }, SerializerOptions);
        Assert.Equal(first, second);

        var guard = new ConsecutiveRepeatSuppressor();
        Assert.True(guard.ShouldPublish("screen_changed\n" + first));
        Assert.False(guard.ShouldPublish("screen_changed\n" + second));
    }
}
