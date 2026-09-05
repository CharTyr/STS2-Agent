using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class AutoPlaySessionTests
{
    public static async Task PauseWaitsForCommittedWorkAndBlocksRestart()
    {
        var session = new AutoPlaySession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var task = session.TryStart(async _ =>
        {
            Interlocked.Increment(ref started);
            entered.SetResult();
            // Models cancellation-insensitive work already submitted to the game.
            await release.Task;
        }, CancellationToken.None);
        Assert.NotNull(task);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var paused = session.RequestPause();
            Assert.False(paused.IsCompleted);
            Assert.Equal("stopping", session.Phase);
            Assert.Null(session.TryStart(_ => Task.CompletedTask, CancellationToken.None));
            Assert.Equal(1, started);
        }
        finally { release.TrySetResult(); }
        await task!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paused", session.Phase);
        var resumed = session.TryStart(_ => { Interlocked.Increment(ref started); return Task.CompletedTask; }, CancellationToken.None);
        Assert.NotNull(resumed);
        await resumed!;
        Assert.Equal(2, started);
    }

    public static async Task PauseCancelsWaitingModel()
    {
        var session = new AutoPlaySession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = session.TryStart(async cancellation =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.RequestPause().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paused", session.Phase);
        Assert.False(session.IsActive);
    }

    public static async Task ImmediatePauseNeverOverlapsGenerations()
    {
        var session = new AutoPlaySession();
        var active = 0;
        for (var i = 0; i < 20; i++)
        {
            _ = session.TryStart(async token =>
            {
                Assert.Equal(1, Interlocked.Increment(ref active));
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { Interlocked.Decrement(ref active); }
            }, CancellationToken.None);
            await session.RequestPause().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, active);
        }
    }

    public static void CanceledLifetimeCannotStart()
    {
        var session = new AutoPlaySession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Null(session.TryStart(_ => throw new Exception("Must not start"), cancellation.Token));
        Assert.Equal("paused", session.Phase);
    }
}
