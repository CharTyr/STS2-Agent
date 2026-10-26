using STS2AIAgent.Agent;
using System;
using System.Text;
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

    public static void SharedContractIsIndependentOfLineEndings()
    {
        const string expected = "First paragraph.\n\n  Indented guidance.\nLast line.";
        var skill = "# Outside the contract\n" + PlayPrompt.SharedContractBegin + "\n\n"
            + expected + "\n\n" + PlayPrompt.SharedContractEnd + "\nNot part of the contract.\n";

        foreach (var variant in new[]
                 {
                     skill,
                     skill.Replace("\n", "\r\n", StringComparison.Ordinal),
                     skill.Replace("\n", "\r", StringComparison.Ordinal),
                     skill.Replace("\n\n", "\r\n\n", StringComparison.Ordinal)
                 })
        {
            Assert.Equal(expected, PlayPrompt.ExtractSharedContract(variant),
                "the shared contract must keep the same content for CRLF, CR, and mixed line endings");
        }
    }

    public static void PromptResourceReaderNormalizesLineEndings()
    {
        // Exercise the production StreamReader boundary with CRLF bytes even on an LF checkout.
        const string expected = "\nFirst line: 战斗.\n\n  Indented guidance.\nLast line.\n";
        foreach (var variant in new[]
                 {
                     expected,
                     expected.Replace("\n", "\r\n", StringComparison.Ordinal),
                     expected.Replace("\n", "\r", StringComparison.Ordinal),
                     expected.Replace("\n\n", "\r\n\n", StringComparison.Ordinal)
                 })
        {
            foreach (var withBom in new[] { false, true })
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom);
                using var stream = new MemoryStream(utf8.GetPreamble().Concat(utf8.GetBytes(variant)).ToArray());
                Assert.Equal(expected, PlayPrompt.ReadPromptResource(stream),
                    "the production resource reader must preserve UTF-8 content and whitespace while normalizing newlines");
            }
        }
    }

    public static void EmbeddedPromptsUseCanonicalLineEndings()
    {
        foreach (var (path, actual) in new[]
                 {
                     ("skills/sts2-mcp-player/references/screen-playbooks.md", PlayPrompt.ScreenPlaybooks),
                     ("skills/sts2-mcp-player/references/strategy.md", PlayPrompt.StrategyReference)
                 })
        {
            Assert.Equal(AgentSourceFixture.Read(path).ReplaceLineEndings("\n"), actual,
                "embedded prompt content must be preserved apart from newline normalization: " + path);
        }

        foreach (var text in new[]
                 {
                     PlayPrompt.PlayContract, PlayPrompt.ScreenPlaybooks,
                     PlayPrompt.StrategyReference, PlayPrompt.PlaySystem
                 })
        {
            Assert.True(text.Contains('\n'), "the prompt fixture must contain multiple lines");
            Assert.False(text.Contains('\r'), "embedded resources and the assembled prompt must use LF");
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
