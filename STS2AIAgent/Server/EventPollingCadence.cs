namespace STS2AIAgent.Server;

/// <summary>
/// How long the SSE poll waits before its next full state build (W9). Every poll builds the whole
/// state on the game thread; while nothing changes (a menu, a paused game, a player reading a card)
/// that work buys nothing, so the delay steps up after a run of unchanged polls and snaps back the
/// moment something changes.
/// </summary>
internal sealed class EventPollingCadence
{
    public const int BaseMs = 120;

    /// <summary>Polls without a change before stepping one level slower.</summary>
    public const int UnchangedPerStep = 8;

    public static readonly int[] Steps = { 120, 250, 500, 1000 };

    private readonly object _gate = new();
    private readonly int[] _steps;
    private int _unchanged;

    public EventPollingCadence(int baseMs = BaseMs)
    {
        var first = Math.Max(1, baseMs);
        _steps = Steps.Select((step, index) => index == 0 ? first : Math.Max(step, first)).ToArray();
    }

    public int CurrentMs
    {
        get { lock (_gate) return _steps[StepIndex()]; }
    }

    public int UnchangedPolls
    {
        get { lock (_gate) return _unchanged; }
    }

    /// <summary>Records one poll and returns the delay before the next.</summary>
    public int Observe(bool changed)
    {
        lock (_gate)
        {
            _unchanged = changed ? 0 : _unchanged + 1;
            return _steps[StepIndex()];
        }
    }

    /// <summary>Back to the fastest step (an action ran, a subscriber joined).</summary>
    public void Reset()
    {
        lock (_gate) _unchanged = 0;
    }

    private int StepIndex() => Math.Min(_steps.Length - 1, _unchanged / UnchangedPerStep);
}
