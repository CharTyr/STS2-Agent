namespace STS2AIAgent.Server;

/// <summary>
/// Owns the single poll loop behind the event stream: one loop per <see cref="Start"/> lifecycle,
/// and a generation fence so a result that was already in flight when the lifecycle ended can never
/// be committed afterwards.
/// </summary>
/// <remarks>
/// Two properties are load-bearing and are why this is not a bare <c>Task.Run</c>:
/// <list type="bullet">
/// <item>The loop is demand-driven. With no subscribers it waits for a signal instead of polling,
/// so a mod with nobody on <c>/events/stream</c> does not build a full state payload on the game
/// thread every interval.</item>
/// <item>Stop is final. The previous generation is cancelled, awaited, and only then forgotten; if
/// it refuses to end within the shutdown budget, the coordinator stays stopped rather than letting
/// <see cref="Start"/> put a second loop next to the one still running.</item>
/// </list>
/// </remarks>
internal sealed class EventPollingCoordinator
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _pollOnce;
    private readonly TimeSpan _pollInterval;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private TaskCompletionSource<bool> _demandSignal = NewSignal();
    private int _subscriberCount;
    private long _generation;
    private bool _running;
    private bool _stopped;
    private int _loopStartCount;

    public EventPollingCoordinator(Func<CancellationToken, Task> pollOnce, TimeSpan pollInterval)
    {
        _pollOnce = pollOnce;
        _pollInterval = pollInterval;
    }

    internal int LoopStartCount => Volatile.Read(ref _loopStartCount);

    /// <summary>
    /// The generation a caller captured at the start of a poll, or <c>0</c> before any lifecycle
    /// has started. A result is only allowed to commit while this still matches
    /// <see cref="CurrentGeneration"/>, which is what stops a pre-Stop poll from writing state
    /// after the lifecycle it belonged to has ended.
    /// </summary>
    public long CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public bool IsGenerationCurrent(long generation)
    {
        lock (_gate)
        {
            return _running && _generation == generation;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_stopped || _loopTask != null)
            {
                return;
            }

            _generation++;
            _running = true;
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;
            _loopTask = Task.Run(() => RunAsync(cancellationToken));
            Interlocked.Increment(ref _loopStartCount);
            if (_subscriberCount > 0)
            {
                _demandSignal.TrySetResult(true);
            }
        }
    }

    public void SetSubscriberCount(int subscriberCount)
    {
        lock (_gate)
        {
            _subscriberCount = Math.Max(0, subscriberCount);
            if (_subscriberCount > 0)
            {
                _demandSignal.TrySetResult(true);
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;
        lock (_gate)
        {
            // The generation moves on before anything is cancelled, so a poll that is already past
            // its cancellation check can no longer commit: it captured the previous generation.
            _generation++;
            _running = false;
            cts = _cts;
            loopTask = _loopTask;
            _cts = null;
            _subscriberCount = 0;
            _demandSignal.TrySetResult(false);
            _demandSignal = NewSignal();
        }

        cts?.Cancel();

        var drained = true;
        if (loopTask != null)
        {
            try
            {
                drained = loopTask.Wait(ShutdownBudget);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException or OperationCanceledException))
            {
            }
        }

        lock (_gate)
        {
            if (drained)
            {
                _loopTask = null;
                cts?.Dispose();
            }
            else
            {
                // The poll did not honour cancellation inside the budget. Keep it as the live loop
                // so Start cannot add a second one, and keep its CTS alive: disposing a source a
                // running task still registers against is what turns a slow shutdown into an
                // ObjectDisposedException on the game thread.
                _stopped = true;
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (await WaitForDemandAsync(cancellationToken))
        {
            await _pollOnce(cancellationToken);

            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> WaitForDemandAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Task<bool> signal;
            lock (_gate)
            {
                if (_subscriberCount > 0)
                {
                    return true;
                }

                signal = _demandSignal.Task;
            }

            bool signaled;
            try
            {
                signaled = await signal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            lock (_gate)
            {
                if (ReferenceEquals(signal, _demandSignal.Task))
                {
                    _demandSignal = NewSignal();
                }
            }

            if (!signaled)
            {
                return false;
            }
        }

        return false;
    }

    private static TaskCompletionSource<bool> NewSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
