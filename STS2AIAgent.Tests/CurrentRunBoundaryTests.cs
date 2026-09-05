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

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
