using System.Diagnostics;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// Enforces an optional minimum wall-clock interval between consecutive primary play-model requests (#234).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only primary LLM play turns pass through <see cref="WaitBeforePrimaryRequestAsync"/>; zero-request
/// <c>Autopilot</c> moves and whole-turn plan <c>then</c> follow-up steps run without delay.</item>
/// <item>Measures monotonic time since the previous primary dispatch so LLM inference and card-animation
/// settle time already elapsed counts toward the interval rather than stacking extra sleep on top.</item>
/// <item>Cancellation aborts any pending wait immediately without stamping a phantom dispatch.</item>
/// </list>
/// </remarks>
internal sealed class DecisionPacingGate
{
    private readonly Func<TimeSpan> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _sync = new();
    private TimeSpan? _lastPrimaryDispatchAt;

    public DecisionPacingGate(
        Func<TimeSpan>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _clock = clock ?? DefaultClock;
        _delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    private static TimeSpan DefaultClock() =>
        TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    /// <summary>
    /// Waits any remaining interval since the previous primary LLM dispatch and records the new dispatch timestamp.
    /// Returns the actual duration waited (<see cref="TimeSpan.Zero"/> when disabled or already elapsed).
    /// </summary>
    public async Task<TimeSpan> WaitBeforePrimaryRequestAsync(
        double? minIntervalSeconds,
        CancellationToken cancellationToken,
        bool stampOnReturn = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (minIntervalSeconds is not { } seconds || !double.IsFinite(seconds) || seconds <= 0)
        {
            if (stampOnReturn) RecordDispatch();
            return TimeSpan.Zero;
        }

        var clampedSeconds = Math.Min(seconds, AgentSettings.MaxDecisionIntervalSeconds);
        var targetInterval = TimeSpan.FromSeconds(clampedSeconds);
        TimeSpan remaining;
        lock (_sync)
        {
            var now = _clock();
            remaining = _lastPrimaryDispatchAt is { } previous
                ? targetInterval - (now - previous)
                : TimeSpan.Zero;
        }

        if (remaining > TimeSpan.Zero)
        {
            await _delay(remaining, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (stampOnReturn) RecordDispatch();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void RecordDispatch()
    {
        lock (_sync)
        {
            _lastPrimaryDispatchAt = _clock();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _lastPrimaryDispatchAt = null;
        }
    }
}
