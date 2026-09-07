using STS2AIAgent.Agent;
using System;
namespace STS2AIAgent.Tests;

internal static class McpPlayerSkillTests
{
    public static void SkillTracksLivePlayContract()
    {
        var skill = AgentSourceFixture.Read("skills/sts2-mcp-player/SKILL.md");
        var playbooks = AgentSourceFixture.Read("skills/sts2-mcp-player/references/screen-playbooks.md");
        var combined = skill + Environment.NewLine + playbooks;
        Assert.Contains(PlayPrompt.SharedContractBegin, skill, StringComparison.Ordinal);
        Assert.Contains(PlayPrompt.SharedContractEnd, skill, StringComparison.Ordinal);
        Assert.Equal(PlayPrompt.ExtractSharedContract(skill), PlayPrompt.PlayContract);
        var playSystem = PlayPrompt.PlaySystem;
        Assert.Contains(PlayPrompt.PlayContract, playSystem, StringComparison.Ordinal);
        Assert.Contains(PlayPrompt.ScreenPlaybooks, playSystem, StringComparison.Ordinal);
        Assert.Contains("same play contract as the STS2 MCP player skill", playSystem, StringComparison.Ordinal);

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
            Assert.True(PlayPrompt.PlaySystem.Contains(token, StringComparison.Ordinal), "in-game PlaySystem missing " + token);
        }
    }
}
