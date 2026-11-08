using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The play page's run report: the scorecard of the run that just ended, with a copy button and the
/// action that clears the recorded runs.
/// </summary>
/// <remarks>
/// The card is fed by <see cref="AgentRuntime.LastRunSummary"/>, which only exists after a GAME_OVER
/// frame was observed (see <c>AgentRuntime.RunSummary.cs</c>), so it cannot appear for an abandoned or
/// paused run. It was built as its own file for two reasons: the play page was already the largest
/// builder in the overlay, and the card follows the runtime's change notification instead of joining
/// the 800 ms page tick -- a finished run is a once-per-run event, and the tick that repaints the
/// decisions would otherwise have to know about it.
///
/// The handler is bound to the card's own lifetime: the subscription is dropped when this exact card
/// leaves the tree (a theme or language rebuild), so a freed control is never repainted and a stale
/// handler can never unsubscribe its replacement.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    private Control? _runSummaryCard;
    private Label? _runSummaryLines;
    private Label? _runSummaryStatus;

    /// <summary>The run whose report was already revealed, so a rebuild does not reopen it.</summary>
    private string? _revealedRunId;

    /// <summary>
    /// The 「本轮成绩单」 card: this run's facts, then a copy button and the clear action, under the
    /// usual help panel. Hidden while nothing has been recorded, so the play page is unchanged for a
    /// player who has not finished a run with the AI yet.
    /// </summary>
    private Control BuildRunSummaryCard()
    {
        var column = UiFactory.Column();
        _runSummaryLines = UiFactory.Wrapped("-", UiFactory.FontBody, muted: false);
        column.AddChild(_runSummaryLines);

        var tools = new HFlowContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        tools.AddThemeConstantOverride("h_separation", UiFactory.SpaceXs);
        tools.AddThemeConstantOverride("v_separation", UiFactory.SpaceXs);
        tools.AddChild(UiFactory.Button(Loc.T("复制成绩单"), CopyRunSummary, UiFactory.ButtonKind.Ghost));
        tools.AddChild(UiFactory.Button(Loc.T("清空战绩记录"), ClearRunJournalFromUi, UiFactory.ButtonKind.Ghost));
        column.AddChild(tools);

        _runSummaryStatus = UiFactory.StatusLine();
        column.AddChild(_runSummaryStatus);

        var card = HelpCard.Build(
            "play.runsummary",
            Loc.T("本轮成绩单"),
            Loc.T("一局结束（游戏显示结算画面）时自动出现。全部是这一局的事实：结果、层数、决策来源、请求与 Token。\n「复制成绩单」把同样的文字放进剪贴板，可以直接发给别人。\n「清空战绩记录」删除跨局保存的战绩文件；关掉「游玩体验」里的战绩开关后，不再写入，也不再提供给模型。"),
            column);
        card.Visible = false;
        _runSummaryCard = card;
        // The runtime's own change notification, not a polling clock: a finished run and the clear
        // action are the only things that can change this card, and both raise Changed. The exit
        // handler is bound to this exact control so a rebuild cannot unsubscribe its replacement.
        AgentRuntime.Instance.Changed += OnRunSummaryRuntimeChanged;
        card.TreeExited += () => OnRunSummaryCardExited(card);

        RefreshRunSummaryCard();
        return card;
    }

    /// <summary>
    /// Re-reads the scorecard and the recorded-run count. Both come from the runtime's in-memory
    /// state and its cached journal listing, so this is safe to call on every change notification.
    /// </summary>
    private void RefreshRunSummaryCard()
    {
        if (_runSummaryCard == null || !GodotObject.IsInstanceValid(_runSummaryCard))
        {
            return;
        }

        var summary = AgentRuntime.Instance.LastRunSummary;
        var recorded = AgentRuntime.Instance.RecentRunJournal(AgentRuntime.RunJournalPreviewLimit);
        if (summary == null && recorded.Count == 0)
        {
            _runSummaryCard.Visible = false;
            return;
        }

        _runSummaryCard.Visible = true;
        if (_runSummaryLines != null)
        {
            _runSummaryLines.Text = summary != null
                ? summary.ToCardText()
                : Loc.T("还没有本轮成绩单；已记录 {0} 局战绩。", recorded.Count);
        }

        if (summary?.Entry.RunId is not { } runId || string.Equals(runId, _revealedRunId, StringComparison.Ordinal))
        {
            return;
        }

        _revealedRunId = runId;
        RevealRunSummary();
    }

    /// <summary>
    /// Brings the report into view when the window is already open. The panel is deliberately not
    /// forced open: a player who hid it to watch the game does not want it to reappear over the
    /// game-over screen, and the edge tab already carries the way back.
    /// </summary>
    private void RevealRunSummary()
    {
        if (_panel?.Visible != true) return;
        if (!IsTabVisible(OverlayTabCatalog.Play))
        {
            ShowTab(OverlayTabCatalog.Play);
        }
    }

    /// <summary>
    /// The runtime's change notification, marshalled onto the game thread: it can be raised from a
    /// model callback or an HTTP request thread, and every control it repaints belongs to the game.
    /// The card reads cached runtime state only, so a frequent notification costs a field read.
    /// </summary>
    private void OnRunSummaryRuntimeChanged()
    {
        if (GameThread.IsCurrentThread)
        {
            RefreshRunSummaryCard();
            return;
        }

        _ = GameThread.InvokeAsync(RefreshRunSummaryCard);
    }

    /// <summary>
    /// Drops the subscription when the card that owned it leaves the tree. Guarded by identity: a
    /// late exit from a replaced card must not unsubscribe the card that replaced it.
    /// </summary>
    private void OnRunSummaryCardExited(Control card)
    {
        if (!ReferenceEquals(card, _runSummaryCard)) return;
        _runSummaryCard = null;
        AgentRuntime.Instance.Changed -= OnRunSummaryRuntimeChanged;
    }

    private void CopyRunSummary()
    {
        var text = AgentRuntime.Instance.LastRunSummary?.ToCopyText();
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowRunSummaryStatus(Loc.T("还没有本轮成绩单。"));
            return;
        }

        CopyText(text);
        ShowRunSummaryStatus(Loc.T("已复制成绩单。"));
    }

    private void ClearRunJournalFromUi()
    {
        AgentRuntime.Instance.TryClearRunJournal(out var message);
        ShowRunSummaryStatus(message);
    }

    private void ShowRunSummaryStatus(string message)
    {
        if (_runSummaryStatus != null && GodotObject.IsInstanceValid(_runSummaryStatus))
        {
            UiFactory.SetStatusLine(_runSummaryStatus, message);
        }

        RefreshRunSummaryCard();
    }
}
