using STS2AIAgent.Agent;
using System;
namespace STS2AIAgent.Tests;

internal static class McpPlayerSkillTests
{
    /// <summary>
    /// The skill on disk and the in-game prompt stay one contract.
    /// </summary>
    /// <remarks>
    /// The contract these tests pin changed on 2026-09-21: the in-game system prompt carries the
    /// per-screen slice, and the full reference documents stay on disk for the skill (and stay
    /// reachable over MCP as its resources). Reachability is therefore asserted against everything
    /// the in-game loop can inject -- the system prompt plus every screen's slices -- rather than
    /// against the system prompt alone, which used to carry both documents whole.
    /// </remarks>
    public static void SkillTracksLivePlayContract()
    {
        var skill = AgentSourceFixture.Read("skills/sts2-mcp-player/SKILL.md");
        var playbooks = AgentSourceFixture.Read("skills/sts2-mcp-player/references/screen-playbooks.md");
        var strategy = AgentSourceFixture.Read("skills/sts2-mcp-player/references/strategy.md");
        var combined = skill + Environment.NewLine + playbooks + Environment.NewLine + strategy;
        Assert.Contains(PlayPrompt.SharedContractBegin, skill, StringComparison.Ordinal);
        Assert.Contains(PlayPrompt.SharedContractEnd, skill, StringComparison.Ordinal);
        Assert.Equal(PlayPrompt.ExtractSharedContract(skill), PlayPrompt.PlayContract);
        var playSystem = PlayPrompt.PlaySystem;
        Assert.Contains(PlayPrompt.PlayContract, playSystem, StringComparison.Ordinal);
        Assert.Contains("same play contract as the STS2 MCP player skill", playSystem, StringComparison.Ordinal);

        // The contract the in-game prompt is: the per-screen slice, not the whole document.
        Assert.Contains("playbook section for the current screen", playSystem, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            playSystem.Contains(PlayPrompt.ScreenPlaybooks, StringComparison.Ordinal),
            "the full screen playbooks must stay on disk for the skill, not ride in every play step");

        var reachableInGame = InGamePromptText();
        foreach (var token in new[]
                 {
                     "continue_game_over",
                     "confirm_unlock",
                     "choose_capstone_option",
                     "choose_bundle",
                     "confirm_bundle",
                     "wait_until_actionable",
                     "get_raw_game_state",
                     "crystal_clear_cell",
                     "local_vote"
                 })
        {
            Assert.True(combined.Contains(token, StringComparison.Ordinal), "mcp skill missing " + token);
            Assert.True(
                reachableInGame.Contains(token, StringComparison.Ordinal),
                "no in-game prompt text reaches " + token
                + "; if it moved out of the shared contract it has to be in a screen's slice");
        }
    }

    /// <summary>
    /// Everything the in-game loop can put in front of the model: the system prompt plus the slice
    /// every screen is given, from both references. A token missing here reaches no in-game decision
    /// on any screen, which is how a section can be "shipped" without ever being read.
    /// </summary>
    private static string InGamePromptText()
    {
        var text = new System.Text.StringBuilder(PlayPrompt.PlaySystem);
        foreach (var screen in PlaybookSections.Playbooks.Screens)
        {
            text.Append('\n').Append(PlayPrompt.PlaybookGuidance(screen));
            text.Append('\n').Append(PlayPrompt.ScreenGuidance(screen));
        }

        foreach (var screen in PlaybookSections.Strategy.Screens)
        {
            text.Append('\n').Append(PlayPrompt.ScreenGuidance(screen));
        }

        return text.ToString();
    }
}
