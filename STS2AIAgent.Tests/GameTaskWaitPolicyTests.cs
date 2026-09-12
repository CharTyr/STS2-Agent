using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class GameTaskWaitPolicyTests
{
    public static void AFinishedTaskIsCompleted()
    {
        Assert.Equal(GameTaskWaitOutcome.Completed, GameTaskWaitPolicy.Classify(
            taskCompleted: true,
            taskFaulted: false,
            deadlineReached: false));

        // The deadline flag no longer matters once the task already finished.
        Assert.Equal(GameTaskWaitOutcome.Completed, GameTaskWaitPolicy.Classify(
            taskCompleted: true,
            taskFaulted: false,
            deadlineReached: true));
    }

    public static void AFaultedTaskIsFailed()
    {
        Assert.Equal(GameTaskWaitOutcome.Failed, GameTaskWaitPolicy.Classify(
            taskCompleted: true,
            taskFaulted: true,
            deadlineReached: false));

        Assert.Equal(GameTaskWaitOutcome.Failed, GameTaskWaitPolicy.Classify(
            taskCompleted: true,
            taskFaulted: true,
            deadlineReached: true));
    }

    public static void AStillRunningTaskPastItsDeadlineTimesOut()
    {
        Assert.Equal(GameTaskWaitOutcome.TimedOut, GameTaskWaitPolicy.Classify(
            taskCompleted: false,
            taskFaulted: false,
            deadlineReached: true));

        Assert.Equal(GameTaskWaitOutcome.TimedOut, GameTaskWaitPolicy.Classify(
            taskCompleted: false,
            taskFaulted: true,
            deadlineReached: true));
    }

    public static void AStillRunningTaskBeforeItsDeadlineCannotBeClassified()
    {
        AssertThrowsOutOfRange(() => GameTaskWaitPolicy.Classify(false, false, false));
        AssertThrowsOutOfRange(() => GameTaskWaitPolicy.Classify(false, true, false));
    }

    public static void TimeoutMessageNamesTheActionAndTheTimeout()
    {
        var restMessage = GameTaskWaitPolicy.DescribeTimeout("choose_rest_option", TimeSpan.FromSeconds(10));
        Assert.Contains("choose_rest_option", restMessage, StringComparison.Ordinal);
        Assert.Contains("10s", restMessage, StringComparison.Ordinal);

        var saveMessage = GameTaskWaitPolicy.DescribeTimeout("save_and_quit", TimeSpan.FromSeconds(20));
        Assert.Contains("save_and_quit", saveMessage, StringComparison.Ordinal);
        Assert.Contains("20s", saveMessage, StringComparison.Ordinal);
    }

    private static void AssertThrowsOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new Exception("Expected ArgumentOutOfRangeException for a wait that is still running.");
    }
}
