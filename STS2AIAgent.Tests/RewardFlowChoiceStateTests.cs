using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// Pins the request-scoped consume-once contract that replaced the process-wide
/// <c>_pendingCardRewardChoice</c> field. A drain may spend its choice once; every later
/// card-reward screen in the same drain takes the automatic first-card behavior.
/// </summary>
internal static class RewardFlowChoiceStateTests
{
    public static void ExplicitChoiceIsSpentOnce()
    {
        var state = new RewardFlowChoiceState(2);

        Assert.Equal(2, state.PendingChoice);
        Assert.Equal(2, state.ConsumePendingChoice());
        Assert.Equal(RewardChoicePolicy.AutoChoice, state.PendingChoice);
        Assert.Equal(RewardChoicePolicy.AutoChoice, state.ConsumePendingChoice());
    }

    public static void SkipSentinelIsSpentOnce()
    {
        var state = new RewardFlowChoiceState(RewardChoicePolicy.SkipChoice);

        Assert.Equal(RewardChoicePolicy.SkipChoice, state.ConsumePendingChoice());
        Assert.Equal(RewardChoicePolicy.AutoChoice, state.PendingChoice);
        Assert.Equal(RewardChoicePolicy.AutoChoice, state.ConsumePendingChoice());
    }

    public static void StatesDoNotShareChoice()
    {
        // Each request owns its own state: an unspent choice cannot ride along into a later
        // call the way the removed static field allowed.
        var unspent = new RewardFlowChoiceState(RewardChoicePolicy.SkipChoice);
        var laterRequest = new RewardFlowChoiceState(RewardChoicePolicy.AutoChoice);

        Assert.Equal(RewardChoicePolicy.SkipChoice, unspent.PendingChoice);
        Assert.Equal(RewardChoicePolicy.AutoChoice, laterRequest.PendingChoice);
        Assert.Equal(RewardChoicePolicy.AutoChoice, laterRequest.ConsumePendingChoice());
        Assert.Equal(RewardChoicePolicy.SkipChoice, unspent.PendingChoice);
    }
}
