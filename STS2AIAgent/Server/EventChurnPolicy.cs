
namespace STS2AIAgent.Server;

/// <summary>
/// Builds the events the debug churn action publishes, and validates its request.
/// </summary>
/// <remarks>
/// This exists so a live run can exercise the slow-subscriber contract without waiting for the game
/// to produce 256 real state changes. On 2026-09-20 that wait turned out to be the reason the
/// overflow path had no in-game evidence: the poll loop only publishes when a digest field actually
/// changes, an idle client therefore never falls behind, and the debug console that could have
/// driven the changes answers 409 until a run exists. The events below go through the same
/// publishing path as every other event, so what they test is the real deliverability rules -- a
/// full queue must fail the write and drop that subscriber, not evict the oldest event.
///
/// The count is required to exceed <see cref="GameEventSubscriberCapacity"/> so the action cannot be
/// quietly useless: a subscriber with a queue that cannot fill proves nothing.
/// </remarks>
internal static class EventChurnPolicy
{
    /// <summary>The event type these events are published as. Documented as debug-only.</summary>
    public const string EventType = "debug_churn";

    /// <summary>Must match the per-subscriber queue capacity the event service creates.</summary>
    public const int GameEventSubscriberCapacity = 256;

    public const int DefaultCount = 300;

    public const int MaxCount = 5000;

    /// <summary>
    /// The number of events to publish, or an error for the caller to raise.
    /// </summary>
    public static bool TryResolveCount(int? requested, out int count, out string? error)
    {
        var value = requested ?? DefaultCount;
        if (value < GameEventSubscriberCapacity + 1)
        {
            count = 0;
            error = $"count must be at least {GameEventSubscriberCapacity + 1} to fill one subscriber "
                + $"queue (capacity {GameEventSubscriberCapacity}), so nothing below that proves anything.";
            return false;
        }

        if (value > MaxCount)
        {
            count = 0;
            error = $"count must be at most {MaxCount}.";
            return false;
        }

        count = value;
        error = null;
        return true;
    }

    /// <summary>
    /// The synthetic events, in order. Each carries <c>synthetic: true</c> and its 1-based index so a
    /// reader can tell them apart from real game events and see which ones arrived. It takes the two
    /// state fields it copies rather than the whole payload, so this stays testable without the game
    /// assemblies.
    /// </summary>
    public static IEnumerable<object> BuildEvents(string runId, string screen, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            yield return new
            {
                synthetic = true,
                index,
                total = count,
                screen,
                run_id = runId
            };
        }
    }
}
