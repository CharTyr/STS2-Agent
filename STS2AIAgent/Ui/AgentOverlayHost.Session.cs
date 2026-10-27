using System.Collections.Generic;
using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The session card at the bottom of the play page: this session's and this run's spend, the
/// decision log, and the counter and diagnostics buttons.
/// </summary>
/// <remarks>
/// Split out of <c>AgentOverlayHost.Pages.cs</c> when the counter reset and the diagnostics moved
/// here from the multiplayer status card, which pushed the pages file past its line budget. Both
/// modes show this card, which is why those buttons belong on it.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>The decision log's three live labels: usage, run spend, and the log itself.</summary>
    private RichTextLabel? _decisionLog;
    private RichLog? _decisionLogView;
    private Label? _decisionUsage;
    private Label? _decisionRunSpend;

    /// <summary>
    /// The session tile, shown in multiplayer only (solo draws the same reading in the auto-play
    /// card), and the button that opens the log. Open state is static so a rebuild keeps it.
    /// </summary>
    private Control? _decisionUsageTile;
    private Button? _decisionLogToggle;
    private static bool _decisionLogOpen;

    /// <summary>The session card's confirmation line for the counter and diagnostics buttons.</summary>
    private Label? _sessionToolsStatus;

    /// <summary>
    /// The decision log card: this session's usage on top, then the recorded decisions newest first.
    /// Moved into the play page from its own tab.
    /// </summary>
    private Control BuildDecisionCard()
    {
        var column = UiFactory.Column();
        _decisionUsage = UiFactory.Wrapped("-", UiFactory.FontHeading, muted: false);
        _decisionRunSpend = UiFactory.Wrapped("-", UiFactory.FontHeading, muted: false);

        // Two counters stacked rather than side by side: these are full sentences ("Token 消耗：66,184
        // ..."), not metric numerals, and a side-by-side row clipped the request count off the right
        // edge in the live pass.
        _decisionUsageTile = UiFactory.MetricTile(Loc.T("本次会话"), _decisionUsage);
        column.AddChild(_decisionUsageTile);
        column.AddChild(UiFactory.MetricTile(Loc.T("本局"), _decisionRunSpend));
        _decisionLog = UiFactory.Rich();
        _decisionLog.FitContent = false;
        // Newest first: the view sticks to the top, where the line that just arrived is drawn.
        _decisionLogView = new RichLog(_decisionLog, RichLog.LogAnchor.Top);
        _decisionLog.CustomMinimumSize = new Vector2(0, 140);
        _decisionLog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Closed by default: the log is for reviewing a run, and open it pushed everything under it
        // 140 px further down a page the player scrolls mid-fight.
        _decisionLog.Visible = _decisionLogOpen;
        _decisionLogToggle = UiFactory.Button(DecisionLogToggleText(), ToggleDecisionLog, UiFactory.ButtonKind.Ghost);
        column.AddChild(_decisionLogToggle);
        column.AddChild(_decisionLog);

        // The counter reset and the diagnostics live here, on the card both modes show. They were in
        // the multiplayer status card, so a solo player had no way to export diagnostics without
        // switching modes -- which pauses play -- and the budget copy pointed at a button solo could
        // not see.
        var tools = new HFlowContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        tools.AddThemeConstantOverride("h_separation", UiFactory.SpaceXs);
        tools.AddThemeConstantOverride("v_separation", UiFactory.SpaceXs);
        _resetStatsButton = UiFactory.Button(Loc.T(SessionBudgetLimits.ResetStatsActionLabel), ResetSessionStatsFromUi, UiFactory.ButtonKind.Ghost);
        tools.AddChild(_resetStatsButton);
        tools.AddChild(UiFactory.Button(Loc.T("复制诊断"), CopyDiagnostics, UiFactory.ButtonKind.Ghost));
        tools.AddChild(UiFactory.Button(Loc.T("保存诊断"), SaveDiagnostics, UiFactory.ButtonKind.Ghost));
        column.AddChild(tools);
        _sessionToolsStatus = UiFactory.StatusLine();
        column.AddChild(_sessionToolsStatus);
        return HelpCard.Build(
            "play.decisions",
            Loc.T("决策记录"),
            Loc.T("最新的在最前。每条包含动作、理由、来源和该步消耗的 Token。\n「清零计数」只清零本次会话的请求和 Token 计数，不改上限。诊断不含 Key 和对话，反馈问题时附上。"),
            column);
    }

    private static string DecisionLogToggleText() => _decisionLogOpen ? Loc.T("收起记录") : Loc.T("展开记录");

    private void ToggleDecisionLog()
    {
        _decisionLogOpen = !_decisionLogOpen;
        if (_decisionLog != null) _decisionLog.Visible = _decisionLogOpen;
        if (_decisionLogToggle != null) _decisionLogToggle.Text = DecisionLogToggleText();
        if (_decisionLogOpen) RefreshDynamic();
    }

    /// <summary>
    /// Re-reads the usage summary and the log from <c>AgentRuntime</c>. The caller passes the
    /// player-facing view it already asked for, so the budget reason is the same text the AI teammate
    /// tab shows rather than a second reading of the budget guard.
    /// </summary>
    private void RefreshDecisionPage(PlayerFacingView facing)
    {
        if (_decisionUsage != null)
        {
            _decisionUsage.Text = PlayerFacingSession.FormatUsageSummary(
                AgentRuntime.Instance.SessionUsageKnown,
                AgentRuntime.Instance.SessionUsage,
                AgentRuntime.Instance.SessionRequests,
                facing);
        }

        if (_decisionRunSpend != null)
        {
            var spend = AgentRuntime.Instance.CurrentRunSpend();
            _decisionRunSpend.Text = PlayerFacingSession.FormatRunSpend(
                AgentRuntime.Instance.CurrentRunId ?? spend.RunId,
                spend.Decisions,
                spend.Tokens,
                spend.TokensKnown);
        }

        // A closed log is not redrawn; opening it runs a refresh pass.
        if (_decisionLogView == null || !_decisionLogOpen)
        {
            return;
        }

        var entries = AgentRuntime.Instance.RecentDecisions(DecisionLogView.RecentLimit);
        if (entries.Count == 0)
        {
            _decisionLogView.Show(new[] { Muted(Loc.T("暂无决策记录。")) + "\n" });
            return;
        }

        // Newest first, and rendered directly rather than through the pre-joined lines: the parts of
        // a line that carry meaning get their own colour, the action in the accent because it is what
        // happened. Everything is escaped, so a model that writes BBCode in its reason cannot restyle
        // the page it is quoted on. The blank line between entries is what stops fifty of them
        // reading as one paragraph.
        var lines = new List<string>(entries.Count);
        for (var offset = 0; offset < entries.Count; offset++)
        {
            var entry = entries[entries.Count - 1 - offset];
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(entry.reason))
            {
                parts.Add(Loc.T("理由：{0}", entry.reason));
            }

            parts.Add(Loc.T("来源：{0}", PlayerLabels.Source(entry.source)));
            parts.Add(entry.total_tokens is { } tokens
                ? Loc.T("Token {0}", tokens.ToString("N0"))
                : Loc.T("Token 未知"));
            // The action stays on its own line. A reason such as "Play Dismantle (14 dmg with STR 3)
            // on the 1st Inklet" plus source and token count is wider than the panel even after the
            // rich label wraps, and one joined line is what the 2026-09-22 screenshot clipped.
            lines.Add(Accent($"{entry.id}. {PlayerLabels.Action(entry.action)}") + "\n" + Muted(string.Join("\n", parts)) + "\n\n");
        }

        _decisionLogView.Show(lines);
    }
}
