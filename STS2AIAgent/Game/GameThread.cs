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

        await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
