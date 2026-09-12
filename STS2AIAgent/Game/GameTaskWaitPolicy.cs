namespace STS2AIAgent.Game;

/// <summary>
/// Result of a bounded wait on a game task. <see cref="TimedOut"/> means the deadline
/// passed while the task was still running, so the caller must keep observing it instead
/// of reporting success.
/// </summary>
internal enum GameTaskWaitOutcome
{
    Completed,
    TimedOut,
    Failed,
}

/// <summary>
/// Pure decision layer for bounded game-task waits. The classification runs once, after
/// the wait returned, so "still running and the deadline has not passed" is unreachable.
/// </summary>
internal static class GameTaskWaitPolicy
{
    /// <summary>
    /// Classifies a finished wait. A task that already faulted (or was canceled) is a
    /// failure regardless of the deadline; a task still running when the deadline passed
    /// is a timeout; every other combination is a completion.
    /// </summary>
    public static GameTaskWaitOutcome Classify(bool taskCompleted, bool taskFaulted, bool deadlineReached)
    {
        if (taskCompleted)
        {
            return taskFaulted ? GameTaskWaitOutcome.Failed : GameTaskWaitOutcome.Completed;
        }

        if (deadlineReached)
        {
            return GameTaskWaitOutcome.TimedOut;
        }

        throw new ArgumentOutOfRangeException(
            nameof(deadlineReached),
            "A task that is still running and has not reached its deadline has no outcome yet; keep waiting until the deadline passes.");
    }

    /// <summary>
    /// Message for a task that is still running after its deadline, naming the action and
    /// the timeout that produced the pending response.
    /// </summary>
    public static string DescribeTimeout(string actionName, TimeSpan timeout)
    {
        return $"{actionName} did not finish within {timeout.TotalSeconds:0.###}s; the game task is still running.";
    }
}
