using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class StopKindPolicyTests
{
    private static string State(string screen, string phase, string runId = "run_123") =>
        JsonSerializer.Serialize(new
        {
            screen,
            run_id = runId,
            session = new { phase }
        });

    // Three failed decisions stop auto-play with the hint "检查当前局面后可手动继续". The bare word
    // "当前局" used to match that hint, so the overlay reported a finished run and told the player
    // to start a new one from the main menu while the run was still live.
    public static Task RetryStopIsNotRunEnd()
    {
        var recovery = new AutoPlayRecovery();
        string? stopReason = null;
        string? stopKind = null;
        for (var attempt = 0; attempt < 3 && stopReason == null; attempt++)
        {
            var next = recovery.Observe(new AgentTurnResult());
            stopReason = next.StopReason;
            stopKind = next.StopKind;
        }

        Assert.NotNull(stopReason);
        Assert.Contains("面后可手动继续", stopReason);
        Assert.Null(stopKind);
        Assert.Equal(StopKindPolicy.Failed, StopKindPolicy.Resolve(stopKind, stopReason));
        return Task.CompletedTask;
    }

    public static void BoundaryStopsAreRunEnd()
    {
        var leftRun = new CurrentRunBoundary();
        leftRun.Check(State("COMBAT", "run", "run_1"));
        var left = Expect<AutoPlayStoppedException>(() =>
            leftRun.Check(State("MAIN_MENU", "menu", "run_unknown")));
        Assert.Equal(StopKindPolicy.RunEnd, StopKindPolicy.Resolve(left.Kind, left.Message));

        var changedRun = new CurrentRunBoundary();
        changedRun.Check(State("COMBAT", "run", "run_1"));
        var changed = Expect<AutoPlayStoppedException>(() =>
            changedRun.Check(State("EVENT", "run", "run_2")));
        Assert.Equal(StopKindPolicy.RunEnd, StopKindPolicy.Resolve(changed.Kind, changed.Message));
    }

    public static void BudgetConfigAndNetworkKindsSurvive()
    {
        Assert.Equal(
            StopKindPolicy.Budget,
            StopKindPolicy.Resolve(StopKindPolicy.Budget, "已达到会话请求次数上限（12/10 次），已自动停止游玩。"));
        Assert.Equal(
            StopKindPolicy.Configuration,
            StopKindPolicy.Classify("请检查模型、端点或凭据后再继续：401 unauthorized"));
        Assert.Equal(StopKindPolicy.Network, StopKindPolicy.Classify("LLM request timed out."));
        Assert.Equal(StopKindPolicy.Failed, StopKindPolicy.Classify(null));
    }

    /// <summary>
    /// A typed kind wins over message matching, and bare keywords no longer decide the budget kind:
    /// a game error containing 上限 used to be reported as a budget stop, which advised the player
    /// to reset session stats instead of looking at the failing decision.
    /// </summary>
    public static void ExplicitKindBeatsTheMessage()
    {
        var recovery = new AutoPlayRecovery();
        string? stopKind = null;
        for (var attempt = 0; attempt < 3 && stopKind == null; attempt++)
        {
            stopKind = recovery.Observe(new AgentTurnResult { Error = "手牌已达上限" }).StopKind;
        }

        Assert.Null(stopKind);
        Assert.Equal(StopKindPolicy.Failed, StopKindPolicy.Classify("手牌已达上限"));

        var configured = recovery.Observe(new AgentTurnResult { RequiresConfiguration = true, Error = "超时" });
        Assert.Equal(StopKindPolicy.Configuration, configured.StopKind);
        Assert.Equal(StopKindPolicy.Configuration, StopKindPolicy.Resolve(configured.StopKind, configured.StopReason));

        // The reason still supplies the missing distinction for recovery stops.
        Assert.Equal(
            StopKindPolicy.Network,
            StopKindPolicy.Resolve(null, "连续 3 次决策未成功：Cannot reach the mod, connection refused"));
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
