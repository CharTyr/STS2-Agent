using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The action service recovers from a lot of things it cannot control: a Godot call that throws, a
/// reflection hook the game renamed, an overlay that is already gone. Recovering is fine, doing it
/// without a trace is not — "the fallback threw" and "the fallback had nothing to do" otherwise read
/// identically in the log of a stuck action, which is exactly how the confirm_bundle and reward-drain
/// fallbacks behaved. GameActionService.cs does not compile into this project, so the rule is
/// asserted from the source.
/// </summary>
internal static class ActionDiagnosticsContractTests
{

    /// <summary>
    /// A faulted game task reports the exception that faulted it.
    /// </summary>
    /// <remarks>
    /// Eleven actions answer 409 through <c>DescribeGameTaskFailure</c> -- save and quit, the
    /// crystal sphere, event proceed, rest options, three purchases, both lobby operations and
    /// <c>run_console_command</c>. While the helper discarded <c>Task.Exception</c>, every one of
    /// them said the same sentence for a request the game legitimately rejected and a request that
    /// broke the mod.
    ///
    /// A 2026-09-17 live pass hit it: `run_console_command room Treasure`, issued while already
    /// standing in a treasure room, answered `Console command failed: the game task faulted.` with
    /// nothing to act on, and the bounded retry loop in run_sts2_validation.py then repeated it
    /// twenty times. The synchronous branch of that same handler had already been taught to name its
    /// exception, for `bestiary`; this is its asynchronous twin.
    /// </remarks>
    public static void FaultedGameTasksNameTheirException()
    {
        var source = AgentSourceFixture.ReadActionService();
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(source, "private static string DescribeGameTaskFailure(Task task)"));

        Assert.Contains("DescribeTaskFault(task)", body, StringComparison.Ordinal);

        // A task that carries no exception still has to answer something, and a canceled task keeps
        // its own wording: those are different outcomes, not different phrasings of one.
        Assert.Contains("\"thegametaskfaulted\"", body, StringComparison.Ordinal);
        Assert.Contains("\"thegametaskwascanceled\"", body, StringComparison.Ordinal);
        Assert.Contains("\"thegametaskfailed\"", body, StringComparison.Ordinal);

        // The shared describer is the one place Task.Exception is read, and it names the type and
        // the message rather than restating that something faulted.
        var describer = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(source, "private static string DescribeTaskFault(Task task)"));
        Assert.True(
            describer.Contains("task.Exception", StringComparison.Ordinal),
            "DescribeTaskFault must read Task.Exception. Without it every faulted action -- console "
            + "commands, purchases, lobby joins -- reports the same contextless sentence, and a caller "
            + "cannot tell a rejected request from a broken one.");
        Assert.Contains("failure.GetType().Name", describer, StringComparison.Ordinal);
        Assert.Contains("failure.Message", describer, StringComparison.Ordinal);

        // Every call site closes its sentence with a period and a game exception often carries its
        // own, which read as "...already occurring!." live before this was trimmed. The describer is
        // the one place foreign text enters those sentences, so it normalises the seam.
        Assert.Contains("TrimEnd('.','!','?')", describer, StringComparison.Ordinal);

        // remove_card_at_shop reaches its 409 through the pure BackgroundTaskOutcome decision layer
        // instead of DescribeGameTaskFailure, so it is the one path that would otherwise keep the old
        // contextless wording. Fixing eleven call sites and leaving the twelfth is how the two drift.
        var flatSource = AgentSourceFixture.WithoutWhitespace(source);
        Assert.Contains(
            "$\"Cardremovalfailed:{purchaseFailure}{DescribeTaskFault(purchaseTask)}.\"",
            flatSource,
            StringComparison.Ordinal);

        // The console handler's synchronous branch is where this honesty was established; it must
        // keep naming its own exception too, so the two branches of one handler stay consistent.
        var flat = AgentSourceFixture.WithoutWhitespace(source);
        Assert.Contains(
            "$\"Consolecommandfailed:{ex.GetType().Name}:{ex.Message}\"",
            flat,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>catch {}</c>, <c>catch { }</c> and <c>catch (Exception) { }</c> all flatten to the same
    /// shape, so one pattern covers every spelling.
    /// </summary>
    private static readonly Regex EmptyCatch = new(
        @"catch(?:\s*\([^)]*\))?\s*\{\}",
        RegexOptions.Compiled);

    public static void NoRecoveryCatchSwallowsWithoutSayingSo()
    {
        var source = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.ReadActionService());

        var match = EmptyCatch.Match(source);
        Assert.False(
            match.Success,
            "GameActionService.cs swallows a failure without a word at '"
            + source[Math.Max(0, match.Index - 200)..Math.Min(source.Length, match.Index + 2)]
            + "'. Log it, or say in the body why it needs no log.");

        // The fallback chains that used to be wordless. Each step names itself, so a recovery that
        // keeps failing is visible in the log instead of only in a stuck action.
        foreach (var chain in new[]
                 {
                     "TryCancelRunningPlayerAction",
                     "DrainRewardFlowAsync",
                     "confirm_bundle",
                     "continue_game_over",
                 })
        {
            Assert.Contains(
                "Log.Warn($\"[STS2AIAgent]" + chain + ":",
                source,
                StringComparison.Ordinal);
        }
    }
}
