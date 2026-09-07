using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class FtueModalPolicyTests
{
    public static void CombatRulesFtueWithoutButtonIsConfirmable()
    {
        Assert.True(FtueModalPolicy.IsFtueType("NCombatRulesFtue"));
        Assert.True(FtueModalPolicy.IsCombatRulesFtue("NCombatRulesFtue"));
        Assert.True(FtueModalPolicy.ExposeConfirm("NCombatRulesFtue", hasUsableConfirmButton: false));
        Assert.False(FtueModalPolicy.CloseFtueDirectly("NCombatRulesFtue", hasUsableConfirmButton: false));
        Assert.False(FtueModalPolicy.CloseFtueDirectly("NCombatRulesFtue", hasUsableConfirmButton: true));
        Assert.True(FtueModalPolicy.AdvanceWithConfirmButton("NCombatRulesFtue", hasUsableConfirmButton: true));
        Assert.False(FtueModalPolicy.AdvanceWithConfirmButton("NCombatRulesFtue", hasUsableConfirmButton: false));
        Assert.False(FtueModalPolicy.IsFtueType("NVerticalPopup"));
        Assert.False(FtueModalPolicy.ExposeConfirm("NVerticalPopup", hasUsableConfirmButton: false));
        Assert.True(FtueModalPolicy.ExposeConfirm("NVerticalPopup", hasUsableConfirmButton: true));
        Assert.False(FtueModalPolicy.CloseFtueDirectly("NAbandonRunConfirmPopup", hasUsableConfirmButton: false));
        Assert.Equal("CloseFtueAndEndTurn", FtueModalPolicy.CloseMethodNames("NCanPlayCardsFtue")[0]);
        Assert.Equal(0, FtueModalPolicy.CloseMethodNames("NCombatRulesFtue").Count);
        Assert.Contains("CloseMethodNames", AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs"));

        var stateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        Assert.Contains("FtueModalPolicy.ExposeConfirm", stateSource);
        Assert.Contains("TryCloseOpenFtue", stateSource);
        Assert.Contains("CloseFtue", stateSource);
        Assert.True(!stateSource.Contains("GameActionService.EnsureEndTurnPhaseStarts()"));
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        Assert.Contains("GameStateService.TryCloseOpenFtue()", actionSource);
        Assert.Contains("FtueModalPolicy.CloseFtueDirectly", actionSource);
        Assert.Contains("FtueModalPolicy.IsCombatRulesFtue", actionSource);
        Assert.Contains("typeof(NButton)", stateSource);
        var endTurn = AgentSourceFixture.MethodBody(actionSource, "CommitEndTurnButtonAsync");
        Assert.Contains("DebugPress()", endTurn);
        Assert.Contains("WaitForEndTurnLongPressAsync", endTurn);
        Assert.Contains("CallReleaseLogic()", endTurn);
        Assert.Contains("DebugRelease()", endTurn);
        Assert.Contains("Unpause()", endTurn);
        Assert.True(!endTurn.Contains("SetReadyToEndTurn"));
        var executeEndTurn = AgentSourceFixture.MethodBody(actionSource, "ExecuteEndTurnAsync");
        Assert.True(!executeEndTurn.Contains("EnsureEndTurnPhaseStarts()"));
        Assert.Contains("confirm pages instead of ending the turn", executeEndTurn);
        var kick = AgentSourceFixture.MethodBody(actionSource, "EnsureEndTurnPhaseStarts");
        Assert.True(!kick.Contains("EndPlayerTurnPhaseOneInternal"));
        Assert.True(!kick.Contains("AfterAllPlayersReadyToEndTurn"));
        Assert.True(!kick.Contains("method.Invoke"));
    }
}
