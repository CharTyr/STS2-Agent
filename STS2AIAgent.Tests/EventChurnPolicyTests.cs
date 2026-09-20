using System.Text.Json;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The debug churn action exists so the slow-subscriber contract can be observed in a live run
/// instead of only offline. These pin the two rules that keep it honest: it must be able to fill a
/// queue, and it must go through the ordinary publish path rather than a private shortcut.
/// </summary>
internal static class EventChurnPolicyTests
{
    public static void DefaultCountFillsOneQueue()
    {
        Assert.True(EventChurnPolicy.TryResolveCount(null, out var count, out var error));
        Assert.Equal(EventChurnPolicy.DefaultCount, count);
        Assert.True(error == null);
        Assert.True(
            count > EventChurnPolicy.GameEventSubscriberCapacity,
            "the default must be able to overflow one subscriber queue, or the action proves nothing");
    }

    public static void CountBelowQueueCapacityIsRejected()
    {
        foreach (var requested in new[] { 0, 1, 255, EventChurnPolicy.GameEventSubscriberCapacity })
        {
            Assert.False(
                EventChurnPolicy.TryResolveCount(requested, out _, out var error),
                $"count {requested} cannot fill a {EventChurnPolicy.GameEventSubscriberCapacity}-slot queue");
            Assert.True(!string.IsNullOrWhiteSpace(error));
        }
    }

    public static void CountAboveTheCeilingIsRejected()
    {
        Assert.False(EventChurnPolicy.TryResolveCount(EventChurnPolicy.MaxCount + 1, out _, out var error));
        Assert.True(!string.IsNullOrWhiteSpace(error));
        Assert.True(EventChurnPolicy.TryResolveCount(EventChurnPolicy.MaxCount, out var count, out _));
        Assert.Equal(EventChurnPolicy.MaxCount, count);
    }

    public static void EventsAreSyntheticNumberedAndInOrder()
    {
        var events = EventChurnPolicy.BuildEvents("run_1", "MAIN_MENU", 3).ToList();
        Assert.Equal(3, events.Count);

        for (var index = 0; index < events.Count; index++)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(events[index]));
            var root = document.RootElement;
            Assert.True(root.GetProperty("synthetic").GetBoolean(), "a client must be able to tell these apart");
            Assert.Equal(index + 1, root.GetProperty("index").GetInt32());
            Assert.Equal(3, root.GetProperty("total").GetInt32());
            Assert.Equal("run_1", root.GetProperty("run_id").GetString());
            Assert.Equal("MAIN_MENU", root.GetProperty("screen").GetString());
        }
    }

    /// <summary>
    /// Every synthetic event has to be distinct. The publish path drops an event whose payload equals
    /// the previous one, so a constant payload would publish once and fill nothing.
    /// </summary>
    public static void EveryEventPayloadIsDistinct()
    {
        var signatures = EventChurnPolicy.BuildEvents("run_1", "MAIN_MENU", EventChurnPolicy.DefaultCount)
            .Select(payload => JsonSerializer.Serialize(payload))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(EventChurnPolicy.DefaultCount, signatures.Count);

        var guard = new ConsecutiveRepeatSuppressor();
        foreach (var payload in EventChurnPolicy.BuildEvents("run_1", "MAIN_MENU", 3))
        {
            Assert.True(guard.ShouldPublish(EventChurnPolicy.EventType + "\n" + JsonSerializer.Serialize(payload)));
        }
    }

    /// <summary>
    /// The action must be reachable only when debug actions are on, must require a count that can
    /// fill a queue, and must publish through the event service's own path.
    /// </summary>
    public static void ActionIsDebugGatedAndUsesTheRealPublishPath()
    {
        var menus = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.Menus.cs");
        var handler = AgentSourceFixture.MethodBody(menus, "ExecuteInjectEventChurnAsync");

        Assert.Contains("if (!AreDebugActionsEnabled())", handler, StringComparison.Ordinal);
        Assert.Contains("EventChurnPolicy.TryResolveCount(request.option_index", handler, StringComparison.Ordinal);
        Assert.Contains("GameEventService.Instance.PublishDebugChurn(", handler, StringComparison.Ordinal);
        Assert.Contains("ApiException(400, \"invalid_request\"", handler, StringComparison.Ordinal);
        Assert.Contains("ApiException(409, \"invalid_action\"", handler, StringComparison.Ordinal);

        var dispatch = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        Assert.Contains("\"inject_event_churn\" => ExecuteInjectEventChurnAsync(request),", dispatch, StringComparison.Ordinal);

        // The publish helper is the one the poll loop uses, so the churn cannot drift from it.
        var service = AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs");
        var churn = AgentSourceFixture.MethodBody(service, "PublishDebugChurn");
        Assert.Contains("Publish(EventChurnPolicy.EventType, payload)", churn, StringComparison.Ordinal);
    }
}
