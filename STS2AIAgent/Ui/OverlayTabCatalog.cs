using System;
using System.Collections.Generic;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The overlay's tabs as data: a stable id, the Chinese source label the header row shows, and
/// whether entering the tab has to re-read live state.
/// </summary>
/// <remarks>
/// The header row and <c>AgentOverlayHost.ShowTab</c> both read this list, so a tab exists in one
/// place. Adding one is an entry here plus the page builder the id selects in
/// <c>AgentOverlayHost.Tabs.cs</c>; a page whose id is missing from this list never gets a button,
/// and an id with no builder fails loudly at build time instead of showing an empty page.
///
/// This half stays free of Godot on purpose: the executable test project links it, so the tab list
/// and its labels can be pinned without a running game.
/// </remarks>
internal static class OverlayTabCatalog
{
    internal const string Chat = "chat";
    internal const string Settings = "settings";
    internal const string Play = "play";
    internal const string Dual = "dual";
    internal const string Connect = "connect";
    internal const string Decisions = "decisions";

    /// <summary>
    /// One tab: the id <c>ShowTab</c> accepts, its Chinese source label, and whether showing it
    /// re-reads state that changes without a runtime event.
    /// </summary>
    internal readonly record struct TabDefinition(string Id, string Label, bool RefreshOnShow);

    /// <summary>
    /// The tabs in the order the header row shows them. <see cref="Decisions"/> was appended rather
    /// than inserted, so the five tabs that existed before it keep their positions.
    /// </summary>
    private static readonly TabDefinition[] OrderedTabs =
    {
        new(Dual, "AI 队友", true),
        new(Chat, "对话", false),
        new(Play, "游玩", false),
        new(Settings, "设置", false),
        new(Connect, "接入", false),
        new(Decisions, "决策日志", true)
    };

    public static IReadOnlyList<TabDefinition> Tabs => OrderedTabs;

    /// <summary>
    /// The tab's label in the current language. Resolved on read, so switching the game language
    /// does not leave a label frozen in the language it was built with.
    /// </summary>
    public static string Label(string id)
    {
        var definition = Find(id);
        return definition.HasValue ? Loc.T(definition.Value.Label) : id;
    }

    /// <summary>
    /// True when the tab's content can change with nothing to subscribe to -- the game screen behind
    /// the play and teammate pages, or the decision log the panel tick owns -- so entering it has to
    /// re-read that state.
    /// </summary>
    public static bool RefreshesOnShow(string id)
    {
        var definition = Find(id);
        return definition.HasValue && definition.Value.RefreshOnShow;
    }

    private static TabDefinition? Find(string id)
    {
        foreach (var tab in OrderedTabs)
        {
            if (string.Equals(tab.Id, id, StringComparison.Ordinal))
            {
                return tab;
            }
        }

        return null;
    }
}
