namespace STS2AIAgent.Agent;

/// <summary>
/// The single current <see cref="PlayStrategy"/> the decider reads, safe to update from a planner
/// thread while a decider thread reads it.
/// </summary>
/// <remarks>
/// The planner writes at its own pace and Jev reads on every frame, so the current strategy is shared
/// mutable state crossed by two threads. A lock makes the swap atomic: a decider never observes a
/// half-written strategy, and a reader never blocks the planner. It starts at
/// <see cref="PlayStrategy.Default"/> so Jev can act before the first plan has landed, and a null
/// update is treated as "reset to default" rather than stored as a null the decider would trip on.
/// </remarks>
internal sealed class StrategyStore
{
    private readonly object _gate = new();
    private PlayStrategy _current = PlayStrategy.Default;

    /// <summary>A consistent snapshot of the current strategy.</summary>
    public PlayStrategy Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Replace the current strategy; a null resets it to <see cref="PlayStrategy.Default"/>.</summary>
    public void Update(PlayStrategy strategy)
    {
        lock (_gate)
        {
            _current = strategy ?? PlayStrategy.Default;
        }
    }
}
