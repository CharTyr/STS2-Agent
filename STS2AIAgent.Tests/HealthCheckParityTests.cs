using STS2AIAgent.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The in-game auto-play prompt is <see cref="PlayPrompt.PlaySystem"/>: the shared skill contract plus
/// the screen playbooks. The model answers it with <see cref="AgentTools.Play"/>, which does not expose
/// <c>health_check</c>, so an imperative mention of it costs a wasted round and contradicts the tool list
/// printed right above the contract. These tests keep the embedded text honest without removing the tool
/// from the MCP surfaces that really do offer it.
/// </summary>
internal static class HealthCheckParityTests
{
    /// <summary>Tools that exist on an MCP surface but never on the in-game play surface.</summary>
    private static readonly string[] ExternalOnlyToolNames =
    [
        "health_check",
        "wait_for_event",
        "run_console_command"
    ];

    // Split on line breaks, "; ", and ". " so an instruction keeps its own object and cannot hide beside
    // a neighbouring sentence that happens to comply.
    private static readonly Regex SegmentSplitter = new(@"\r?\n|;\s+|(?<=\.)\s+", RegexOptions.Compiled);

    // "do not call" is a prohibition, not an instruction, so scrub it before looking for call verbs.
    private static readonly Regex NegatedCallVerb = new(
        @"\b(?:do not|don't|does not|doesn't|never|cannot|can't|must not|should not|instead of|without)\s+(?:call|calls|use|uses|invoke|invokes|run|runs|prefer|prefers|try|tries|attempt|attempts|start|starts|begin|begins)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CallVerb = new(
        @"\b(?:call|calls|use|uses|invoke|invokes|run|runs|prefer|prefers|try|tries|attempt|attempts|start|starts|begin|begins)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A "get_game_state -> get_available_actions -> act" chain is a call sequence with no verb.
    private static readonly Regex ToolChainArrow = new(@"->|→", RegexOptions.Compiled);

    public static void InGamePlaySurfaceKeepsNoConnectionCheck()
    {
        var play = PlayToolNames();
        Assert.True(play.Contains("act"), "AgentTools.Play must expose act.");
        Assert.False(
            play.Contains("health_check"),
            "AgentTools.Play must not expose health_check; an in-process loop cannot check its own connection.");
        Assert.True(
            McpToolNames().Contains("health_check"),
            "AgentTools.Mcp must keep health_check for the native MCP endpoint.");

        foreach (var name in ExternalOnlyToolNames)
        {
            Assert.False(play.Contains(name), name + " must stay off the in-game play surface.");
        }
    }

    public static void EmbeddedPromptOnlyInstructsPlaySurfaceTools()
    {
        AssertNoInstructedExternalTool(PlayPrompt.PlayContract, "the shared play contract");
        AssertNoInstructedExternalTool(PlayPrompt.PlaySystem, "the in-game play system prompt");
    }

    public static void McpSurfaceStillDocumentsHealthCheck()
    {
        var skill = AgentSourceFixture.Read("skills/sts2-mcp-player/SKILL.md");
        Assert.Equal(1, CountOccurrences(skill, PlayPrompt.SharedContractBegin));
        Assert.Equal(1, CountOccurrences(skill, PlayPrompt.SharedContractEnd));

        var begin = skill.IndexOf(PlayPrompt.SharedContractBegin, StringComparison.Ordinal);
        var end = skill.IndexOf(PlayPrompt.SharedContractEnd, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "Both shared contract markers must exist once each, in order.");

        var outsideTheContract = skill[..begin] + skill[(end + PlayPrompt.SharedContractEnd.Length)..];
        Assert.Contains("\"health_check\"", outsideTheContract, StringComparison.Ordinal);
    }

    private static void AssertNoInstructedExternalTool(string text, string label)
    {
        var play = PlayToolNames();
        var externalOnly = ExternalOnlyToolNames
            .Concat(McpToolNames().Where(name => !play.Contains(name)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var violations = new List<string>();
        foreach (var segment in SegmentSplitter.Split(text))
        {
            var scrubbed = NegatedCallVerb.Replace(segment, " ");
            if (!CallVerb.IsMatch(scrubbed) && !ToolChainArrow.IsMatch(segment))
            {
                continue;
            }

            foreach (var name in externalOnly)
            {
                if (ContainsWord(segment, name))
                {
                    violations.Add(name + " in \"" + segment.Trim() + "\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            label + " instructs the in-game model to call a tool its play surface cannot dispatch: "
                + string.Join(" | ", violations));
    }

    private static HashSet<string> PlayToolNames() =>
        AgentTools.Play.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> McpToolNames() =>
        AgentTools.Mcp.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var startsToken = index == 0 || !IsWordCharacter(text[index - 1]);
            var afterIndex = index + word.Length;
            var endsToken = afterIndex >= text.Length || !IsWordCharacter(text[afterIndex]);
            if (startsToken && endsToken)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
