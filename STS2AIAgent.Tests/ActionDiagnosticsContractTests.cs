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
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";

    /// <summary>
    /// <c>catch {}</c>, <c>catch { }</c> and <c>catch (Exception) { }</c> all flatten to the same
    /// shape, so one pattern covers every spelling.
    /// </summary>
    private static readonly Regex EmptyCatch = new(
        @"catch(?:\s*\([^)]*\))?\s*\{\}",
        RegexOptions.Compiled);

    public static void NoRecoveryCatchSwallowsWithoutSayingSo()
    {
        var source = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read(ActionPath));

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
