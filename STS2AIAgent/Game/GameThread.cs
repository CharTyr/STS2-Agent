using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using Environment = System.Environment;

namespace STS2AIAgent.Game;

internal static class GameThread
{
    private const string LogPrefix = "[STS2AIAgent.GameThread]";

    private static readonly object Gate = new();

    private static SynchronizationContext? _syncContext;
    private static int _threadId;

    public static void Initialize()
    {
        lock (Gate)
        {
            _syncContext = SynchronizationContext.Current;
            _threadId = Environment.CurrentManagedThreadId;

            if (_syncContext == null)
            {
                Log.Error($"{LogPrefix} Failed to capture SynchronizationContext.");
                return;
            }

            Log.Info($"{LogPrefix} Captured game thread context on managed thread {_threadId}");
        }
    }

    public static Task<T> InvokeAsync<T>(Func<T> action)
    {
        if (_syncContext == null)
        {
            throw new InvalidOperationException("Game thread context has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return Task.FromResult(action());
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            try
            {
                if (!completionSource.TrySetResult(action()))
                {
                    Log.Warn($"{LogPrefix} InvokeAsync completion source was already completed.");
                }
            }
            catch (Exception ex)
            {
                if (!completionSource.TrySetException(ex))
                {
                    Log.Warn($"{LogPrefix} Failed to propagate InvokeAsync exception because the completion source was already completed: {ex}");
                }
            }
        }, null);

        return completionSource.Task;
    }

    /// <summary>
    /// <see cref="InvokeAsync{T}(Func{T})"/> with a deadline and a caller token. A posted callback
    /// that never runs (the game thread is not pumping, or a synchronous long task is ahead of it)
    /// used to hang the awaiting agent turn forever -- the caller's cancellation token never reached
    /// the post, so even pausing auto-play could not break the wait. Here the await completes with
    /// <see cref="TimeoutException"/> or <see cref="OperationCanceledException"/> instead; the late
    /// callback's result is discarded by the already-completed source.
    /// </summary>
    public static async Task<T> InvokeAsync<T>(Func<T> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var task = InvokeAsync(action);
        if (task.IsCompleted)
        {
            return await task;
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        // Task.Delay with a token completes (canceled) the moment either fires; WhenAny treats a
        // canceled task as completed, so the first finisher decides. No ConfigureAwait(false): this
        // class's contract is to keep callers on the context they came from, because game-adjacent
        // continuations break on a thread-pool thread.
        var wait = Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
        var completed = await Task.WhenAny(task, wait);
        if (completed == task)
        {
            return await task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new TimeoutException("The game thread did not run the posted action in time.");
    }

    /// <summary>Cancellation-aware overload without an explicit timeout: the caller's token breaks the wait.</summary>
    public static Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        // No clock of its own: the caller's token is the whole deadline, so this is the timeout
        // overload with an unbounded clock rather than a second copy of the wait loop.
        return InvokeAsync(action, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static Task InvokeAsync(Action action)
    {
        return InvokeAsync(() =>
        {
            action();
            return true;
        });
    }

    public static Task InvokeAsync(Func<Task> action)
    {
        return InvokeAsync(async () =>
        {
            await action();
            return true;
        });
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        if (_syncContext == null)
        {
            throw new InvalidOperationException("Game thread context has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return action();
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ => _ = InvokeAsyncCoreAsync(action, completionSource), null);

        return completionSource.Task;
    }

    /// <summary>
    /// <see cref="InvokeAsync{T}(Func{Task{T}})"/> that the caller can abandon -- by its token or
    /// <paramref name="startTimeout"/> -- only while the work is still queued. A game thread that
    /// stops pumping used to leave the agent's act/screenshot awaits hanging with pause unable to break
    /// them. Abandonment is claimed atomically against the start, so an abandoned callback never runs
    /// later (no action lands after the player paused), and work that already started is seen through:
    /// a half-executed action is bounded by its own waits and must not be reported as not done.
    /// </summary>
    public static async Task<T> InvokeAsync<T>(Func<Task<T>> action, TimeSpan startTimeout, CancellationToken cancellationToken)
    {
        if (_syncContext == null)
        {
            throw new InvalidOperationException("Game thread context has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }

        // 0 = queued, 1 = started, 2 = abandoned.
        var claim = new System.Runtime.CompilerServices.StrongBox<int>(0);
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            if (Interlocked.CompareExchange(ref claim.Value, 1, 0) != 0)
            {
                completionSource.TrySetCanceled();
                return;
            }

            _ = InvokeAsyncCoreAsync(action, completionSource);
        }, null);

        using var timeoutCts = new CancellationTokenSource(startTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var wait = Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
        var completed = await Task.WhenAny(completionSource.Task, wait);
        if (completed == completionSource.Task)
        {
            return await completionSource.Task;
        }

        if (Interlocked.CompareExchange(ref claim.Value, 2, 0) == 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException("The game thread did not start the posted work in time.");
        }

        // Started before the abandon could be claimed: the work owns its own bounds; see it through.
        return await completionSource.Task;
    }

    private static async Task InvokeAsyncCoreAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> completionSource)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            if (!completionSource.TrySetResult(result))
            {
                Log.Warn($"{LogPrefix} InvokeAsync async completion source was already completed.");
            }
        }
        catch (Exception ex)
        {
            if (!completionSource.TrySetException(ex))
            {
                Log.Warn($"{LogPrefix} Failed to propagate InvokeAsync async exception because the completion source was already completed: {ex}");
            }
        }
    }

    public static Task WaitForNextFrameAsync()
    {
        if (_syncContext == null)
        {
            return Task.Delay(16);
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return WaitForNextFrameCoreAsync();
        }

        return InvokeAsync(WaitForNextFrameCoreAsync);
    }

    private static async Task WaitForNextFrameCoreAsync()
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(16));
            return;
        }

        var tree = game.GetTree();
        if (tree == null || !GodotObject.IsInstanceValid(tree))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(16));
            return;
        }

        // Occluded/background windows may never emit ProcessFrame. Bound the wait so
        // action timeouts can still fire.
        var frame = AwaitProcessFrame(game, tree);
        var completed = await Task.WhenAny(frame, Task.Delay(50));
        if (completed != frame)
        {
            return;
        }

        await frame;
    }

    private static async Task AwaitProcessFrame(NGame game, SceneTree tree)
    {
        await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
