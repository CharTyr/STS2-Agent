using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Game;

/// <summary>
/// Per-section fault isolation for <see cref="GameStateService.BuildStatePayload"/>. An upstream
/// game patch that breaks one room or overlay reader degrades only that nullable sub-section while
/// recording the failure in <see cref="SectionFaults"/> (surfaced on <c>GET /health.compatibility</c>)
/// and emitting a deduplicated warning to the game log.
/// </summary>
internal static partial class GameStateService
{
    internal static StateSectionFaultTracker SectionFaults { get; } = new();

    private static T? GuardStateSection<T>(string section, Func<T?> build) where T : class
    {
        try
        {
            var value = build();
            // Only clear an active fault once this section actually builds a live payload again;
            // early-returning null on unrelated screens must not mask a still-broken screen reader.
            if (value != null)
            {
                SectionFaults.RecordSuccess(section);
            }

            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            if (SectionFaults.RecordFault(section, ex, DateTimeOffset.UtcNow))
            {
                Log.Warn($"[STS2AIAgent] State section '{section}' failed ({ex.GetType().Name}): {ex.Message}");
            }

            return null;
        }
    }
}
