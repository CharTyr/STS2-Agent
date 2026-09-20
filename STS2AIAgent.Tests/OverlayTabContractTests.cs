using STS2AIAgent.Agent;
using STS2AIAgent.Localization;
using STS2AIAgent.Ui;

namespace STS2AIAgent.Tests;

/// <summary>
/// The overlay's tabs are data in <see cref="OverlayTabCatalog"/> and pages in
/// <c>AgentOverlayHost.Tabs.cs</c>. These contracts keep the two halves in step. A tab with no page
/// builder is a button that switches to a blank page, and a page builder no catalog entry names is a
/// page nobody can reach; both are invisible to the compiler because the pages are Godot controls the
/// offline test project cannot compile.
/// </summary>
internal static class OverlayTabContractTests
{
    /// <summary>
    /// The file the size ratchet and the architecture table watch no longer holds the page builders,
    /// so a seventh tab lands in the tabs file instead of growing it again.
    /// </summary>
    public static void TabConstructionLivesInItsOwnFile()
    {
        var baseFile = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        var tabs = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Tabs.cs");

        Assert.Contains("private Control BuildTabs()", tabs);
        Assert.Contains("private void ShowTab(string tab)", tabs);
        Assert.Contains("private Control BuildDecisionPage()", tabs);
        Assert.False(
            baseFile.Contains("private Control BuildTabs()", StringComparison.Ordinal),
            "The tab construction must stay in AgentOverlayHost.Tabs.cs, or the next tab grows the file again.");
        Assert.False(
            baseFile.Contains("private void ShowTab(string tab)", StringComparison.Ordinal),
            "ShowTab belongs with the tab construction it switches between.");

        // ShowTab keeps its string call sites in the base file: the startup tab is chosen from runtime
        // state, and those call sites are the overlay's, not the tabs file's.
        Assert.Contains("ShowTab(\"dual\")", baseFile);
        Assert.Contains("ShowTab(\"play\")", baseFile);
        Assert.Contains("ShowTab(\"settings\")", baseFile);
    }

    /// <summary>
    /// The five tabs that existed before the decision log keep their ids and positions, so showing
    /// them is a move and not a reshuffle.
    /// </summary>
    public static void ExistingTabsKeepTheirOrder()
    {
        var ids = OverlayTabCatalog.Tabs.Select(tab => tab.Id).ToArray();

        Assert.Equal(6, ids.Length);
        Assert.Equal(OverlayTabCatalog.Dual, ids[0]);
        Assert.Equal(OverlayTabCatalog.Chat, ids[1]);
        Assert.Equal(OverlayTabCatalog.Play, ids[2]);
        Assert.Equal(OverlayTabCatalog.Settings, ids[3]);
        Assert.Equal(OverlayTabCatalog.Connect, ids[4]);
        Assert.Equal(OverlayTabCatalog.Decisions, ids[5]);
    }

    /// <summary>Every catalog entry has a page builder, and every page builder has a catalog entry.</summary>
    public static void EveryTabHasAPageBuilder()
    {
        var tabs = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Tabs.cs");
        var builders = AgentSourceFixture.MethodBody(tabs, "BuildPageForTab");

        foreach (var tab in OverlayTabCatalog.Tabs)
        {
            Assert.Contains($"OverlayTabCatalog.{ConstantFor(tab.Id)} =>", builders);
        }

        Assert.Equal(OverlayTabCatalog.Tabs.Count, CountOccurrences(builders, "=> Build"));
    }

    /// <summary>
    /// A tab whose content changes with nothing to subscribe to re-reads on entry. The decision log is
    /// one of those: an action submitted over the HTTP API records a decision without raising the
    /// runtime's Changed event, so relying on that event alone would leave the page stale.
    /// </summary>
    public static void TabsThatChangeWithoutAnEventRefreshOnShow()
    {
        Assert.True(OverlayTabCatalog.RefreshesOnShow(OverlayTabCatalog.Dual));
        Assert.True(OverlayTabCatalog.RefreshesOnShow(OverlayTabCatalog.Decisions));
        Assert.False(OverlayTabCatalog.RefreshesOnShow(OverlayTabCatalog.Chat));
        Assert.False(OverlayTabCatalog.RefreshesOnShow(OverlayTabCatalog.Play));
        Assert.False(OverlayTabCatalog.RefreshesOnShow("no-such-tab"));
    }

    /// <summary>
    /// The catalog labels are the only tab text the <c>Loc</c> call-site scan cannot see, because they
    /// are data rather than a <c>Loc.T("...")</c> literal. Pin them here: every tab label has to have
    /// an English entry, which is what makes the header row readable on an English client.
    /// </summary>
    public static void CatalogLabelsResolveToEnglish()
    {
        try
        {
            Loc.SetLanguage("eng");
            foreach (var tab in OverlayTabCatalog.Tabs)
            {
                var label = OverlayTabCatalog.Label(tab.Id);
                Assert.False(
                    string.Equals(label, tab.Label, StringComparison.Ordinal),
                    $"The {tab.Id} tab has no English label: it still reads \"{label}\".");
            }
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }
    }

    /// <summary>
    /// The log updates while auto-play runs, with no manual tab switch: the runtime's Changed event
    /// drives RefreshDynamic, which refreshes the page, and the page that is already open also
    /// re-reads on the panel tick. Both read the runtime's own snapshot -- never a second store and
    /// never the JSONL file, which would let the view and the log disagree.
    /// </summary>
    public static void DecisionLogFollowsTheLiveRefreshPath()
    {
        var overlay = AgentSourceFixture.ReadOverlayHost();

        var refresh = AgentSourceFixture.MethodBody(overlay, "RefreshDynamic");
        Assert.Contains("RefreshDecisionPage(facing);", refresh);

        var tick = AgentSourceFixture.MethodBody(overlay, "OnProcessFrame");
        Assert.Contains("if (IsTabVisible(OverlayTabCatalog.Decisions))", tick);
        Assert.Contains("RefreshDecisionPage(AgentRuntime.Instance.PlayerFacing());", tick);

        var page = AgentSourceFixture.MethodBody(overlay, "RefreshDecisionPage");
        Assert.Contains("AgentRuntime.Instance.RecentDecisions(DecisionLogView.RecentLimit)", page);
        Assert.False(
            page.Contains("decisions.jsonl", StringComparison.Ordinal),
            "The overlay must render the runtime's in-memory snapshot, not the decision JSONL file.");
    }

    /// <summary>
    /// The page shows this session's usage from the runtime's own counters, and the budget reason from
    /// the player-facing view it is handed -- the same text the AI teammate tab shows instead of a
    /// second reading of the budget guard.
    /// </summary>
    public static void DecisionPageReusesTheRuntimeCounters()
    {
        var overlay = AgentSourceFixture.ReadOverlayHost();
        var page = AgentSourceFixture.MethodBody(overlay, "RefreshDecisionPage");

        Assert.Contains("PlayerFacingSession.FormatUsageSummary(", page);
        Assert.Contains("AgentRuntime.Instance.SessionUsageKnown", page);
        Assert.Contains("AgentRuntime.Instance.SessionUsage", page);
        Assert.Contains("AgentRuntime.Instance.SessionRequests", page);
        Assert.False(
            page.Contains("CheckBudget", StringComparison.Ordinal),
            "The budget reason is carried by the player-facing view; the overlay must not recompute it.");
    }

    /// <summary>
    /// Newest first, one line per decision, and a step whose service returned no usage reads as
    /// unknown rather than as 0 -- those are different facts about the same session.
    /// </summary>
    public static void DecisionLinesAreNewestFirstAndUnknownUsageStaysUnknown()
    {
        var entries = new[]
        {
            new DecisionLogEntry(1, "2026-09-20T00:00:00.0000000+00:00", "http_api", "play_card", "先补防", "fp", 1, 900, "R1"),
            new DecisionLogEntry(2, "2026-09-20T00:00:05.0000000+00:00", "agent_loop", "end_turn", null, "fp", 1, null, "R1")
        };

        var lines = DecisionLogView.Lines(entries);

        Assert.Equal(2, lines.Count);
        Assert.Contains("2. end_turn", lines[0]);
        Assert.Contains("agent_loop", lines[0]);
        Assert.Contains("未知", lines[0]);
        Assert.False(
            lines[0].Contains("本次 Token：0", StringComparison.Ordinal),
            "A step with no usage must not be reported as having spent nothing.");
        Assert.Contains("1. play_card", lines[1]);
        Assert.Contains("先补防", lines[1]);
        Assert.Contains("http_api", lines[1]);
        Assert.Contains("900", lines[1]);
    }

    /// <summary>An empty store reads as empty, not as a line about nothing.</summary>
    public static void NoDecisionsProducesNoLines()
    {
        Assert.Equal(0, DecisionLogView.Lines(Array.Empty<DecisionLogEntry>()).Count);
    }

    /// <summary>The page is bounded: a long session shows the newest decisions, not all of them.</summary>
    public static void DecisionLinesStayBounded()
    {
        var entries = new List<DecisionLogEntry>();
        for (var id = 1; id <= DecisionLogView.RecentLimit + 10; id++)
        {
            entries.Add(new DecisionLogEntry(id, "2026-09-20T00:00:00.0000000+00:00", "agent_loop", "play_card", null, null, 1, 10, "R1"));
        }

        var lines = DecisionLogView.Lines(entries);

        Assert.Equal(DecisionLogView.RecentLimit, lines.Count);
        Assert.Contains($"{DecisionLogView.RecentLimit + 10}. play_card", lines[0]);
    }

    private static string ConstantFor(string id)
    {
        return char.ToUpperInvariant(id[0]) + id[1..];
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        for (var index = source.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
