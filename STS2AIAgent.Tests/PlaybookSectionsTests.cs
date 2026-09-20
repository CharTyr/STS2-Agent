using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The screen-to-guidance mapping and the slice it produces.
/// </summary>
/// <remarks>
/// The point of these contracts is that a reference file can be edited freely without a section
/// quietly ceasing to reach the model: a heading that no screen maps to has to be declared as
/// run-level or as deliberately not injected, or <see cref="EveryReferenceHeadingIsAccountedFor"/>
/// fails. That is the failure this guards against -- a new strategy section that is added, read by
/// nobody, and looks shipped.
/// </remarks>
internal static class PlaybookSectionsTests
{
    private static string Strategy => PlayPrompt.StrategyReference;

    public static void StrategyReferenceIsEmbedded()
    {
        Assert.True(Strategy.Length > 500, "the strategy reference did not load from the embedded resource");
        Assert.Contains("Route: which node to enter", Strategy);
    }

    public static void ScreenGuidanceIsLimitedToTheScreen()
    {
        var combat = PlaybookSections.ForScreen(Strategy, "COMBAT");
        Assert.Contains("Combat: what to prioritise", combat);
        Assert.Contains("Potions: when to drink", combat);
        Assert.False(
            combat.Contains("Route: which node to enter", StringComparison.Ordinal),
            "a combat turn must not be charged for the route rules");

        var map = PlaybookSections.ForScreen(Strategy, "MAP");
        Assert.Contains("Route: which node to enter", map);
        Assert.False(map.Contains("Shop: what to buy", StringComparison.Ordinal));

        var shop = PlaybookSections.ForScreen(Strategy, "SHOP");
        Assert.Contains("Shop: what to buy", shop);

        var rest = PlaybookSections.ForScreen(Strategy, "REST");
        Assert.Contains("Rest site: heal or upgrade", rest);
    }

    public static void FakeMerchantGetsTheShopGuidance()
    {
        Assert.Equal(
            PlaybookSections.ForScreen(Strategy, "SHOP"),
            PlaybookSections.ForScreen(Strategy, "FAKE_MERCHANT"));
    }

    public static void ScreensWithoutAStrategicChoiceGetNothing()
    {
        foreach (var screen in new[] { "REWARD", "CARD_SELECTION", "MODAL", "GAME_OVER", "UNLOCK", "CHEST", "MAIN_MENU" })
        {
            Assert.Equal(string.Empty, PlaybookSections.ForScreen(Strategy, screen));
        }

        Assert.Equal(string.Empty, PlaybookSections.ForScreen(Strategy, null));
        Assert.Equal(string.Empty, PlaybookSections.ForScreen(Strategy, "  "));
        Assert.Equal(string.Empty, PlaybookSections.ForScreen(Strategy, "NOT_A_SCREEN"));
    }

    public static void TheInjectionIsBounded()
    {
        var combat = PlaybookSections.ForScreen(Strategy, "COMBAT");
        Assert.True(
            combat.Length <= PlaybookSections.MaxInjectedCharacters,
            $"combat guidance is {combat.Length} characters, over the {PlaybookSections.MaxInjectedCharacters} cap");

        // A cap that cuts mid-word would be worse than no cap: the model reads a broken sentence.
        Assert.False(
            combat.EndsWith("  ", StringComparison.Ordinal),
            "the capped section must not end with trailing blank space");

        var huge = PlaybookSections.Cap(new string('x', PlaybookSections.MaxInjectedCharacters + 500));
        Assert.Equal(PlaybookSections.MaxInjectedCharacters, huge.Length);
    }

    public static void EveryMappedHeadingExistsInTheReference()
    {
        var headings = PlaybookSections.Headings(Strategy);
        foreach (var mapped in PlaybookSections.MappedHeadings())
        {
            Assert.True(
                headings.Contains(mapped),
                $"the mapping names '{mapped}', which no longer exists in strategy.md; the guidance for "
                + "that screen would silently be empty");
        }
    }

    public static void EveryReferenceHeadingIsAccountedFor()
    {
        var mapped = PlaybookSections.MappedHeadings().ToHashSet(StringComparer.Ordinal);
        var runLevel = PlaybookSections.RunLevel().ToHashSet(StringComparer.Ordinal);
        var notInjected = PlaybookSections.NotInjected();

        var unaccounted = PlaybookSections.Headings(Strategy)
            .Where(heading => !mapped.Contains(heading) && !runLevel.Contains(heading) && !notInjected.ContainsKey(heading))
            .ToArray();

        Assert.True(
            unaccounted.Length == 0,
            "these strategy.md headings reach the model on no screen and are not declared as "
            + "run-level or as deliberately not injected: " + string.Join(", ", unaccounted));

        // The other direction: a declared heading that no longer exists is a stale exemption.
        var headings = PlaybookSections.Headings(Strategy);
        foreach (var declared in notInjected.Keys)
        {
            Assert.True(
                headings.Contains(declared),
                $"'{declared}' is declared as not injected but is no longer a heading in strategy.md");
        }
    }

    public static void ScreenComesFromTheCompactPayload()
    {
        Assert.Equal("COMBAT", PlaybookSections.ScreenOfCompactState("""{"screen":"COMBAT","run":{}}"""));
        Assert.Equal("MAP", PlaybookSections.ScreenOfCompactState("""{"screen":" MAP "}""").Trim());
        Assert.Null(PlaybookSections.ScreenOfCompactState("{}"));
        Assert.Null(PlaybookSections.ScreenOfCompactState("""{"screen":null}"""));
        Assert.Null(PlaybookSections.ScreenOfCompactState("not json"));
        Assert.Null(PlaybookSections.ScreenOfCompactState(null));
        Assert.Null(PlaybookSections.ScreenOfCompactState("[]"));
    }

    public static void PlaySystemStillCarriesTheFullReferences()
    {
        // The per-screen injection is additive. Removing the full playbooks from the system prompt
        // is a real behaviour change and would need its own decision, not a side effect of this one.
        Assert.Contains(PlayPrompt.ScreenPlaybooks, PlayPrompt.PlaySystem, StringComparison.Ordinal);
        Assert.Contains(PlayPrompt.PlayContract, PlayPrompt.PlaySystem, StringComparison.Ordinal);
        Assert.False(
            PlayPrompt.PlaySystem.Contains(PlayPrompt.StrategyReference, StringComparison.Ordinal),
            "the strategy reference must not be carried whole in the system prompt");
    }
}
