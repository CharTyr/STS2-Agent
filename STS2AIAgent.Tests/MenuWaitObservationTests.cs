using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// Coverage for the "a lost node is not proof of success" rules. GameActionService.cs is not part of
/// the offline compile, so the decision rules are exercised directly through MenuTransitionPolicy and
/// the three wait helpers are pinned by a source contract instead.
/// </summary>
internal static class MenuWaitObservationTests
{
    private class BaseScreen
    {
    }

    private sealed class DerivedScreen : BaseScreen
    {
    }

    /// <summary>
    /// Truth table for <see cref="MenuTransitionPolicy.IsSubmenuObserved"/>: only an observed screen
    /// that is an instance of the requested submenu type counts, and "nothing observed" never does.
    /// </summary>
    public static void SubmenuIsOnlyObservedWhenTheTargetTypeIsCurrent()
    {
        // Nothing was observed at all: no proof of the transition.
        Assert.False(MenuTransitionPolicy.IsSubmenuObserved(null, typeof(BaseScreen)));

        // Exact type match.
        Assert.True(MenuTransitionPolicy.IsSubmenuObserved(typeof(BaseScreen), typeof(BaseScreen)));

        // Subclass match: "is TSubmenu" is true for a specialization of the requested submenu.
        Assert.True(MenuTransitionPolicy.IsSubmenuObserved(typeof(DerivedScreen), typeof(BaseScreen)));

        // Wrong direction: the current screen is not an instance of the requested submenu.
        Assert.False(MenuTransitionPolicy.IsSubmenuObserved(typeof(BaseScreen), typeof(DerivedScreen)));

        // Unrelated screen type.
        Assert.False(MenuTransitionPolicy.IsSubmenuObserved(typeof(string), typeof(BaseScreen)));
    }

    /// <summary>
    /// Truth table for <see cref="MenuTransitionPolicy.IsFlagObserved"/>: every combination of
    /// "source node alive" and observed/requested value, because reporting the requested value when
    /// the node that carries it is gone is exactly the false success this rule removes.
    /// </summary>
    public static void FlagIsOnlyObservedWhenTheSourceNodeSurvives()
    {
        // Source node destroyed: unreadable, so never confirmed - whatever the last read said.
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: false, observedValue: false, requestedValue: false));
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: false, observedValue: false, requestedValue: true));
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: false, observedValue: true, requestedValue: false));
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: false, observedValue: true, requestedValue: true));

        // Source node alive: the observed value decides, and it must match the request.
        Assert.True(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: true, observedValue: false, requestedValue: false));
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: true, observedValue: false, requestedValue: true));
        Assert.False(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: true, observedValue: true, requestedValue: false));
        Assert.True(MenuTransitionPolicy.IsFlagObserved(
            sourceNodeValid: true, observedValue: true, requestedValue: true));
    }

    /// <summary>
    /// Source contract for the three menu waits: losing the node they were watching stops the wait
    /// but never settles it, while observing the real target still succeeds.
    /// </summary>
    public static void MenuWaitsDoNotTreatALostNodeAsSuccess()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");

        var submenuWait = Body(source, "private static async Task<bool> WaitForMainMenuSubmenuOpenAsync<TSubmenu>");
        var characterSelectWait = Body(source, "private static async Task<bool> WaitForCharacterSelectionTransitionAsync(");
        var lobbyReadyWait = Body(source, "private static async Task<bool> WaitForLobbyReadyTransitionAsync(");

        // The removed shape: a destroyed observation node returned success.
        foreach (var (label, body) in new[]
        {
            ("submenu", submenuWait),
            ("character-select", characterSelectWait),
            ("lobby-ready", lobbyReadyWait)
        })
        {
            Assert.False(
                body.Contains("IsInstanceValid(screen)){returntrue;", StringComparison.Ordinal),
                $"The {label} wait must not report success after the node it observed was destroyed.");
        }

        Assert.False(
            lobbyReadyWait.Contains("returnready;", StringComparison.Ordinal),
            "The lobby-ready wait must not echo the requested ready value when the lobby node is gone.");

        // Losing the node must stop the loop instead of settling it.
        Assert.Contains("break;", submenuWait, StringComparison.Ordinal);
        Assert.Contains("break;", characterSelectWait, StringComparison.Ordinal);
        Assert.Contains("break;", lobbyReadyWait, StringComparison.Ordinal);

        // ... and the final answer must come from the shared observation rules.
        Assert.Contains("MenuTransitionPolicy.IsSubmenuObserved(", submenuWait, StringComparison.Ordinal);
        Assert.Contains("MenuTransitionPolicy.IsFlagObserved(", lobbyReadyWait, StringComparison.Ordinal);

        // An honest fix must not swing the other way: the genuine target still settles the wait.
        Assert.Contains("currentScreenisTSubmenu", submenuWait, StringComparison.Ordinal);
        Assert.Contains(
            "screen.Lobby.LocalPlayer.character.Id.Entry==currentCharacterId",
            characterSelectWait,
            StringComparison.Ordinal);
        Assert.Contains(
            "screen.Lobby.LocalPlayer.isReady==ready",
            lobbyReadyWait,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Source contract for run_console_command: a command task that timed out is still running, so
    /// neither status nor stable may claim completion just because the screen settled.
    /// </summary>
    public static void ConsoleTimeoutNeverReportsCompletion()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var body = Body(source, "private static async Task<ActionResponsePayload> ExecuteConsoleCommandCoreAsync(");

        var statusStart = body.IndexOf("status=", StringComparison.Ordinal);
        Assert.True(statusStart >= 0, "run_console_command must build an action response payload.");
        var flagStart = statusStart + "status=".Length;
        var flagEnd = body.IndexOf('?', flagStart);
        Assert.True(flagEnd > flagStart, "run_console_command status must come from a completion flag.");
        var flagName = body[flagStart..flagEnd];

        // The screen-stability wait alone used to be the whole success proof.
        Assert.False(
            body.Contains($"var{flagName}=awaitWaitForConsoleCommandStabilityAsync(", StringComparison.Ordinal),
            "run_console_command must not derive status/stable straight from the screen-stability wait.");

        // stable mirrors the same flag, so a timeout cannot leave one field claiming a settled state.
        Assert.Contains($"stable={flagName}", body, StringComparison.Ordinal);

        var definitionStart = body.IndexOf($"var{flagName}=", StringComparison.Ordinal);
        Assert.True(
            definitionStart >= 0,
            "The flag that drives status/stable must be defined in this handler.");
        var definitionEnd = body.IndexOf(';', definitionStart);
        var definition = body[definitionStart..definitionEnd];
        Assert.Contains("!consoleTimedOut", definition, StringComparison.Ordinal);
    }

    private static string Body(string source, string declaration)
    {
        return AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(source, declaration));
    }
}
