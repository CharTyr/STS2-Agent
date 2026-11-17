namespace STS2AIAgent.Multiplayer;

internal enum CompanionCloseStart { NotAtRoot, AlreadyExited, Closing, Failed }
internal enum CompanionRetirementResult { Retired, NotAtRoot, Busy, Failed, TimedOut }

/// <summary>Shared, game-free policy and bounded sequencing for an explicit new co-op session.</summary>
internal static class CompanionRetirement
{
    public static bool ShowTeamControls(bool isRootMenu, bool processAlive, bool connected) =>
        !isRootMenu && processAlive && connected;

    /// <summary>
    /// The caller uses the launcher's own gate, so no launch can overlap the close or cleanup.
    /// startClose must bound its game-thread post and check the root menu and signal the owned
    /// process in that same post. Once the post starts, it must finish before this gate is released.
    /// </summary>
    public static async Task<CompanionRetirementResult> TryRetireAsync(
        SemaphoreSlim gate,
        Func<CancellationToken, Task<CompanionCloseStart>> startClose,
        Func<CancellationToken, Task> waitForExit,
        Action clearExited,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return CompanionRetirementResult.Busy;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                var start = await startClose(deadline.Token);
                if (start == CompanionCloseStart.NotAtRoot) return CompanionRetirementResult.NotAtRoot;
                if (start == CompanionCloseStart.Failed) return CompanionRetirementResult.Failed;
                if (start == CompanionCloseStart.Closing)
                    await waitForExit(deadline.Token).WaitAsync(deadline.Token);
                clearExited();
                return CompanionRetirementResult.Retired;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CompanionRetirementResult.TimedOut;
            }
            catch (TimeoutException)
            {
                return CompanionRetirementResult.TimedOut;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
