using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class CurrentRunBoundaryTests
{
    private static string State(string screen, string phase, string runId = "run_123") =>
        JsonSerializer.Serialize(new
        {
            screen,
            run_id = runId,
            session = new { phase }
        });

    public static void AllowsLobbyBeforeRun()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check(State("CHARACTER_SELECT", "character_select"));
        boundary.Check(State("MULTIPLAYER_LOBBY", "multiplayer_lobby"));
        boundary.Check(State("MULTIPLAYER_LOAD", "menu"));
    }

    public static void StopsWhenLeavingRunToMainMenu()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check(State("COMBAT", "run", "run_1"));
        var ex = Expect<AutoPlayStoppedException>(() =>
            boundary.Check(State("MAIN_MENU", "menu", "run_unknown")));
        Assert.Contains("当前局已离开", ex.Message);
    }

    public static void StopsWhenLeavingRunToLobby()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check(State("COMBAT", "run", "run_1"));
        var ex = Expect<AutoPlayStoppedException>(() =>
            boundary.Check(State("CHARACTER_SELECT", "character_select", "run_2")));
        Assert.Contains("当前局已离开", ex.Message);
    }

    public static void StopsWhenRunIdChanges()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check(State("COMBAT", "run", "run_1"));
        var ex = Expect<AutoPlayStoppedException>(() =>
            boundary.Check(State("EVENT", "run", "run_2")));
        Assert.Contains("对局标识变化", ex.Message);
    }

    public static void AllowsGameOverAndUnlock()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check(State("COMBAT", "run", "run_1"));
        boundary.Check(State("GAME_OVER", "run", "run_1"));
        boundary.Check(State("UNLOCK", "unknown", "run_1"));
    }

    public static void StopsMainMenuEvenIfSessionPhaseStillRun()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check("COMBAT", "run", "run_1");
        var ex = Expect<AutoPlayStoppedException>(() =>
            boundary.Check("MAIN_MENU", "run", "run_1"));
        Assert.Contains("当前局已离开", ex.Message);
    }

    public static void StopsCharacterSelectByScreenName()
    {
        var boundary = new CurrentRunBoundary();
        boundary.Check("MAP", "run", "run_1");
        var ex = Expect<AutoPlayStoppedException>(() =>
            boundary.Check("CHARACTER_SELECT", "run", "run_2"));
        Assert.Contains("当前局已离开", ex.Message);
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    /// <summary>
    /// StartAutoPlay installs a fresh boundary, which is what makes the class comment
    /// ("scoped to one automatic session") true: a run that begins while auto-play is
    /// paused belongs to the new session instead of reading as an identity change
    /// against the run the previous session watched.
    /// </summary>
    public static void FreshSessionAcceptsARunThatStartedWhilePaused()
    {
        var finished = new CurrentRunBoundary();
        finished.Check(State("COMBAT", "run", "run_1"));
        finished.Check(State("GAME_OVER", "run", "run_1"));

        var nextSession = new CurrentRunBoundary();
        nextSession.Check(State("EVENT", "run", "run_2"));
        nextSession.Check(State("MAP", "run", "run_2"));

        var ex = Expect<AutoPlayStoppedException>(() =>
            finished.Check(State("EVENT", "run", "run_2")));
        Assert.Contains("对局标识变化", ex.Message);
    }
}
