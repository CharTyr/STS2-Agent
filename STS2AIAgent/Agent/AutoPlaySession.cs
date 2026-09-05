namespace STS2AIAgent.Agent;

internal sealed class AutoPlaySession
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;
    private bool _requested;

    public string Phase
    {
        get { lock (_gate) return _task.IsCompleted ? "paused" : _requested ? "running" : "stopping"; }
    }

    public bool IsActive => Phase != "paused";

    public Task? TryStart(Func<CancellationToken, Task> run, CancellationToken lifetime)
    {
        lock (_gate)
        {
            if (!_task.IsCompleted || lifetime.IsCancellationRequested) return null;
            _cancellation?.Dispose();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _cancellation = cancellation;
            _requested = true;
            _task = Task.Run(async () =>
            {
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await run(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            });
            return _task;
        }
    }

    public Task RequestPause()
    {
        Task task;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _requested = false;
            cancellation = _cancellation;
            task = _task;
        }
        // Cancellation can invoke arbitrary callbacks. Never run them under
        // the lifecycle lock; a completed session may already be disposed.
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        return task;
    }
}
