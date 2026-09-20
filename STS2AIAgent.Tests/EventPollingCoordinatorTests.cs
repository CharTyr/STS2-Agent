using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class EventPollingCoordinatorTests
{
    public static async Task PollingFollowsSubscriberDemand()
    {
        var builds = 0;
        var coordinator = new EventPollingCoordinator(
            _ =>
            {
                Interlocked.Increment(ref builds);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(15));

        coordinator.Start();
        await Task.Delay(50);
        Assert.Equal(0, Volatile.Read(ref builds));

        coordinator.SetSubscriberCount(1);
        await WaitUntilAsync(() => Volatile.Read(ref builds) >= 1);
        Assert.Equal(1, coordinator.LoopStartCount);

        coordinator.SetSubscriberCount(2);
        var beforeSecond = Volatile.Read(ref builds);
        await WaitUntilAsync(() => Volatile.Read(ref builds) > beforeSecond);
        Assert.Equal(1, coordinator.LoopStartCount);

        coordinator.SetSubscriberCount(1);
        var beforeOneLeaves = Volatile.Read(ref builds);
        await WaitUntilAsync(() => Volatile.Read(ref builds) > beforeOneLeaves);

        coordinator.SetSubscriberCount(0);
        await Task.Delay(35);
        var idleBuilds = Volatile.Read(ref builds);
        await Task.Delay(50);
        Assert.Equal(idleBuilds, Volatile.Read(ref builds));

        coordinator.SetSubscriberCount(1);
        await WaitUntilAsync(() => Volatile.Read(ref builds) > idleBuilds);
        Assert.Equal(1, coordinator.LoopStartCount);

        coordinator.Stop();
        var stoppedBuilds = Volatile.Read(ref builds);
        coordinator.SetSubscriberCount(1);
        await Task.Delay(50);
        Assert.Equal(stoppedBuilds, Volatile.Read(ref builds));
    }

    public static async Task ConcurrentStartsCreateOneLoop()
    {
        var builds = 0;
        var coordinator = new EventPollingCoordinator(
            _ =>
            {
                Interlocked.Increment(ref builds);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(15));

        coordinator.SetSubscriberCount(2);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(coordinator.Start)));
        await WaitUntilAsync(() => Volatile.Read(ref builds) >= 1);
        Assert.Equal(1, coordinator.LoopStartCount);
        coordinator.Stop();
    }

    public static async Task StopCancelsAnInFlightPoll()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new EventPollingCoordinator(
            async cancellationToken =>
            {
                entered.TrySetResult(true);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult(true);
                    throw;
                }
            },
            TimeSpan.FromMilliseconds(15));

        coordinator.SetSubscriberCount(1);
        coordinator.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.Stop();
        Assert.True(await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    /// A poll that ignores cancellation cannot be replaced. The old behaviour nulled the loop task
    /// before waiting, so a restart put a second loop beside the one still running on the game
    /// thread; this pins that the coordinator stays stopped instead.
    /// </summary>
    public static async Task RestartIsRefusedWhileAnAbandonedPollStillRuns()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0;
        var coordinator = new EventPollingCoordinator(
            async _ =>
            {
                Interlocked.Increment(ref polls);
                entered.TrySetResult(true);
                // Deliberately ignores the token, the way a poll blocked on the game thread can.
                await release.Task;
            },
            TimeSpan.FromMilliseconds(15));

        coordinator.SetSubscriberCount(1);
        coordinator.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.SetSubscriberCount(0);
        coordinator.Stop();
        var generationAfterStop = coordinator.CurrentGeneration;

        coordinator.SetSubscriberCount(1);
        coordinator.Start();
        await Task.Delay(60);

        // The abandoned poll keeps its generation current because Start refuses to begin a new
        // lifecycle while it is still live; the point is that no second loop was created beside it.
        Assert.Equal(1, coordinator.LoopStartCount);
        Assert.Equal(1, Volatile.Read(ref polls));

        release.TrySetResult(true);
        await WaitUntilAsync(() => coordinator.LoopStartCount == 1);
    }

    /// <summary>
    /// The fence itself: a sample captured before Stop can never be committed after it, which is
    /// what keeps a shutdown-then-subscribe sequence from being handed the old lifecycle's state.
    /// </summary>
    public static void GenerationFenceRejectsPreStopCommits()
    {
        var coordinator = new EventPollingCoordinator(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(15));

        coordinator.Start();
        var live = coordinator.CurrentGeneration;
        Assert.True(coordinator.IsGenerationCurrent(live));

        coordinator.Stop();

        Assert.False(coordinator.IsGenerationCurrent(live));
        Assert.False(coordinator.IsGenerationCurrent(coordinator.CurrentGeneration));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before the test deadline.");
            }

            await Task.Delay(5);
        }
    }
}
