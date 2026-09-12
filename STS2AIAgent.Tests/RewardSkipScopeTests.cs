using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// Pins the reward-set-scoped skip intent that replaced the process-wide
/// <c>_cardRewardSkipped</c> bool. The intent outlives the request that recorded it, but it
/// may only ever apply to the reward set whose identity it carries.
/// </summary>
internal static class RewardSkipScopeTests
{
    private const ulong RewardSetA = 1001;
    private const ulong RewardSetB = 2002;

    public static void AppliesToTheRewardSetItRecorded()
    {
        var scope = new RewardSkipScope();
        scope.MarkSkipped(RewardSetA);

        Assert.True(scope.AppliesTo(RewardSetA));
        Assert.Equal(RewardSetA, scope.RecordedRewardSetId);
    }

    public static void DoesNotApplyToAnotherRewardSet()
    {
        // The leak this replaces: a skip recorded in one reward set must never suppress the
        // card reward of a later, different reward set.
        var scope = new RewardSkipScope();
        scope.MarkSkipped(RewardSetA);

        Assert.False(scope.AppliesTo(RewardSetB));
    }

    public static void DoesNotApplyBeforeAnythingIsRecorded()
    {
        var scope = new RewardSkipScope();

        Assert.False(scope.AppliesTo(RewardSetA));
        Assert.Equal(0UL, scope.RecordedRewardSetId);
    }

    public static void UnresolvedRewardSetNeverApplies()
    {
        // Fail-safe direction: an owner that could not be resolved (id 0) must not suppress a
        // card reward, so the reward is re-shown instead of silently dropped.
        var scope = new RewardSkipScope();
        scope.MarkSkipped(0);

        Assert.False(scope.AppliesTo(0));
        Assert.False(scope.AppliesTo(RewardSetA));
        Assert.False(scope.AppliesTo(RewardSetB));
    }

    public static void ClearForgetsTheRecordedSkip()
    {
        var scope = new RewardSkipScope();
        scope.MarkSkipped(RewardSetA);

        scope.Clear();

        Assert.False(scope.AppliesTo(RewardSetA));
        Assert.Equal(0UL, scope.RecordedRewardSetId);
    }

    public static void RemarkingReplacesThePreviousRewardSet()
    {
        var scope = new RewardSkipScope();
        scope.MarkSkipped(RewardSetA);
        scope.MarkSkipped(RewardSetB);

        Assert.False(scope.AppliesTo(RewardSetA));
        Assert.True(scope.AppliesTo(RewardSetB));
        Assert.Equal(RewardSetB, scope.RecordedRewardSetId);
    }
}
