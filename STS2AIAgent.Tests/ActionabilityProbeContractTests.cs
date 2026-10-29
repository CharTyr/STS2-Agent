namespace STS2AIAgent.Tests;

/// <summary>
/// The wait for an actionable frame used to build the whole /state payload on every rendered frame,
/// and the auto-play turn built it again just to read five fields. These source contracts keep the
/// cheap probes in place and keep them faithful to the full builder they stand in for.
/// </summary>
/// <remarks>
/// Added 2026-10-28 with the overhaul's performance groundwork (W9). The probe files read the game
/// assemblies, so they are pinned by source rather than by execution.
/// </remarks>
internal static class ActionabilityProbeContractTests
{
    public static void TheProbeEvaluatesTheGateOnceInTheBuildersOrder()
    {
        var probe = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "ProbeActionable"));

        var gate = probe.IndexOf("EvaluateCombatActionGate(currentScreen,combatState)", StringComparison.Ordinal);
        var names = probe.IndexOf("BuildAvailableActionNames(currentScreen,combatState,runState,combatActionGate,", StringComparison.Ordinal);
        var counters = probe.IndexOf("GameActionService.SyncCardPlayCounters(combatState.RoundNumber)", StringComparison.Ordinal);
        var readiness = probe.IndexOf("BuildCombatActionReadinessPayload(combatActionGate)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && names > gate && counters > names && readiness > counters,
            "ProbeActionable must evaluate the combat gate once (it advances the 200 ms stability sampler), "
            + "enumerate the single action surface with that gate, then sync card-play counters before reading "
            + "readiness -- the same order BuildStatePayload/BuildCombatPayload use.");
        Assert.Equal(1, Occurrences(probe, "EvaluateCombatActionGate("));
        Assert.False(probe.Contains("BuildStatePayload(", StringComparison.Ordinal),
            "The probe exists so the wait does not build the whole payload.");
        Assert.True(probe.Contains("StateBuildTiming.Instance.CountActionableProbe()", StringComparison.Ordinal),
            "Every probe is counted so /health can show the builds it replaced.");
    }

    public static void TheWaitProbesEveryFrameAndBuildsOnlyTheFrameItReturns()
    {
        var bridge = AgentSourceFixture.Read("STS2AIAgent/Agent/GameBridge.cs");
        var core = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(bridge, "WaitActionableCoreAsync"));
        Assert.True(core.Contains("GameStateService.ProbeActionable()", StringComparison.Ordinal),
            "The actionable wait must use the cheap probe.");
        Assert.False(core.Contains("BuildStatePayload(", StringComparison.Ordinal),
            "The actionable wait must not build the whole state on a waiting frame.");

        var ready = core.IndexOf("if(!ready||!withState)", StringComparison.Ordinal);
        var build = core.IndexOf("SerializeCompactState()", StringComparison.Ordinal);
        Assert.True(ready >= 0 && build > ready,
            "The only build inside the wait is the frame judged actionable, in the same game-thread unit.");
        Assert.True(core.Contains("probeBudget,cancellationToken", StringComparison.Ordinal),
            "Each probe keeps its own deadline.");

        var withState = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(bridge, "WaitUntilActionableWithStateAsync"));
        Assert.True(withState.Contains("StateReadBudget,cancellationToken", StringComparison.Ordinal),
            "The timed-out wait's single read must be bounded too.");
    }

    public static void StateReadsCarryADeadline()
    {
        var bridge = AgentSourceFixture.Read("STS2AIAgent/Agent/GameBridge.cs");
        foreach (var method in new[]
                 {
                     "GetCompactStateJsonAsync", "GetRawStateJsonAsync", "GetAvailableActionsJsonAsync",
                     "GetActionSnapshotJsonAsync", "GetScreenAsync"
                 })
        {
            var body = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(bridge, method));
            Assert.True(body.Contains("StateReadBudget,cancellationToken", StringComparison.Ordinal),
                method + " must pass a deadline: a token alone let an occluded window hold the turn until pause.");
        }
    }

    public static void TheAutoPlayTurnReadsAProbeNotAFullBuild()
    {
        var runtime = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadAgentRuntime(), "AutoPlayLoopAsync"));
        Assert.True(runtime.Contains("GameStateService.BuildTurnProbe()", StringComparison.Ordinal),
            "The per-turn snapshot must use the turn probe.");
        Assert.False(runtime.Contains("BuildStatePayload(", StringComparison.Ordinal),
            "The per-turn snapshot only ever read five fields; a full build there cost a frame per turn.");

        var probe = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "BuildTurnProbe"));
        Assert.True(probe.Contains("ResolveScreen(currentScreen)", StringComparison.Ordinal)
            && probe.Contains("BuildSessionPayload(currentScreen,runState).phase", StringComparison.Ordinal)
            && probe.Contains("runState?.Rng.StringSeed??\"run_unknown\"", StringComparison.Ordinal)
            && probe.Contains("CombatManager.Instance.IsInProgress", StringComparison.Ordinal),
            "The turn probe must read the same sources BuildStatePayload uses for screen, phase, run id and in_combat.");
        Assert.False(probe.Contains("EvaluateCombatActionGate(", StringComparison.Ordinal),
            "The turn probe must not advance the combat gate's stability sampler.");
    }

    public static void TheEventPollCountsItsBuilds()
    {
        var events = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs"));
        Assert.True(events.Contains("StateBuildTiming.Instance.CountEventPollBuild();returnGameStateService.BuildStatePayload();", StringComparison.Ordinal),
            "The SSE poll's builds are counted so /health separates them from the agent's own.");
        Assert.Contains("TimeSpan.FromSeconds(5),cancellationToken", events);
        Assert.Contains("_cadence.Observe(", events);
        Assert.Contains("TimeSpan.FromMilliseconds(_cadence.CurrentMs)", events);
        Assert.Contains("_cadence.Reset()", AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs"), "Subscribe"));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
