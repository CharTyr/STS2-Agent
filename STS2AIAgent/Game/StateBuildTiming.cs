namespace STS2AIAgent.Game;

/// <summary>How long building the state payload takes on the game thread.</summary>
/// <remarks>
/// Every <c>/state</c>, every action response and every SSE refresh builds the full payload on the
/// game thread, and the game does not draw a frame while that runs. The request log recorded the
/// whole request -- queueing for the game thread included -- at Info, next to every other request,
/// with no threshold: a build that froze the game for a second looked like any other line.
///
/// This measures the build alone, warns when one is slow, and keeps a summary for
/// <c>GET /health</c>. It holds no game types, so the offline suite compiles and tests it directly.
///
/// The threshold is reasoned, not measured: at 60 frames per second a frame is 16.7 ms, and 100 ms is
/// six dropped frames, a hitch a player sees. The logs on hand held nine <c>/state</c> requests,
/// too few to set a percentile by; the summary below is what will.
/// </remarks>
internal sealed class StateBuildTiming
{
    internal const double SlowThresholdMs = 100;
    internal const int RecentWindow = 256;

    /// <summary>
    /// A game stuck in a slow state builds slowly on every poll. One warning per interval, carrying
    /// the count it held back, says so without burying the rest of the log.
    /// </summary>
    internal static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(30);

    public static StateBuildTiming Instance { get; } = new();

    private readonly object _gate = new();
    private readonly double[] _recent = new double[RecentWindow];
    private int _recentCount;
    private int _recentNext;
    private long _samples;
    private long _slowBuilds;
    private double _lastMs;
    private double _maxMs;
    private string? _maxScreen;
    private DateTime? _lastWarningAt;
    private long _heldBackWarnings;

    /// <summary>Records one build.</summary>
    /// <returns>A warning to log, or null when the build was fast or a warning went out recently.</returns>
    public string? Record(double elapsedMs, string? screen, DateTime utcNow)
    {
        lock (_gate)
        {
            _samples++;
            _lastMs = elapsedMs;
            if (elapsedMs > _maxMs)
            {
                _maxMs = elapsedMs;
                _maxScreen = screen;
            }

            _recent[_recentNext] = elapsedMs;
            _recentNext = (_recentNext + 1) % RecentWindow;
            _recentCount = Math.Min(_recentCount + 1, RecentWindow);

            if (elapsedMs <= SlowThresholdMs)
            {
                return null;
            }

            _slowBuilds++;
            if (_lastWarningAt is { } last && utcNow - last < WarningInterval)
            {
                _heldBackWarnings++;
                return null;
            }

            var heldBack = _heldBackWarnings;
            _heldBackWarnings = 0;
            _lastWarningAt = utcNow;
            return $"State build took {elapsedMs:0} ms on screen {screen ?? "unknown"}, over the {SlowThresholdMs:0} ms "
                + "the game can spend without a visible hitch"
                + (heldBack > 0 ? $"; {heldBack} more slow build(s) since the last warning." : ".");
        }
    }

    /// <summary>The <c>state_build</c> block of <c>GET /health</c>.</summary>
    public object Snapshot()
    {
        lock (_gate)
        {
            var recent = _recent.Take(_recentCount).OrderBy(ms => ms).ToArray();
            return new
            {
                slow_threshold_ms = SlowThresholdMs,
                samples = _samples,
                slow_builds = _slowBuilds,
                last_ms = _samples == 0 ? (double?)null : Math.Round(_lastMs, 1),
                max_ms = _samples == 0 ? (double?)null : Math.Round(_maxMs, 1),
                max_screen = _maxScreen,
                recent_samples = recent.Length,
                recent_p50_ms = Percentile(recent, 0.50),
                recent_p95_ms = Percentile(recent, 0.95),
            };
        }
    }

    /// <summary>Nearest-rank percentile of an ascending array; null when it is empty.</summary>
    internal static double? Percentile(double[] ascending, double fraction)
    {
        if (ascending.Length == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(fraction * ascending.Length);
        return Math.Round(ascending[Math.Clamp(rank, 1, ascending.Length) - 1], 1);
    }
}
