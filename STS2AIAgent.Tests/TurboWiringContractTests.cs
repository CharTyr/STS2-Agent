namespace STS2AIAgent.Tests;

/// <summary>
/// The game-speed boost (W1) touches the player's own setting, so its safety rails are pinned by
/// source: engage at loop start, release on every loop exit, the prefs-writer guard installed at mod
/// start, and nothing read from the game off the game thread.
/// </summary>
internal static class TurboWiringContractTests
{
    public static void TheLoopEngagesFirstAndReleasesInItsOuterFinally()
    {
        var loop = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadAgentRuntime(), "AutoPlayLoopAsync"));
        var engage = loop.IndexOf("awaitEngageTurboAsync(cancellationToken);", StringComparison.Ordinal);
        var run = loop.IndexOf("awaitAutoPlayRecovery.RunAsync(", StringComparison.Ordinal);
        Assert.True(engage >= 0 && run > engage, "The speed is applied before the first turn.");
        Assert.True(loop.Contains("finally{CancelStrategyRefresh();awaitReleaseTurboAsync();", StringComparison.Ordinal),
            "The outer finally (pause, stop, failure, run end) must give the player's speed back.");
        Assert.True(loop.Contains("TurboController.CapForMultiplayer();", StringComparison.Ordinal),
            "A run that turns out to be co-op is capped at Fast on the next turn.");
    }

    public static void ReleaseIsNotTiedToTheEndedLoopsToken()
    {
        var turbo = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Turbo.cs"));
        var release = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Turbo.cs"), "ReleaseTurboAsync"));
        Assert.True(release.Contains("TurboGameThreadBudget,CancellationToken.None", StringComparison.Ordinal),
            "Pause cancels the loop's token; the release must still be posted, with its own deadline.");
        Assert.True(turbo.Contains("PersistTurboOriginal(original)", StringComparison.Ordinal)
            && release.Contains("PersistTurboOriginal(null)", StringComparison.Ordinal),
            "The player's original is persisted while boosted and cleared only after a successful release.");
    }

    public static void ThePrefsGuardIsInstalledAtModStart()
    {
        var entry = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/ModEntry.cs"));
        Assert.True(entry.Contains("TurboController.InstallPrefsGuard();", StringComparison.Ordinal));
        Assert.True(entry.Contains("AgentRuntime.Instance.ScheduleTurboRecovery();", StringComparison.Ordinal));

        var controller = AgentSourceFixture.Read("STS2AIAgent/Game/TurboController.cs");
        Assert.Contains("new Harmony(\"STS2AIAgent.turbo-prefs-guard\")", controller);
        Assert.Contains("ReflectedGameMembers.Method(typeof(PrefsSaveManager), \"SavePrefs\")", controller);
        Assert.Contains("finalizer: new HarmonyMethod(typeof(TurboController), nameof(AfterSavePrefs))", controller);
        Assert.False(controller.Contains(".SavePrefs()", StringComparison.Ordinal) || controller.Contains("SavePrefsFile(", StringComparison.Ordinal),
            "The mod never writes the game's prefs itself.");
    }

    public static void TheHealthSnapshotNeverReadsTheGame()
    {
        var snapshot = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Game/TurboController.cs"), "Snapshot");
        Assert.False(snapshot.Contains("SaveManager", StringComparison.Ordinal),
            "/health runs off the game thread; it reports the last speed seen on the game thread.");
        Assert.Contains("turbo = TurboController.Snapshot(", AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs"));
    }
}
