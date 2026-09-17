namespace STS2AIAgent.Tests;

/// <summary>
/// <c>abandon_run</c> opens the confirmation, and stops there.
/// </summary>
/// <remarks>
/// This is the only action that destroys a player's run, and it is the one that had no contract of
/// its own: 56 actions, 12 with nothing asserting what they do, and this was among them.
///
/// The property worth pinning is not that it works -- it is where it *stops*. The handler clicks
/// the main menu's abandon button and waits for a modal to open; the run survives until something
/// else answers that modal. Nothing in the code says out loud that the second step is deliberate,
/// so a later "simplification" that confirmed the modal here would look like removing a redundant
/// round trip and would silently turn one call into a destroyed save.
/// </remarks>
internal static class AbandonRunContractTests
{
    private const string RunPath = "STS2AIAgent/Game/GameActionService.Run.cs";

    public static void AbandonRunStopsAtTheConfirmation()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ExecuteAbandonRunAsync"));

        Assert.True(
            body.Contains("WaitForMainMenuModalAsync(TimeSpan.FromSeconds(10))", StringComparison.Ordinal),
            "abandon_run must wait for the confirmation modal to open. That wait is what makes the "
            + "destructive step a separate decision.");

        // The three ways this handler could grow into one that destroys a run by itself.
        foreach (var forbidden in new[] { "ConfirmModal", "ExecuteConfirmModalAsync", "ConfirmButton" })
        {
            Assert.False(
                body.Contains(forbidden, StringComparison.Ordinal),
                $"abandon_run must not reach for {forbidden}. It opens the confirmation; answering it "
                + "is confirm_modal's job, and collapsing the two turns one call into a lost run.");
        }
    }

    public static void AbandonRunRefusesRatherThanClickingBlind()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ExecuteAbandonRunAsync"));

        // Both halves of the guard: the screen type, and the availability predicate that also checks
        // the submenu stack and the button's own enabled state. Either one alone would let a click
        // land on a main menu that is covered by a submenu.
        Assert.True(
            body.Contains("currentScreenisnotNMainMenumainMenu", StringComparison.Ordinal),
            "abandon_run must confirm it is on the main menu before clicking anything.");
        Assert.True(
            body.Contains("!GameStateService.CanAbandonRun(currentScreen)", StringComparison.Ordinal),
            "abandon_run must ask the same availability predicate the action surface offers it by, "
            + "or /state can advertise an action the executor then performs under different rules.");
        Assert.True(
            body.Contains("ApiException(409,\"invalid_action\"", StringComparison.Ordinal),
            "An unavailable abandon_run must answer 409 invalid_action rather than doing nothing.");

        // A missing button is a transient UI state, not a permanent refusal: saying so is what lets
        // a caller retry instead of concluding the run cannot be abandoned.
        Assert.True(
            body.Contains("ApiException(503,\"state_unavailable\"", StringComparison.Ordinal) &&
            body.Contains("retryable:true", StringComparison.Ordinal),
            "A missing abandon button must answer 503 state_unavailable with retryable:true.");
        Assert.False(
            body.Contains("abandonButton?.ForceClick()", StringComparison.Ordinal),
            "abandon_run must not null-conditionally click. A click that silently does not happen "
            + "would report completed while the run is untouched.");
    }
}
