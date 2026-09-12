using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class BackgroundTaskOutcomeTests
{
    public static void RunningTaskHasNoFailure()
    {
        Assert.Null(BackgroundTaskOutcome.DescribeFailure(
            isCompleted: false,
            isFaulted: false,
            isCanceled: false,
            result: null));
    }

    public static void SuccessfulPurchaseHasNoFailure()
    {
        Assert.Null(BackgroundTaskOutcome.DescribeFailure(
            isCompleted: true,
            isFaulted: false,
            isCanceled: false,
            result: true));
    }

    public static void RejectedPurchaseReportsAReason()
    {
        var reason = BackgroundTaskOutcome.DescribeFailure(
            isCompleted: true,
            isFaulted: false,
            isCanceled: false,
            result: false);

        Assert.NotNull(reason);
    }

    public static void FaultedAndCanceledTasksReportAReason()
    {
        Assert.NotNull(BackgroundTaskOutcome.DescribeFailure(
            isCompleted: true,
            isFaulted: true,
            isCanceled: false,
            result: null));

        Assert.NotNull(BackgroundTaskOutcome.DescribeFailure(
            isCompleted: true,
            isFaulted: false,
            isCanceled: true,
            result: null));
    }
}
