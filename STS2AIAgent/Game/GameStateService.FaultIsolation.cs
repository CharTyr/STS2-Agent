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
            SectionFaults.RecordSuccess(section);
            return value;
        }
        catch (Exception ex)
        {
            if (SectionFaults.RecordFault(section, ex, DateTimeOffset.UtcNow))
            {
                Log.Warn($"[STS2AIAgent] State section '{section}' failed ({ex.GetType().Name}): {ex.Message}");
            }

            return null;
        }
    }
}
