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
        for (var attempt = 0; attempt < 3 && stopReason == null; attempt++)
        {
            stopReason = recovery.Observe(new AgentTurnResult()).StopReason;
        }

        Assert.NotNull(stopReason);
        Assert.Contains("面后可手动继续", stopReason);
        Assert.Equal(StopKindPolicy.Failed, StopKindPolicy.Classify(stopReason));
        return Task.CompletedTask;
    }

    public static void BoundaryStopsAreRunEnd()
    {
        var leftRun = new CurrentRunBoundary();
        leftRun.Check(State("COMBAT", "run", "run_1"));
        var left = Expect<AutoPlayStoppedException>(() =>
            leftRun.Check(State("MAIN_MENU", "menu", "run_unknown")));
        Assert.Equal(StopKindPolicy.RunEnd, StopKindPolicy.Classify(left.Message));

        var changedRun = new CurrentRunBoundary();
        changedRun.Check(State("COMBAT", "run", "run_1"));
        var changed = Expect<AutoPlayStoppedException>(() =>
            changedRun.Check(State("EVENT", "run", "run_2")));
        Assert.Equal(StopKindPolicy.RunEnd, StopKindPolicy.Classify(changed.Message));
    }

    public static void BudgetConfigAndNetworkKindsSurvive()
    {
        Assert.Equal(
            StopKindPolicy.Budget,
            StopKindPolicy.Classify("已达到会话请求次数上限（12/10 次），已自动停止游玩。"));
        Assert.Equal(
            StopKindPolicy.Configuration,
            StopKindPolicy.Classify("请检查模型、端点或凭据后再继续：401 unauthorized"));
        Assert.Equal(StopKindPolicy.Network, StopKindPolicy.Classify("LLM request timed out."));
        Assert.Equal(StopKindPolicy.Failed, StopKindPolicy.Classify(null));
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
