using System;
namespace STS2AIAgent.Tests;

internal static class McpPlayerSkillTests
{
    public static void SkillTracksLivePlayContract()
    {
        var skill = AgentSourceFixture.Read("skills/sts2-mcp-player/SKILL.md");
        var playbooks = AgentSourceFixture.Read("skills/sts2-mcp-player/references/screen-playbooks.md");
        var prompt = AgentSourceFixture.Read("STS2AIAgent/Agent/PlayPrompt.cs");
        var combined = skill + Environment.NewLine + playbooks;
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
            Assert.True(prompt.Contains(token, StringComparison.Ordinal), "PlayPrompt missing " + token);
        }
    }
}
