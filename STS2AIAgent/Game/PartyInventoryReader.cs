namespace STS2AIAgent.Game;

/// <summary>
/// Reads one part of a player's inventory (deck, relics, potions) for <c>run.players[]</c>.
/// </summary>
/// <remarks>
/// A read that throws returns null rather than an empty array, so a client can tell "could not be
/// read" from "holds nothing". The failure is logged at most once per interval per part, carrying
/// the count it held back, because a broken read fails on every state build. Holds no game types,
/// so the offline suite tests it directly.
/// </remarks>
internal sealed class PartyInventoryReader
{
    internal static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(30);

    public static PartyInventoryReader Instance { get; } = new();

    private readonly object _gate = new();

    private readonly Dictionary<string, (DateTime At, long HeldBack)> _warnings = new(StringComparer.Ordinal);

    public TPayload[]? Read<TItem, TPayload>(
        string part,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TPayload> map,
        DateTime utcNow,
        Action<string> warn)
    {
        try
        {
            return source().Select(map).ToArray();
        }
        catch (Exception ex)
        {
            var message = Throttle(part, ex, utcNow);
            if (message != null)
            {
                warn(message);
            }

            return null;
        }
    }

    /// <returns>A warning to log, or null when one for this part went out recently.</returns>
    internal string? Throttle(string part, Exception failure, DateTime utcNow)
    {
        lock (_gate)
        {
            if (_warnings.TryGetValue(part, out var last) && utcNow - last.At < WarningInterval)
            {
                _warnings[part] = (last.At, last.HeldBack + 1);
                return null;
            }

            var heldBack = _warnings.TryGetValue(part, out var previous) ? previous.HeldBack : 0;
            _warnings[part] = (utcNow, 0);
            return $"Could not read a player's {part} ({failure.GetType().Name}: {failure.Message}); "
                + $"run.players[].{part} is null for that player"
                + (heldBack > 0 ? $"; {heldBack} more failure(s) since the last warning." : ".");
        }
    }
}
