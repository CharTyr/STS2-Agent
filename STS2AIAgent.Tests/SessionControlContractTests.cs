namespace STS2AIAgent.Tests;

internal static class SessionControlContractTests
{
    public static void RouterExposesLocalSessionControl()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        Assert.Contains("/session/control", source, StringComparison.Ordinal);
        Assert.Contains("local_only", source, StringComparison.Ordinal);
        Assert.Contains("SetCompanionRunningAsync", AgentSourceFixture.MethodBody(source, "HandleAsync"), StringComparison.Ordinal);
        Assert.Contains("play_phase = AgentRuntime.Instance.PlayPhase", source, StringComparison.Ordinal);
        Assert.Contains("session_requests = AgentRuntime.Instance.SessionRequests", source, StringComparison.Ordinal);
        Assert.Contains("companion_process_exited = LocalDualInstanceLauncher.CompanionProcessExited", source, StringComparison.Ordinal);
    }

    public static void WorkshopStagingKeepsLocalCandidate()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs");
        Assert.Contains("Keeping the already-installed local STS2AIAgent copy", source, StringComparison.Ordinal);
        var localGuard = source.IndexOf("var flatDll = Path.Combine", StringComparison.Ordinal);
        var workshopLookup = source.IndexOf("FindSubscribedWorkshopModDir()", StringComparison.Ordinal);
        Assert.True(localGuard >= 0 && workshopLookup > localGuard, "local candidate guard must run before Workshop staging");
    }

    /// <summary>
    /// The step button spends session budget too. It used to record only the display counter, so the
    /// guard never advanced and stepping repeatedly could pass the configured request cap.
    /// </summary>
    public static void StepOnceRecordsTheSessionBudget()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        var step = AgentSourceFixture.MethodBody(source, "StepOnceCoreAsync");
        Assert.Contains("_budgetGuard.Observe(result)", step, StringComparison.Ordinal);
        Assert.Contains("SetStop(StopKindPolicy.Budget", step, StringComparison.Ordinal);
    }
}
