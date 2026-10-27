using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Ui;

/// <summary>
/// The overlay's tab construction: the tab state, the header row, and the switch that maps a tab id
/// to the page builder that fills it.
/// </summary>
/// <remarks>
/// These members were moved here from <c>AgentOverlayHost.cs</c> unchanged. That file is the one the
/// size ratchet and the architecture table watch, and the sixth tab would otherwise have grown it
/// instead of landing next to the five pages it belongs with. The tab list itself is data in
/// <see cref="OverlayTabCatalog"/>, so which tabs exist, what they are called and which ones re-read
/// on entry live in one place that compiles offline.
///
/// The pages themselves live in <c>AgentOverlayHost.Pages.cs</c>, and the fields they write live
/// beside them: a control field declared here while its only writer is in another file is how the
/// two halves of a page end up apart.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>The built page per tab id, and the tab currently shown. Tab state stays with the tab code.</summary>
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private string _tab = OverlayTabCatalog.Play;
    private HBoxContainer? _tabRow;

    /// <summary>
    /// Builds every tab in <see cref="OverlayTabCatalog"/> order. One loop rather than one line per
    /// page, so a tab that exists in the list cannot be missing from the panel.
    /// </summary>
    private void BuildPages(Control pageHost)
    {
        foreach (var tab in OverlayTabCatalog.Tabs)
        {
            var page = BuildPageForTab(tab.Id);
            page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            pageHost.AddChild(page);
            _pages[tab.Id] = page;
        }
    }

    /// <summary>
    /// The page builder for one tab id. Every catalog entry has an arm; an id without one fails
    /// loudly here instead of showing a tab whose page area is empty.
    /// </summary>
    private Control BuildPageForTab(string tab)
    {
        return tab switch
        {
            OverlayTabCatalog.Play => BuildPlayPage(),
            OverlayTabCatalog.Settings => BuildSettingsPage(),
            _ => throw new InvalidOperationException(
                $"No page builder for tab '{tab}'. Every OverlayTabCatalog entry needs one.")
        };
    }

    private bool IsTabVisible(string tab)
    {
        return _pages.TryGetValue(tab, out var page) && page.Visible;
    }

    private void ShowTab(string tab)
    {
        if (_tab == OverlayTabCatalog.Settings && tab != OverlayTabCatalog.Settings)
        {
            FlushSettingsIfDirty();
        }

        _tab = tab;
        if (tab == OverlayTabCatalog.Play && _offerGoPlay)
        {
            // The offer has been taken; it does not need to wait on the footer for the next visit.
            _offerGoPlay = false;
            RefreshModelTestControls();
        }

        foreach (var (id, page) in _pages)
        {
            page.Visible = string.Equals(id, tab, StringComparison.Ordinal);
        }

        // The selected tab is drawn, not just remembered: without this the header row looks the same
        // on every page and the player has to read six labels to find out where they are.
        RebuildTabButtons();

        // The play page's content (conversation, decision log, Jev panel) changes with nothing to
        // subscribe to; re-read it whenever this tab comes into view.
        if (OverlayTabCatalog.RefreshesOnShow(tab)) RefreshDynamic();
    }

    /// <summary>
    /// The tab row: one underlined tab per page, sharing the panel's width.
    /// </summary>
    /// <remarks>
    /// The row is rebuilt rather than restyled when the tab changes, because a Godot
    /// <c>StyleBox</c> is set per control and swapping the buttons is cheaper to read than mutating
    /// their overrides in place. It is a couple of controls on a click, not a per-frame cost.
    ///
    /// A plain row with equal columns rather than the flow container six tabs needed: with two tabs
    /// the flow left two small pills at the left edge and an empty bar beside them, and the
    /// underline reads as one bar across the window only when the tabs fill it. If more tabs are
    /// added and stop fitting, bring the flow container back.
    /// </remarks>
    private Control BuildTabs()
    {
        _tabRow = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _tabRow.AddThemeConstantOverride("separation", 0);
        RebuildTabButtons();
        return _tabRow;
    }

    private void RebuildTabButtons()
    {
        if (_tabRow == null)
        {
            return;
        }

        foreach (var child in _tabRow.GetChildren())
        {
            _tabRow.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var tab in OverlayTabCatalog.Tabs)
        {
            var button = UiFactory.TabButton(
                OverlayTabCatalog.Label(tab.Id),
                string.Equals(tab.Id, _tab, StringComparison.Ordinal),
                () => ShowTab(tab.Id));
            // Equal columns: the underline runs the width of the window, split between the tabs.
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _tabRow.AddChild(button);
        }
    }
}
