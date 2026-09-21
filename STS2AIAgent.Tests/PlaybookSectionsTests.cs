using System.Text.RegularExpressions;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The screen-to-guidance mappings and the slices they produce, for both shared references.
/// </summary>
/// <remarks>
/// The point of these contracts is that a reference file can be edited freely without a section
/// quietly ceasing to reach the model: a heading that no screen maps to has to be declared as
/// run-level or as deliberately not injected, or <see cref="EveryReferenceHeadingIsAccountedFor"/>
/// fails. That is the failure this guards against -- a new strategy section that is added, read by
/// nobody, and looks shipped.
///
/// <see cref="EveryPlaybookHeadingIsAccountedFor"/> is the same ratchet for the screen playbooks,
/// which now travel one screen at a time instead of riding whole in <see cref="PlayPrompt.PlaySystem"/>.
/// </remarks>
internal static class PlaybookSectionsTests
{
    private static string Strategy => PlayPrompt.StrategyReference;

    private static string Playbooks => PlayPrompt.ScreenPlaybooks;

    /// <summary>A quoted string in the resolver that is nothing but a screen name.</summary>
    private static readonly Regex ScreenLiteral = new("\"([A-Z][A-Z0-9_]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// A backticked field reference in the strategy text. The file names fields this way and no
    /// other, so this is the set of names a model is told to read.
    /// </summary>
    private static readonly Regex CompactField = new("`([^`]+)`", RegexOptions.Compiled);

    /// <summary>
    /// Raw <c>/state</c> paths and flags that compact <c>agent_view</c> renames or drops. A
    /// strategy span matching one of these is a read of a key the model will not find. Mentioning
    /// the raw spelling in prose ("raw /state spells it is_enabled") does not match, because that
    /// sentence does not wrap the name as a field path.
    /// </summary>
    private static readonly Regex RawCompactField = new(
        @"^(run\.(current_hp|max_hp)|map\.(available_nodes|player_votes)|shop\.card_removal|event\.event_id|rest\.options\[\]\.is_enabled)$|^(is_locked|is_enabled|is_proceed|will_kill_player|enough_gold|is_stocked|can_use|is_queued|occupied)$",
        RegexOptions.Compiled);

    public static void StrategyReferenceIsEmbedded()
    {
        Assert.True(Strategy.Length > 500, "the strategy reference did not load from the embedded resource");
        Assert.Contains("Route: which node to enter", Strategy);
    }

    public static void ScreenGuidanceIsLimitedToTheScreen()
    {
        var combat = PlaybookSections.Strategy.Slice(Strategy, "COMBAT");
        Assert.Contains("Combat: what to prioritise", combat);
        Assert.Contains("Potions: when to drink", combat);
        Assert.False(
            combat.Contains("Route: which node to enter", StringComparison.Ordinal),
            "a combat turn must not be charged for the route rules");

        var map = PlaybookSections.Strategy.Slice(Strategy, "MAP");
        Assert.Contains("Route: which node to enter", map);
        Assert.False(map.Contains("Shop: what to buy", StringComparison.Ordinal));

        var shop = PlaybookSections.Strategy.Slice(Strategy, "SHOP");
        Assert.Contains("Shop: what to buy", shop);

        var rest = PlaybookSections.Strategy.Slice(Strategy, "REST");
        Assert.Contains("Rest site: heal or upgrade", rest);

        // The event screen gets the option rules, which is where 1.2's risk vocabulary is actionable.
        var @event = PlaybookSections.Strategy.Slice(Strategy, "EVENT");
        Assert.Contains("Event options: how to choose", @event);
        Assert.False(@event.Contains("Shop: what to buy", StringComparison.Ordinal));
    }

    public static void FakeMerchantGetsTheShopGuidance()
    {
        Assert.Equal(
            PlaybookSections.Strategy.Slice(Strategy, "SHOP"),
            PlaybookSections.Strategy.Slice(Strategy, "FAKE_MERCHANT"));
    }

    public static void ScreensWithoutAStrategicChoiceGetNothing()
    {
        foreach (var screen in new[] { "REWARD", "CARD_SELECTION", "MODAL", "GAME_OVER", "UNLOCK", "CHEST", "MAIN_MENU" })
        {
            Assert.Equal(string.Empty, PlaybookSections.Strategy.Slice(Strategy, screen));
        }

        Assert.Equal(string.Empty, PlaybookSections.Strategy.Slice(Strategy, null));
        Assert.Equal(string.Empty, PlaybookSections.Strategy.Slice(Strategy, "  "));
        Assert.Equal(string.Empty, PlaybookSections.Strategy.Slice(Strategy, "NOT_A_SCREEN"));
    }

    public static void TheInjectionIsBounded()
    {
        var combat = PlaybookSections.Strategy.Slice(Strategy, "COMBAT");
        Assert.True(
            combat.Length <= PlaybookSections.MaxInjectedCharacters,
            $"combat guidance is {combat.Length} characters, over the {PlaybookSections.MaxInjectedCharacters} cap");

        // A cap that cuts mid-word would be worse than no cap: the model reads a broken sentence.
        Assert.False(
            combat.EndsWith("  ", StringComparison.Ordinal),
            "the capped section must not end with trailing blank space");

        var huge = PlaybookSections.Cap(new string('x', PlaybookSections.MaxInjectedCharacters + 500));
        Assert.Equal(PlaybookSections.MaxInjectedCharacters, huge.Length);

        // The playbook slices answer to the same budget.
        foreach (var screen in PlaybookSections.Playbooks.Screens)
        {
            var slice = PlayPrompt.PlaybookGuidance(screen);
            Assert.True(
                slice.Length > 0 && slice.Length <= PlaybookSections.MaxInjectedCharacters,
                $"the playbook slice for {screen} is {slice.Length} characters; expected 1..{PlaybookSections.MaxInjectedCharacters}");
        }
    }

    public static void EveryMappedHeadingExistsInTheReference()
    {
        AssertHeadingsExist(PlaybookSections.Strategy, Strategy, "strategy.md");
        AssertHeadingsExist(PlaybookSections.Playbooks, Playbooks, "screen-playbooks.md");
    }

    public static void EveryReferenceHeadingIsAccountedFor()
    {
        AssertHeadingsAccountedFor(PlaybookSections.Strategy, Strategy, "strategy.md");
    }

    /// <summary>
    /// The same accounting for the screen playbooks. A section with no screen mapping is a sequence
    /// no in-game decision ever receives, and the file is now sliced rather than carried whole.
    /// </summary>
    public static void EveryPlaybookHeadingIsAccountedFor()
    {
        AssertHeadingsAccountedFor(PlaybookSections.Playbooks, Playbooks, "screen-playbooks.md");
    }

    /// <summary>
    /// The two directions of the screen-name contract, read from the resolver's own source: a mapped
    /// screen the game never reports is guidance that can no longer be reached, and a name the game
    /// can report that is neither mapped nor declared drops to the index fallback -- the way a
    /// renamed enum value would silently cost the agent its action sequence for that screen.
    /// </summary>
    public static void EveryPlaybookScreenIsOneTheGameCanReport()
    {
        var reported = ScreensTheGameCanReport();
        Assert.True(reported.Count >= 25, $"only {reported.Count} screen names were read from the resolver");

        var unreachable = PlaybookSections.Playbooks.Screens
            .Where(screen => !reported.Contains(screen))
            .ToArray();
        Assert.True(
            unreachable.Length == 0,
            "the playbook mapping names screens GameStateService can never report, so that guidance "
            + "reaches no in-game decision: " + string.Join(", ", unreachable));

        var mapped = PlaybookSections.Playbooks.Screens.ToHashSet(StringComparer.Ordinal);
        var fallback = PlaybookSections.PlaybookIndexFallbackScreens;
        var unaccounted = reported
            .Where(screen => !mapped.Contains(screen) && !fallback.ContainsKey(screen))
            .ToArray();
        Assert.True(
            unaccounted.Length == 0,
            "GameStateService can report these screens and nothing says what playbook they get: "
            + string.Join(", ", unaccounted)
            + ". Map them, or declare them in PlaybookIndexFallbackScreens with the reason the index "
            + "fallback is the right answer.");

        foreach (var declared in fallback.Keys)
        {
            Assert.True(
                reported.Contains(declared),
                $"'{declared}' is declared as an index-fallback screen but GameStateService no longer reports it");
        }
    }

    public static void ScreenComesFromTheCompactPayload()
    {
        Assert.Equal("COMBAT", PlaybookSections.ScreenOfCompactState("""{"screen":"COMBAT","run":{}}"""));
        // The raw value is returned as-is; the mapping is what trims before looking a screen up.
        Assert.Equal(" MAP ", PlaybookSections.ScreenOfCompactState("""{"screen":" MAP "}"""));
        Assert.Contains("Route: which node to enter", PlaybookSections.Strategy.Slice(Strategy, " MAP "));
        Assert.Null(PlaybookSections.ScreenOfCompactState("{}"));
        Assert.Null(PlaybookSections.ScreenOfCompactState("""{"screen":null}"""));
        Assert.Null(PlaybookSections.ScreenOfCompactState("not json"));
        Assert.Null(PlaybookSections.ScreenOfCompactState(null));
        Assert.Null(PlaybookSections.ScreenOfCompactState("[]"));
    }

    /// <summary>
    /// The playbook side of the slice: the screen's own section, and none of the other 19.
    /// </summary>
    public static void PlaybookSliceIsLimitedToTheScreen()
    {
        var combat = PlayPrompt.PlaybookGuidance("COMBAT");
        Assert.Contains("## COMBAT", combat);
        Assert.Contains("Stay inside `play_card`", combat);
        // Potion targeting is a combat action, so its rules travel with the combat section.
        Assert.Contains("## Potion Targeting", combat);
        Assert.False(
            combat.Contains("## CHEST", StringComparison.Ordinal),
            "a combat turn must not be charged for the chest sequence");
        Assert.False(combat.Contains("## EVENT", StringComparison.Ordinal));

        var chest = PlayPrompt.PlaybookGuidance("CHEST");
        Assert.Contains("## CHEST", chest);
        Assert.Contains("choose_treasure_relic", chest);
        Assert.False(chest.Contains("## COMBAT", StringComparison.Ordinal));

        // Every screen named together in one heading gets that heading.
        foreach (var screen in new[] { "MODAL", "GAME_OVER", "UNLOCK" })
        {
            Assert.Contains("## MODAL, GAME_OVER, and UNLOCK", PlayPrompt.PlaybookGuidance(screen));
        }

        // The in-run pause pages share one section because they share one exit rule.
        Assert.Contains("## In-Run Menu Pages", PlayPrompt.PlaybookGuidance("STATS"));
        Assert.Contains("## In-Run Menu Pages", PlayPrompt.PlaybookGuidance("PAUSE_MENU"));
    }

    /// <summary>
    /// The documented fallback: a screen with no section of its own is never told nothing, because
    /// the playbook carries the play contract for the screen. It gets the index of the sections --
    /// bounded, and enough for the model to tell a gap in the mapping from "nothing to know here".
    /// </summary>
    public static void UnknownScreenGetsTheSectionIndexNotNothing()
    {
        foreach (var screen in new[] { "NOT_A_SCREEN", "UNKNOWN", "MULTIPLAYER_LOBBY", "MULTIPLAYER_LOAD", null, "  " })
        {
            var slice = PlayPrompt.PlaybookGuidance(screen);
            Assert.True(slice.Length > 0, "the playbook injection must never be empty for " + (screen ?? "null"));
            Assert.Contains("MAIN_MENU and Timeline", slice);
            Assert.Contains("CRYSTAL_SPHERE", slice);
            Assert.Contains("shared play contract", slice);
            Assert.True(
                slice.Length < Playbooks.Length / 4,
                "the index fallback must stay a small fraction of the document, not a quiet way to send it whole");
        }

        Assert.False(
            PlayPrompt.PlaybookGuidance("COMBAT").Contains("No section of this playbook matches", StringComparison.Ordinal),
            "a screen that has a section must get the section, not the index");
    }

    /// <summary>
    /// The prompt contract this change is: <see cref="PlayPrompt.PlaySystem"/> carries the shared play
    /// contract and the per-screen slices are injected beside it, while the full documents stay on
    /// disk for the skill (and stay reachable over MCP as
    /// <c>sts2://skill/screen-playbooks</c>). The system prompt used to carry the whole playbook
    /// document -- ~3,070 tokens -- on every play step, one screen at a time being all any decision
    /// can use.
    /// </summary>
    public static void PlaySystemCarriesTheSliceNotTheDocuments()
    {
        Assert.Contains(PlayPrompt.PlayContract, PlayPrompt.PlaySystem, StringComparison.Ordinal);
        Assert.False(
            PlayPrompt.PlaySystem.Contains(PlayPrompt.ScreenPlaybooks, StringComparison.Ordinal),
            "the screen playbooks must not be carried whole in the system prompt; only the current "
            + "screen's section is injected (PlayPrompt.PlaybookGuidance)");
        Assert.False(
            PlayPrompt.PlaySystem.Contains(PlayPrompt.StrategyReference, StringComparison.Ordinal),
            "the strategy reference must not be carried whole in the system prompt");

        // The full documents are still the skill's, and are still what an MCP client reads.
        var playbookResource = PlayPrompt.SkillResources.Single(resource => resource.Uri == "sts2://skill/screen-playbooks");
        Assert.Equal(PlayPrompt.ScreenPlaybooks, playbookResource.Text);
        Assert.Contains("## CHEST", playbookResource.Text);
    }

    /// <summary>
    /// A ratchet on the prompt that is re-sent on every step of an autoplay session.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-21 by reading <see cref="PlayPrompt.PlaySystem"/> and the slices it is
    /// injected beside: the system prompt is 11,557 characters (~3,300 tokens) against 21,992
    /// (~6,280) while it carried the screen playbooks whole, of which 10,748 were that document. A
    /// COMBAT step's static block (system prompt + 907-character playbook slice + strategy) is
    /// 14,577 characters (~4,165 tokens) against 24,105 (~6,890) before; the largest slice is
    /// MAIN_MENU at 1,194 and the index fallback is 622. The budget is the ratchet: the saving is a
    /// property of the design, so a section that grows past it is a decision, not a drift.
    /// </remarks>
    /// <summary>
    /// The strategy text is injected beside a compact <c>agent_view</c>, on both the in-game loop
    /// and <c>get_scene_guidance</c>. A rule that names a raw <c>/state</c> field as the thing to
    /// read sends the model after a key that view does not have: <c>run.current_hp</c>,
    /// <c>map.available_nodes</c>, <c>shop.card_removal</c>, <c>event.event_id</c>. The rename is
    /// documented, but the model is handed this file, not the rename table.
    /// </summary>
    /// <remarks>
    /// The check is the backtick span, which is how this file names a field. A sentence that says
    /// the raw payload spells a flag differently is allowed to mention the raw name; a span that
    /// tells the model to read <c>run.current_hp</c> is not. The two are distinguished by whether
    /// the span itself is a raw path.
    /// </remarks>
    public static void StrategyNamesCompactFieldsNotRawOnes()
    {
        var strategy = AgentSourceFixture.Read("skills/sts2-mcp-player/references/strategy.md");
        foreach (var screen in new[] { "MAP", "REST", "SHOP", "EVENT", "COMBAT" })
        {
            var slice = PlaybookSections.Strategy.Slice(strategy, screen);
            Assert.True(slice.Length > 0, $"the {screen} strategy slice is empty; the field check would pass vacuously");
            foreach (Match field in CompactField.Matches(slice))
            {
                var name = field.Groups[1].Value;
                Assert.False(
                    RawCompactField.IsMatch(name),
                    $"the {screen} strategy tells the model to read `{name}`, which compact agent_view does not have");
            }
        }

        var map = PlaybookSections.Strategy.Slice(strategy, "MAP");
        Assert.Contains("map.options[]", map, StringComparison.Ordinal);
        Assert.Contains("run.hp", map, StringComparison.Ordinal);
        Assert.Contains("shop.remove", map, StringComparison.Ordinal);
        Assert.Contains("map.votes[]", map, StringComparison.Ordinal);

        var rest = PlaybookSections.Strategy.Slice(strategy, "REST");
        Assert.Contains("`enabled`", rest, StringComparison.Ordinal);

        var shop = PlaybookSections.Strategy.Slice(strategy, "SHOP");
        Assert.Contains("`affordable`", shop, StringComparison.Ordinal);
        Assert.Contains("shop.remove", shop, StringComparison.Ordinal);

        var @event = PlaybookSections.Strategy.Slice(strategy, "EVENT");
        Assert.Contains("`locked`", @event, StringComparison.Ordinal);
        Assert.Contains("`kill`", @event, StringComparison.Ordinal);
        Assert.Contains("event.id", @event, StringComparison.Ordinal);
    }

    public static void TheStaticPromptStaysUnderItsBudget()
    {
        const int systemBudget = 12_000;
        const int stepBudget = 15_000;

        Assert.True(
            PlayPrompt.PlaySystem.Length <= systemBudget,
            $"the system prompt is {PlayPrompt.PlaySystem.Length} characters, over its {systemBudget} budget");

        foreach (var screen in new[] { "COMBAT", "MAP", "EVENT", "MAIN_MENU", "CARD_SELECTION", "UNKNOWN" })
        {
            var perStep = PlayPrompt.PlaySystem.Length
                + PlayPrompt.PlaybookGuidance(screen).Length
                + PlayPrompt.ScreenGuidance(screen).Length;
            Assert.True(
                perStep <= stepBudget,
                $"the static part of a {screen} step is {perStep} characters, over its {stepBudget} budget");
        }
    }

    private static void AssertHeadingsExist(PlaybookSections.Reference reference, string markdown, string file)
    {
        var headings = PlaybookSections.Headings(markdown);
        foreach (var mapped in reference.MappedHeadings())
        {
            Assert.True(
                headings.Contains(mapped),
                $"the mapping names '{mapped}', which no longer exists in {file}; the guidance for "
                + "that screen would silently be empty");
        }
    }

    private static void AssertHeadingsAccountedFor(PlaybookSections.Reference reference, string markdown, string file)
    {
        var mapped = reference.MappedHeadings().ToHashSet(StringComparer.Ordinal);
        var runLevel = reference.RunLevel().ToHashSet(StringComparer.Ordinal);
        var notInjected = reference.NotInjected();

        var unaccounted = PlaybookSections.Headings(markdown)
            .Where(heading => !mapped.Contains(heading) && !runLevel.Contains(heading) && !notInjected.ContainsKey(heading))
            .ToArray();

        Assert.True(
            unaccounted.Length == 0,
            "these " + file + " headings reach the model on no screen and are not declared as "
            + "run-level or as deliberately not injected: " + string.Join(", ", unaccounted));

        // The other direction: a declared heading that no longer exists is a stale exemption.
        var headings = PlaybookSections.Headings(markdown);
        foreach (var declared in notInjected.Keys)
        {
            Assert.True(
                headings.Contains(declared),
                $"'{declared}' is declared as not injected but is no longer a heading in " + file);
        }
    }

    /// <summary>
    /// Every screen name <c>GameStateService</c> can return, read out of the two resolver bodies
    /// rather than copied: the copy is what would drift.
    /// </summary>
    private static HashSet<string> ScreensTheGameCanReport()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in new[] { "ResolveScreen", "ResolveNonModalScreen" })
        {
            foreach (Match match in ScreenLiteral.Matches(AgentSourceFixture.MethodBody(source, method)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }
}
