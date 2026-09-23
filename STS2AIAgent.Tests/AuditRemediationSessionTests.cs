using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>Run attribution and cross-run isolation contracts; no live game or HTTP listener.</summary>
internal static class AuditRemediationSessionTests
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("SessionRemediation.HttpActionUsesPreActionRun", () => Task.Run(HttpActionUsesPreActionRun));
        yield return ("SessionRemediation.StateReadConfirmsOnGameThread", () => Task.Run(StateReadConfirmsOnGameThread));
        yield return ("SessionRemediation.LateDecisionCannotSwitchRun", () => Task.Run(LateDecisionCannotSwitchRun));
        yield return ("SessionRemediation.StaleObservationCannotSwitchRun", () => Task.Run(StaleObservationCannotSwitchRun));
        yield return ("SessionRemediation.MenuUnknownOnlyClearsOnExplicitExit", () => Task.Run(MenuUnknownOnlyClearsOnExplicitExit));
        yield return ("SessionRemediation.RestoredDecisionMemoryIsBounded", () => Task.Run(RestoredDecisionMemoryIsBounded));
        yield return ("SessionRemediation.LegacyCollisionPreservesBothRuns", () => Task.Run(LegacyCollisionPreservesBothRuns));
    }

    private static void HttpActionUsesPreActionRun()
    {
        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var start = router.IndexOf("request.Url?.AbsolutePath == \"/action\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "HTTP /action route must exist");
        var body = router[start..router.IndexOf("await WriteJsonAsync(response, 200", start, StringComparison.Ordinal)];
        var capture = body.IndexOf("var before = GameStateService.BuildStatePayload();", StringComparison.Ordinal);
        var observe = body.IndexOf("ObserveSessionStateSnapshot(before.run_id, before.screen, before.session.phase)", StringComparison.Ordinal);
        var act = body.IndexOf("GameActionService.ExecuteAsync(actionRequest)", StringComparison.Ordinal);
        var log = body.IndexOf("runId: actionRunId, runIdObserved: true", StringComparison.Ordinal);
        Assert.True(capture >= 0 && capture < observe && observe < act && act < log,
            "Capture the run before the action within the same game-thread unit and attribute the accepted action to it.");
        Assert.False(body.Contains("runId: AgentRuntime.Instance.CurrentRunId", StringComparison.Ordinal),
            "The autoplay run boundary is not a reliable ID for external actions.");
    }

    private static void StateReadConfirmsOnGameThread()
    {
        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var start = router.IndexOf("request.Url?.AbsolutePath == \"/state\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = router[start..router.IndexOf("request.Url?.AbsolutePath == \"/decision-snapshot\"", start, StringComparison.Ordinal)];
        Assert.Contains("GameThread.InvokeAsync(() =>", body);
        Assert.Contains("ObserveSessionStateSnapshot(current.run_id, current.screen, current.session.phase)", body);
    }

    private static void LateDecisionCannotSwitchRun()
    {
        var session = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Session.cs");
        var handler = AgentSourceFixture.MethodBody(session, "OnSessionDecision");
        Assert.False(handler.Contains("ObserveSessionRunBoundary(entry.run_id)", StringComparison.Ordinal));
        Assert.Contains("entry.run_id == active", handler);
        Assert.Contains("_pendingSessionWrites[entry.run_id!]", handler);
        Assert.Contains("history.Record(entry)", handler);
        // The log remains global while the old session gets a separate persisted snapshot.
        Assert.Contains("_sessionMemory.Record(entry)", handler);
        Assert.Contains("_sessionStore.Load(entry.run_id)", handler);
        Assert.Contains("runIdObserved ? runId : runId ?? _sessionRunId", session);
    }

    private static void StaleObservationCannotSwitchRun()
    {
        var session = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Session.cs");
        var callback = AgentSourceFixture.MethodBody(session, "ObserveSessionState");
        Assert.Contains("if (_sessionRunId != runId) return", callback);
        var switchBody = AgentSourceFixture.MethodBody(session, "ObserveSessionRunBoundary");
        Assert.Contains("!confirmed && _retiredSessionRuns.Contains(boundaryRunId!)", switchBody);
        Assert.Contains("DropPlayInstructionsForRun(boundaryRunId)", switchBody);
        Assert.Contains("_retiredSessionRuns.Add(_sessionRunId)", switchBody);
        var fresh = AgentSourceFixture.MethodBody(session, "ObserveCurrentSessionAsync");
        Assert.Contains("GameThread.InvokeAsync(() =>", fresh);
        Assert.Contains("ObserveSessionStateSnapshot(state.run_id, state.screen, state.session.phase)", fresh);
    }

    private static void MenuUnknownOnlyClearsOnExplicitExit()
    {
        var session = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Session.cs");
        var body = AgentSourceFixture.MethodBody(session, "ObserveSessionStateSnapshot");
        Assert.Contains("if (PlaySessionStore.IsPersistable(runId))", body);
        Assert.Contains("screen is not (\"MAIN_MENU\"", body);
        Assert.Contains("|| phase is not (\"menu\"", body);
        Assert.Contains("FlushSessionIfDirty()", body);
        Assert.Contains("_retiredSessionRuns.Add(_sessionRunId!)", body);
        Assert.Contains("DropPlayInstructionsForRun(null)", body);
        Assert.Contains("_sessionRunId = null", body);
        Assert.Contains("_sessionMemory.Restore(\"run_unknown\", null)", body);
    }

    private static void RestoredDecisionMemoryIsBounded()
    {
        var memory = new PlaySessionMemory();
        var run = "seed-a";
        var entries = Enumerable.Range(0, PlaySessionStore.MaxDecisions + 3)
            .Select(i => new DecisionLogEntry(i, "2026-01-01T00:00:00Z", "http_api", "play_card", null, null, 0, null, run));
        memory.Restore(run, entries);
        Assert.Equal(PlaySessionStore.MaxDecisions, memory.Snapshot(PlaySessionStore.MaxDecisions).Count);
        var old = new DecisionLogEntry(9999, "2026-01-01T00:00:00Z", "http_api", "end_turn", null, null, 0, null, "seed-old");
        Assert.False(memory.Record(old));
        Assert.Equal(run, memory.Snapshot()[^1].run_id);
        memory.Restore("seed-b", null);
        Assert.False(memory.Snapshot().Any());
        Assert.False(memory.Record(old));
    }

    private static void LegacyCollisionPreservesBothRuns()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sts2-remediation-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var store = new PlaySessionStore(dir);
            File.WriteAllText(Path.Combine(dir, "seed.json"), "{\"run_id\":\"Seed\"}");
            Assert.True(store.Save(new PlaySessionRecord { RunId = "seed" }));
            Assert.True(store.Save(new PlaySessionRecord { RunId = "seed" }));
            Assert.Equal("Seed", store.Load("Seed")?.RunId);
            Assert.Equal("seed", store.Load("seed")?.RunId);
            Assert.False(store.Save(new PlaySessionRecord { RunId = "run_unknown" }));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
