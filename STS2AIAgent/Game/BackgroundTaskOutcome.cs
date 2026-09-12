namespace STS2AIAgent.Game;

/// <summary>
/// Pure decision layer for fire-and-forget game tasks. A task that already finished
/// without success must be reported to the caller instead of being swallowed as "still
/// transitioning".
/// </summary>
internal static class BackgroundTaskOutcome
{
    /// <summary>
    /// Returns a human-readable reason when the task already finished in a failed state,
    /// or <c>null</c> when it is still running or completed successfully.
    /// </summary>
    public static string? DescribeFailure(bool isCompleted, bool isFaulted, bool isCanceled, bool? result)
    {
        if (!isCompleted)
        {
            return null;
        }

        if (isCanceled)
        {
            return "the purchase task was canceled";
        }

        if (isFaulted)
        {
            return "the purchase task faulted";
        }

        return result == false ? "the game rejected the purchase" : null;
    }
}
