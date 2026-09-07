using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class RewardFlowContractTests
{
    public static void EmptyRewardsScreenEscapesInsteadOfPending()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var drain = AgentSourceFixture.MethodBody(actionSource, "DrainRewardFlowAsync");
        Assert.Contains("TryEscapeEmptyRewardsScreenAsync", drain);

        var escape = AgentSourceFixture.MethodBody(actionSource, "TryEscapeEmptyRewardsScreenAsync");
        Assert.Contains("TryEnableProceedButton", escape);
        Assert.Contains("ForceClick()", escape);
        Assert.Contains("NOverlayStack.Instance?.Remove", escape);
        Assert.Contains("ProceedFromTerminalRewardsScreen", escape);
        Assert.Contains("TryGetNextClaimableRewardButton", escape);
    }
}
