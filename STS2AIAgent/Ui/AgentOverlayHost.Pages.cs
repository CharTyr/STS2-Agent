using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Ui;

/// <summary>
/// The overlay's page bodies, their live refresh, and the controls those two halves share.
/// </summary>
/// <remarks>
/// These members were moved here from <c>AgentOverlayHost.cs</c> when the header, the six pages and
/// the single 220-line refresh pass no longer fitted that file's budget. The split is the same move
/// the tabs made earlier: the file the ratchet watches keeps the host -- attach and teardown,
/// placement and drag, settings harvest and persistence -- while everything that builds or repaints
/// a control lives next to the pages it builds, so a seventh page or a second dashboard row lands
/// here instead of growing the host again.
///
/// The refresh side is deliberately here rather than with the host: <see cref="RefreshDynamic"/> and
/// the page builders write the same control fields, and a page whose repaint lives in another file is
/// how the two drift apart. The settings form is the one page that was split again -- see
/// <c>AgentOverlayHost.Settings.cs</c> -- because it is a third of this file by line count and
/// changes for its own reasons.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>The decision log's three live labels: usage, run spend, and the log itself.</summary>
    private RichTextLabel? _decisionLog;
    private Label? _decisionUsage;
    private Label? _decisionRunSpend;

    /// <summary>The teammate line and the tone it is currently drawn in. See <see cref="ApplyTone"/>.</summary>
    private Control? _teammateLivePill;
    private UiFactory.ButtonKind? _teammateLiveTone;

    /// <summary>The play page's solo/multiplayer sections and the switch that swaps between them.</summary>
    private SegmentedSwitch? _playModeSwitch;
    private Control? _soloSection;
    private Control? _coopSection;

    /// <summary>The per-mode dual-layer toggles and the solo Jev panel's live labels.</summary>
    private CheckBox? _dualLayerSoloCheck;
    private CheckBox? _nonCombatOnlyCheck;
    private CheckBox? _dualLayerCoopCheck;
    private Control? _jevPanel;
    private Label? _jevStatus;
    private Label? _jevLastChoice;
    private Label? _jevProbabilities;

    /// <summary>
    /// The play page: a solo/multiplayer mode switch on top, then the active mode's section.
    /// </summary>
    /// <remarks>
    /// The overlay is organised around how the player wants the AI to play, not around one tab per
    /// feature. Solo play (this window's LLM, or an external MCP client driving the same loop) and
    /// multiplayer (a companion instance playing alongside the human) are the two modes; only one is
    /// active at a time, so they share this page behind a switch rather than two tabs. The choice is
    /// persisted so the page reopens on the mode the player last used.
    /// </remarks>
    private Control BuildPlayPage()
    {
        var page = UiFactory.Column();

        var settings = AgentRuntime.Instance.Settings;
        var coop = settings.OverlayPlayMode == "coop";
        _playModeSwitch = UiFactory.SegmentedSwitch(
            new[] { Loc.T("单人"), Loc.T("多人") },
            coop ? 1 : 0,
            OnPlayModeSelected);
        page.AddChild(_playModeSwitch);
        _modeSwitchStatus = UiFactory.Wrapped("", UiFactory.FontCaption);
        page.AddChild(_modeSwitchStatus);

        _soloSection = BuildSoloSection();
        _coopSection = BuildCoopSection();
        _soloSection.Visible = !coop;
        _coopSection.Visible = coop;
        page.AddChild(_soloSection);
        page.AddChild(_coopSection);
        page.AddChild(BuildNonCombatOnlyControl());
        page.AddChild(BuildJevPanel());
        page.AddChild(BuildDecisionCard());

        return UiFactory.Scroll(page, 120);
    }

    /// <summary>Pause and confirm before persisting; the old view remains until confirmation.</summary>
    private void OnPlayModeSelected(int index) => _ = ChangePlayModeAsync(index);

    /// <summary>
    /// The solo mode section: play controls and metrics, then the conversation the decisions are
    /// read through, then the Jev panel and the decision log.
    /// </summary>
    private Control BuildSoloSection()
    {
        var section = UiFactory.Column();

        // The hero names the state, and colours it: green while the loop runs, muted while it waits.
        _playStatus = UiFactory.Wrapped(Loc.T("状态：-"), UiFactory.FontTitle, muted: false);
        _playSummary = UiFactory.Wrapped(Loc.T("等待开始。"), UiFactory.FontCaption);
        _playToggle = UiFactory.Button(Loc.T("开始自动游玩"), TogglePlay, UiFactory.ButtonKind.Primary);
        _stepButton = UiFactory.Button(Loc.T("单步"), () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        // A horizontal row cannot hold "暂停自动游玩" and "单步" inside a 440px panel: the primary
        // button's natural width plus padding pushes the second control past the card edge, which
        // is the clipped empty frame measured live on 2026-09-22. Stack them, and hide the step
        // button while autoplay owns the turn so the pause control gets the full card width.
        var playControls = UiFactory.Column();
        playControls.AddChild(_playToggle);
        playControls.AddChild(_stepButton);
        section.AddChild(UiFactory.Card(
            Loc.T("当前回合"),
            _playStatus,
            _playSummary,
            playControls));

        _playScreen = UiFactory.Label("-", UiFactory.FontHeading);
        _playAction = UiFactory.Label("-", UiFactory.FontHeading);
        _playUsage = UiFactory.Label("-", UiFactory.FontHeading);
        // The three readings have a 96px floor each, but their live values (especially the Token
        // sentence) grow well past that. Keep the short screen/action readings together and give
        // the usage sentence its own full-width row rather than clipping the right-hand tile.
        section.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("屏幕"), _playScreen),
            UiFactory.MetricTile(Loc.T("最近动作"), _playAction)));
        section.AddChild(UiFactory.MetricTile(Loc.T("Token"), _playUsage));

        // The dual-layer toggle for solo play. When it is on, Jev picks each action and the LLM only
        // plans strategy; the panel below the conversation shows what Jev chose.
        _dualLayerSoloCheck = UiFactory.Check(
            Loc.T("双层决策模式（Jev 执行 + LLM 规划）"),
            AgentRuntime.Instance.Settings.DualLayerSoloEnabled);
        _dualLayerSoloCheck.Toggled += on =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.DualLayerSoloEnabled = on;
            AgentRuntime.Instance.SaveSettings(next);
            RefreshDynamic();
        };
        section.AddChild(_dualLayerSoloCheck);

        section.AddChild(BuildChatCard());
        section.AddChild(UiFactory.Wrapped(Loc.T("自动游玩走 compact 状态和工具；进行中发消息只影响后续决策。空闲聊天要代打一手须明确说「帮我打」。")));
        return section;
    }

    private Control BuildNonCombatOnlyControl()
    {
        _nonCombatOnlyCheck = UiFactory.Check(
            Loc.T("仅在战斗外自动游玩（战斗中只读）"),
            AgentRuntime.Instance.Settings.NonCombatOnlyEnabled);
        _nonCombatOnlyCheck.Toggled += on =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.NonCombatOnlyEnabled = on;
            AgentRuntime.Instance.SaveSettings(next);
            RefreshDynamic();
        };
        return UiFactory.Card(
            Loc.T("战斗控制"),
            _nonCombatOnlyCheck,
            UiFactory.Wrapped(
                Loc.T("开启后，Agent 在战斗期间不请求模型、不执行动作；战斗结束后自动恢复。适合手动战斗或交给外部战斗控制器。"),
                UiFactory.FontCaption));
    }

    /// <summary>
    /// The Jev panel: shown under the conversation while solo dual-layer mode is on. It reads the
    /// execution layer's last reading from the runtime -- the choice, its confidence and the option
    /// scores the decider reported -- and stays blank until a dual-layer turn actually happens.
    /// </summary>
    private Control BuildJevPanel()
    {
        var column = UiFactory.Column();
        _jevStatus = UiFactory.Wrapped(Loc.T("Jev 未配置。到设置页填写 API Key 后即可用双层决策。"), UiFactory.FontCaption);
        _jevLastChoice = UiFactory.Wrapped("-", UiFactory.FontBody, muted: false);
        _jevProbabilities = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _jevDanger = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _jevLatency = UiFactory.Wrapped("-", UiFactory.FontCaption);
        column.AddChild(_jevStatus);
        column.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("Jev 最近选择"), _jevLastChoice),
            UiFactory.MetricTile(Loc.T("概率分布"), _jevProbabilities)));
        column.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("危险度"), _jevDanger),
            UiFactory.MetricTile(Loc.T("耗时"), _jevLatency)));
        // The dual-layer split for this session: how often Jev committed vs how often the LLM had
        // to finish a turn Jev could not. "-" until the first dual-layer turn.
        _jevFallbackRate = UiFactory.Wrapped("-", UiFactory.FontCaption);
        column.AddChild(_jevFallbackRate);
        _jevPanel = UiFactory.Card(Loc.T("Jev 执行层"), column);
        return _jevPanel;
    }

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
        column.AddChild(UiFactory.MetricTile(Loc.T("本次会话"), _decisionUsage));
        column.AddChild(UiFactory.MetricTile(Loc.T("本局"), _decisionRunSpend));
        column.AddChild(UiFactory.Wrapped(Loc.T("最新在前：动作、理由、来源，以及该步消耗的 Token。")));
        _decisionLog = UiFactory.Rich();
        _decisionLog.FitContent = false;
        // Newest first, so follow-to-bottom would scroll away from the line that just arrived.
        _decisionLog.ScrollFollowing = false;
        _decisionLog.CustomMinimumSize = new Vector2(0, 140);
        _decisionLog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(_decisionLog);
        return UiFactory.Card(Loc.T("决策记录"), column);
    }

    /// <summary>
    /// The teammate's live line. Null means "not asked yet" rather than "no teammate", so the label
    /// distinguishes the two instead of claiming the teammate is gone while the first poll is in
    /// flight.
    /// </summary>
    private static string TeammateLiveText()
    {
        var status = AgentRuntime.Instance.TeammateLiveStatus;
        if (status != null)
        {
            return Loc.T("队友实况：{0}", status);
        }

        return LocalDualInstanceLauncher.Connection == null
            ? Loc.T("队友实况：组队后显示。")
            : Loc.T("队友实况：读取中…");
    }

    /// <summary>
    /// The co-op page: the actions first, then what the teammate is doing, then the chat.
    /// </summary>
    /// <remarks>
    /// The order used to be status, invite, control, chat -- which put the page's whole reason for
    /// existing ("invite the teammate", "pause the teammate") below a card of text, and mid-fight the
    /// pause button was a scroll away. Actions now come first because they are what the player came
    /// here to press; the status card answers the question they ask next; the chat, which is the only
    /// part that has to be scrolled to, is last.
    ///
    /// This is the longest page in the panel, so the chat's box is sized from the panel's own height
    /// rather than pinned at 130 pixels: the point of the reorder is lost if a fixed chat box pushes
    /// the actions off the bottom anyway.
    /// </remarks>
    /// <summary>
    /// The multiplayer mode section: the actions first, then what the teammate is doing, then the
    /// chat. Moved into the play page from its own tab; the play page's scroll carries it now.
    /// </summary>
    /// <remarks>
    /// The order used to be status, invite, control, chat -- which put the section's whole reason for
    /// existing ("invite the teammate", "pause the teammate") below a card of text, and mid-fight the
    /// pause button was a scroll away. Actions now come first because they are what the player came
    /// here to press; the status card answers the question they ask next; the chat, which is the only
    /// part that has to be scrolled to, is last.
    /// </remarks>
    private Control BuildCoopSection()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Wrapped(Loc.T("可以一起玩：你打自己的角色，AI 打另一个角色，同一座塔往上爬。大厅仍是 4 人位，还可以再邀 2 名在线玩家。")));

        _sessionHeadline = UiFactory.Label(Loc.T("状态：-"), UiFactory.FontTitle);
        // Wrapped, not Label. Every one of these is short when the control is built and a full sentence
        // once the runtime fills it in -- `_sessionNext` is "-" at build time and measured 577 pixels
        // wide in the live pass on 2026-09-21, which pushed this page's column to 601 against a
        // 440-pixel panel and clipped the cards.
        _sessionDetail = UiFactory.Wrapped("-");
        _sessionNext = UiFactory.Wrapped(Loc.T("下一步：-"));
        _sessionConfigNotice = UiFactory.Wrapped("");
        _resetStatsButton = UiFactory.Button(Loc.T(SessionBudgetLimits.ResetStatsActionLabel), ResetSessionStatsFromUi, UiFactory.ButtonKind.Ghost);
        // The switch names the new behaviour: ticked = the companion no longer takes the preselected
        // character; the AI driving it (or a human at that window) picks instead. It sits right above
        // the invite button so the choice is visible before the second window exists, and is saved on
        // toggle because the companion reads settings at launch and this tab has no separate Save.
        _companionChoiceToggle = UiFactory.Check(Loc.T("禁用自动选角"), !AgentRuntime.Instance.Settings.CompanionAutoSelectCharacter);
        _companionChoiceToggle.Toggled += on =>
        {
            var settings = CloneSettings(AgentRuntime.Instance.Settings);
            settings.CompanionAutoSelectCharacter = !on;
            AgentRuntime.Instance.SaveSettings(settings);
            RefreshDynamic();
        };
        // Wrapped, not Label: this is a full sentence, and an unwrapped label reports its whole line as
        // its minimum width -- measured live on 2026-09-21 at 659 pixels, which pushed the panel's
        // column to 691 and clipped every card against a 440-pixel panel. Every dynamic label that can
        // hold a sentence goes through Wrapped for the same reason.
        _dualHint = UiFactory.Wrapped(DualHintText(), UiFactory.FontCaption);
        // Wrapped for the same reason: launch results ("AI 队友窗口已经在运行…", Steam-blocked errors
        // with an exception message) are full sentences wider than the panel.
        _dualStatus = UiFactory.Wrapped(Loc.T("队友尚未加入。"), UiFactory.FontBody, muted: true);
        // The companion's own live state, not the host's view of the shared run: the host already
        // knows the process is alive, and what a co-op player needs mid-fight is the other
        // character's health and whether it can act. Drawn as a tone chip rather than a sentence, so
        // "the teammate is down" registers before the numbers do; RefreshDynamic recolours it.
        _teammateLive = UiFactory.Label(TeammateLiveText(), UiFactory.FontCaption);
        _teammateLivePill = UiFactory.PillBadge(string.Empty, UiFactory.ButtonKind.Ghost);
        ((PanelContainer)_teammateLivePill).AddChild(_teammateLive);

        // Actions first. Invite is the tab's main action -- the one a player comes to this page to
        // press -- and the control row that used to be a card of its own now sits under it, because
        // "pause the teammate" is the same job as "start the teammate" and was a scroll away from it.
        _dualLaunchButton = UiFactory.Button(Loc.T("邀请 AI 队友"), () => _ = LaunchDualAsync(), UiFactory.ButtonKind.Primary);
        _dualContinueButton = UiFactory.Button(Loc.T("继续上次联机对局"), () => _ = ContinueDualAsync());
        _teamPause = UiFactory.Button(Loc.T("暂停队友"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(false, CancellationToken.None));
        _teamResume = UiFactory.Button(Loc.T("继续游玩"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(true, CancellationToken.None));
        _teamControlStatus = UiFactory.Wrapped(AgentRuntime.Instance.TeamControlStatus, UiFactory.FontCaption, muted: true);
        page.AddChild(UiFactory.Card(
            Loc.T("邀请与控制"),
            _companionChoiceToggle,
            _dualHint,
            UiFactory.Row(_dualLaunchButton, UiFactory.Button(Loc.T("导出诊断"), CopyDiagnostics, UiFactory.ButtonKind.Ghost)),
            // In-game entry for continue_ai_teammate: the game's own Load button opens a saved co-op
            // run over Steam networking and rejects the local-connection NetIds, so the way back into
            // a saved run has to live here, one row under the invite.
            UiFactory.Row(_dualContinueButton, _resetStatsButton),
            UiFactory.Divider(),
            UiFactory.Row(_teamPause, _teamResume),
            _teamControlStatus));

        page.AddChild(UiFactory.Card(
            Loc.T("组队状态"),
            _sessionHeadline,
            _sessionDetail,
            _sessionNext,
            _sessionConfigNotice,
            _teammateLivePill,
            _dualStatus));

        _teamChat = UiFactory.Rich();
        _teamChat.FitContent = false;
        _teamChat.CustomMinimumSize = new Vector2(0, 120);
        _teamInput = UiFactory.Multiline("", 56);
        _teamInput.PlaceholderText = Loc.T("一起集火哪个敌人？这条路线你怎么看？");
        // A text editor reports its placeholder as its minimum width, and the placeholder here is a full
        // question -- measured live on 2026-09-21 as the 525-pixel panel container that was still
        // pushing the co-op page past its 440-pixel panel after the labels were fixed. Set after the
        // placeholder, because the editor recomputes its minimum when the text it holds changes.
        _teamInput.CustomMinimumSize = new Vector2(UiFactory.MinReflowWidth, 56);
        _teamSend = UiFactory.Button(Loc.T("和队友说"), () => _ = SendTeamMessageAsync(), UiFactory.ButtonKind.Primary);
        _teamStatus = UiFactory.Wrapped(AgentRuntime.Instance.TeamStatus, UiFactory.FontCaption);
        page.AddChild(UiFactory.Card(
            Loc.T("队伍交流"),
            _teamChat,
            _teamInput,
            UiFactory.TightRow(_teamSend),
            _teamStatus,
            UiFactory.Wrapped(Loc.T("聊天不会替你出牌，也不会恢复已暂停的队友。建议会供队友下一次决策参考。")),
            UiFactory.Wrapped(Loc.T("如果队友窗口未能连接，请检查游戏日志和 Steam 双开限制。"))));

        // The dual-layer toggle for the multiplayer teammate. The companion instance runs its own Jev
        // loop (it has no overlay), so this switch is the host-side control for it.
        _dualLayerCoopCheck = UiFactory.Check(
            Loc.T("双层决策模式（Jev 执行 + LLM 规划）"),
            AgentRuntime.Instance.Settings.DualLayerCoopEnabled);
        _dualLayerCoopCheck.Toggled += on => _ = SetCoopDualLayerAsync(on);
        page.AddChild(_dualLayerCoopCheck);
        _companionSettingsStatus = UiFactory.Wrapped("", UiFactory.FontCaption);
        page.AddChild(_companionSettingsStatus);
        // The play page's own scroll carries this section now; the section returns its plain column.
        return page;
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

        if (_decisionLog == null)
        {
            return;
        }

        _decisionLog.Clear();
        var entries = AgentRuntime.Instance.RecentDecisions(DecisionLogView.RecentLimit);
        if (entries.Count == 0)
        {
            _decisionLog.AppendText(Muted(Loc.T("还没有决策记录。自动游玩或外部客户端执行动作后会出现在这里。")) + "\n");
            return;
        }

        // Newest first, and rendered directly rather than through the pre-joined lines: the parts of
        // a line that carry meaning get their own colour, the action in the accent because it is what
        // happened. Everything is escaped, so a model that writes BBCode in its reason cannot restyle
        // the page it is quoted on. The blank line between entries is what stops fifty of them
        // reading as one paragraph.
        for (var offset = 0; offset < entries.Count; offset++)
        {
            var entry = entries[entries.Count - 1 - offset];
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(entry.reason))
            {
                parts.Add(Loc.T("理由：{0}", Escape(entry.reason)));
            }

            parts.Add(Loc.T("来源：{0}", Escape(entry.source)));
            parts.Add(entry.total_tokens is { } tokens
                ? Loc.T("本次 Token：{0}", tokens.ToString("N0"))
                : Loc.T("本次 Token：未知"));
            // The action stays on its own line. A reason such as "Play Dismantle (14 dmg with STR 3)
            // on the 1st Inklet" plus source and token count is wider than the panel even after the
            // rich label wraps, and one joined line is what the 2026-09-22 screenshot clipped.
            _decisionLog.AppendText(Accent($"{entry.id}. {entry.action}") + "\n" + Muted(string.Join("\n", parts)) + "\n\n");
        }
    }

    private async Task SendChatAsync()
    {
        var text = _chatInput?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_chatInput != null)
        {
            _chatInput.Text = string.Empty;
        }

        PersistChatFlags();
        var attachState = _attachState?.ButtonPressed ?? true;
        var attachShot = _attachShot?.ButtonPressed ?? false;
        await AgentRuntime.Instance.SendChatAsync(text, attachState, attachShot, CancellationToken.None);
    }

    private async Task TestConnectionAsync()
    {
        SaveSettingsFromUi();
        // The footer button tests the main model with the full probe (connectivity + tool calling);
        // the per-card buttons cover every other model individually.
        var mainId = AgentRuntime.Instance.Settings.ConversationModelId;
        if (string.IsNullOrWhiteSpace(mainId))
        {
            SetSaveStatus(Loc.T("请先在「模型绑定」选择主模型。"));
            return;
        }

        SetSaveStatus(Loc.T("正在测试模型…"));
        var result = await AgentRuntime.Instance.TestModelAsync(mainId, CancellationToken.None);
        SetSaveStatus(result);
        // Keep the role-level records fresh too: the first-run guidance and the co-op gate still
        // read them, and a verified main model is what they are asking about.
        await AgentRuntime.Instance.TestConnectionAsync(CancellationToken.None);
        _settingsDirty = false;
        RebuildSettingsForm();
        ShowTab("settings");
    }

    private void PersistChatFlags()
    {
        AgentRuntime.Instance.PersistChatAttachFlags(
            _attachState?.ButtonPressed ?? true,
            _attachShot?.ButtonPressed ?? false);
    }

    private void TogglePlay()
    {
        if (AgentRuntime.Instance.PlayRunning)
        {
            AgentRuntime.Instance.StopAutoPlay();
        }
        else
        {
            AgentRuntime.Instance.StartAutoPlay();
        }

        RefreshDynamic();
    }

    /// <summary>
    /// Sends the teammate message box's contents, then repaints. The send is handed to the runtime
    /// and awaited to completion here because the button's own state (pending, then the reply) is
    /// what tells the player whether it went; the trailing repaint runs on the game thread.
    /// </summary>
    private async Task SendTeamMessageAsync()
    {
        var text = _teamInput?.Text.Trim() ?? "";
        if (text.Length == 0 || AgentRuntime.Instance.TeamMessagePending) return;
        if (_teamInput != null) _teamInput.Text = "";
        await AgentRuntime.Instance.SendTeamMessageAsync(text, CancellationToken.None);
        await GameThread.InvokeAsync(RefreshDynamic);
    }

    /// <summary>
    /// Repaints every live control on every page.
    /// </summary>
    /// <remarks>
    /// One pass rather than a per-page callback, because the values it writes arrive from paths with
    /// no event to subscribe to -- an action submitted over the HTTP API updates the decision log
    /// without raising <c>Changed</c>. The guard at the top is what keeps it honest: this runs on the
    /// game thread, and a page whose control is gone returns early instead of throwing into the tick.
    ///
    /// The conversation is drawn first and the whole pass is guarded. This used to be one straight
    /// line of blocks, so the first one that threw -- <c>BuildStatePayload</c> against a screen that
    /// had just gone away was enough -- left every block after it, the conversation among them, frozen
    /// at the last snapshot. The caller swallows the exception, so the visible symptom was a chat that
    /// simply stopped updating for the rest of the run.
    /// </remarks>
    private void RefreshDynamic()
    {
        try
        {
            // First, so no later block can keep it from being reached.
            RefreshChatLog();

            if (_apiLabel != null)
            {
                _apiLabel.Text = Loc.T("{0}  ·  {1}  ·  热键 {2}", HttpServer.Instance.Prefix, InstanceRole.Current, AgentRuntime.Instance.Settings.Hotkey);
            }

            var playing = AgentRuntime.Instance.PlayRunning;

            if (_headerStatus != null)
            {
                _headerStatus.Text = playing ? Loc.T("● 自动游玩中") : Loc.T("○ 待机");
                _headerStatus.AddThemeColorOverride(
                    "font_color",
                    playing ? UiFactory.ToGodot(UiFactory.Palette.Positive) : UiFactory.ToGodot(UiFactory.Palette.Muted));
            }

            if (_edgeTab != null)
            {
                _edgeTab.Text = playing ? "AI ▶" : "AI";
            }

            if (_playStatus != null)
            {
                // The elapsed-bearing variant: a turn that stalls now shows a growing counter
                // beside the stage it is stuck in, instead of a frozen line.
                _playStatus.Text = AgentRuntime.Instance.StatusWithElapsed;
                _playStatus.AddThemeColorOverride(
                    "font_color",
                    playing ? UiFactory.ToGodot(UiFactory.Palette.Positive) : UiFactory.ToGodot(UiFactory.Palette.Text));
            }

            if (_playSummary != null)
            {
                _playSummary.Text = playing ? Loc.T("正在自动决策并执行动作。") : Loc.T("等待开始。");
            }

            if (_playScreen != null)
            {
                try
                {
                    _playScreen.Text = Loc.T("屏幕：{0}", GameStateService.CurrentScreenName());
                }
                catch
                {
                    _playScreen.Text = Loc.T("屏幕：-");
                }
            }

            if (_playAction != null)
            {
                _playAction.Text = Trim(AgentRuntime.Instance.LastAction, 24);
            }

            if (_playUsage != null)
            {
                // Whole sentence: the tile has its own full-width row and wraps (see BuildSoloSection).
                _playUsage.Text = PlayerFacingSession.FormatUsage(
                    AgentRuntime.Instance.SessionUsageKnown,
                    AgentRuntime.Instance.SessionUsage,
                    AgentRuntime.Instance.SessionRequests);
            }

            var facing = AgentRuntime.Instance.PlayerFacing();
            if (_sessionHeadline != null)
            {
                _sessionHeadline.Text = facing.Headline;
            }

            if (_sessionDetail != null)
            {
                _sessionDetail.Text = facing.Detail;
            }

            if (_sessionNext != null)
            {
                _sessionNext.Text = Loc.T("下一步：{0}", facing.NextAction);
            }

            RefreshDecisionPage(facing);

            var canResetStats = SessionBudgetLimits.CanResetSessionStats(
                AgentRuntime.Instance.PlayRunning,
                AgentRuntime.Instance.PlayPhase);
            if (_resetStatsButton != null)
            {
                _resetStatsButton.Disabled = !canResetStats;
            }

            if (_settingsResetStatsButton != null)
            {
                _settingsResetStatsButton.Disabled = !canResetStats;
            }

            if (_sessionConfigNotice != null)
            {
                _sessionConfigNotice.Text = FormatSettingsNotice();
                _sessionConfigNotice.Visible = AgentRuntime.Instance.SettingsNotice.HasMessage;
            }

            if (_settingsLoadNotice != null)
            {
                _settingsLoadNotice.Text = FormatSettingsNotice();
            }

            if (_budgetHint != null && !string.IsNullOrWhiteSpace(_budgetInputError))
            {
                _budgetHint.Text = _budgetInputError;
            }

            if (_playTest != null)
            {
                var firstRun = FirstRunSetup.Evaluate(AgentRuntime.Instance.Settings);
                if (_conversationTest != null) _conversationTest.Text = ModelRoleProbe.FormatLine(firstRun.Conversation);
                _playTest.Text = ModelRoleProbe.FormatLine(firstRun.Play);
                if (_visionTest != null) _visionTest.Text = ModelRoleProbe.FormatLine(firstRun.Vision);
            }

            if (_playToggle != null)
            {
                _playToggle.Disabled = AgentRuntime.Instance.DualLaunching;
                _playToggle.Text = playing ? Loc.T("暂停自动游玩") : Loc.T("开始自动游玩");
            }

            if (_stepButton != null)
            {
                _stepButton.Disabled = playing;
                _stepButton.Visible = !playing;
            }

            if (_sendButton != null)
            {
                _sendButton.Disabled = false;
            }

            if (_dualStatus != null)
            {
                _dualStatus.Text = AgentRuntime.Instance.DualStatus;
            }

            if (_teammateLive != null)
            {
                _teammateLive.Text = TeammateLiveText();
            }

            ApplyTone(_teammateLivePill, ref _teammateLiveTone, TeammateLiveTone());

            if (_dualLaunchButton != null)
            {
                // Only the button that started the launch reads as busy; the other one just greys out.
                _dualLaunchButton.Text = AgentRuntime.Instance.DualLaunching && !_continueLaunching ? Loc.T("正在邀请队友…") : Loc.T("邀请 AI 队友");
            }

            RefreshContinueAvailability();

            if (_dualContinueButton != null)
            {
                _dualContinueButton.Text = AgentRuntime.Instance.DualLaunching && _continueLaunching ? Loc.T("正在读档接回队友…") : Loc.T("继续上次联机对局");
            }

            if (_companionChoiceToggle != null)
            {
                _companionChoiceToggle.Disabled = InstanceRole.IsCompanion || AgentRuntime.Instance.DualLaunching;
                _companionChoiceToggle.SetPressedNoSignal(!AgentRuntime.Instance.Settings.CompanionAutoSelectCharacter);
            }

            if (_dualHint != null) _dualHint.Text = DualHintText();

            if (_teamSend != null)
            {
                _teamSend.Disabled = AgentRuntime.Instance.TeamMessagePending || AgentRuntime.Instance.DualLaunching || InstanceRole.IsCompanion;
                _teamSend.Text = AgentRuntime.Instance.TeamMessagePending ? Loc.T("等待队友回复…") : Loc.T("和队友说");
            }
            if (_teamStatus != null) _teamStatus.Text = AgentRuntime.Instance.TeamStatus;
            if (_teamControlStatus != null) _teamControlStatus.Text = AgentRuntime.Instance.TeamControlStatus;
            var controlDisabled = InstanceRole.IsCompanion || AgentRuntime.Instance.DualLaunching || AgentRuntime.Instance.TeamControlPending;
            if (_teamPause != null) _teamPause.Disabled = controlDisabled;
            if (_teamResume != null) _teamResume.Disabled = controlDisabled;
            if (_teamChat != null)
            {
                _teamChat.Clear();
                foreach (var turn in AgentRuntime.Instance.TeamHistory)
                {
                    _teamChat.AppendText(FormatTurn(turn.Role == "user" ? Loc.T("你") : Loc.T("AI 队友"), turn.Text));
                }
            }

            if (_mcpStatus != null)
            {
                _mcpStatus.Text = AgentRuntime.Instance.McpStatus;
            }

            if (_mcpToggle != null)
            {
                _mcpToggle.SetPressedNoSignal(AgentRuntime.Instance.McpRunning);
            }

            if (_mcpInfoBox != null)
            {
                _mcpInfoBox.Visible = AgentRuntime.Instance.McpRunning;
            }

            if (_mcpUrlLabel != null)
            {
                var url = AgentRuntime.Instance.McpUrl;
                _mcpUrlLabel.Text = string.IsNullOrWhiteSpace(url) ? "" : Loc.T("地址：{0}", url);
            }

            if (_mcpConfigEdit != null)
            {
                _mcpConfigEdit.Text = AgentRuntime.Instance.McpClientConfig;
            }

            // The mode switch and the dual-layer toggles mirror settings that can change off this page
            // (a settings save, or the mode switch itself), so the refresh pass re-syncs them rather
            // than trusting the last click to be the only writer.
            var mode = AgentRuntime.Instance.Settings;
            var coopMode = mode.OverlayPlayMode == "coop";
            if (_dualLayerSoloCheck != null) _dualLayerSoloCheck.SetPressedNoSignal(mode.DualLayerSoloEnabled);
            if (_nonCombatOnlyCheck != null) _nonCombatOnlyCheck.SetPressedNoSignal(mode.NonCombatOnlyEnabled);
            if (_dualLayerCoopCheck != null) _dualLayerCoopCheck.SetPressedNoSignal(mode.DualLayerCoopEnabled);
            RefreshModeControls(coopMode, coopMode ? mode.DualLayerCoopEnabled : mode.DualLayerSoloEnabled);
            RefreshJevReading(coopMode, mode);
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} refresh pass failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One chat turn as the log draws it: a coloured speaker, then the body with any BBCode in it
    /// neutralised. A turn that arrives containing <c>[b]</c> must not be able to restyle the panel
    /// it is drawn in.
    /// </summary>
    private static string FormatTurn(string speaker, string text)
    {
        return Accent(speaker) + "\n" + Escape(text) + "\n\n";
    }

    /// <summary>
    /// Which tone the teammate chip carries: down reads as a problem, a live reading reads as fine,
    /// and "not asked yet" stays neutral so the chip never claims the teammate is gone while the
    /// first poll is still in flight.
    /// </summary>
    /// <remarks>
    /// Matches on the same localized text the chip displays rather than re-reading the companion's
    /// payload: the runtime keeps that summary as a formatted string, and re-parsing it here would be
    /// a second source of truth for a fact this file only needs a colour for. A wording change
    /// degrades to the neutral tone, which is the safe direction.
    /// </remarks>
    private static UiFactory.ButtonKind TeammateLiveTone()
    {
        var status = AgentRuntime.Instance.TeammateLiveStatus;
        if (string.IsNullOrWhiteSpace(status))
        {
            return UiFactory.ButtonKind.Ghost;
        }

        if (status.Contains(Loc.T("（已倒下）"), StringComparison.Ordinal))
        {
            return UiFactory.ButtonKind.Danger;
        }

        return UiFactory.ButtonKind.Primary;
    }

    /// <summary>
    /// Repaints a pill's stylebox and its label's colour to <paramref name="tone"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="applied"/> is the tone currently drawn. This runs on the panel tick, and a
    /// Godot <c>StyleBox</c> is a new object every time one is built; without the guard the refresh
    /// pass would allocate two styleboxes and a stylebox override on every tick for a chip whose
    /// colour almost never changes.
    /// </remarks>
    private static void ApplyTone(Control? pill, ref UiFactory.ButtonKind? applied, UiFactory.ButtonKind tone)
    {
        if (pill is not PanelContainer container || applied == tone)
        {
            return;
        }

        applied = tone;
        var (background, foreground) = UiFactory.ToneStyle(tone);
        container.AddThemeStyleboxOverride("panel", UiFactory.ChipStyle(background, foreground.Darkened(0.40f)));
        foreach (var child in container.GetChildren())
        {
            if (child is Label label)
            {
                label.AddThemeColorOverride("font_color", foreground);
            }
        }
    }

    /// <summary>BBCode in the accent colour, for the parts of a line that carry the meaning.</summary>
    private static string Accent(string text)
    {
        return "[color=#" + UiFactory.Accent.ToHtml(false) + "][b]" + Escape(text) + "[/b][/color]";
    }

    /// <summary>BBCode in the muted colour, for hints and empty states.</summary>
    private static string Muted(string text)
    {
        return "[color=#" + UiFactory.Muted.ToHtml(false) + "]" + Escape(text) + "[/color]";
    }

    private static string SelectedText(OptionButton? combo)
    {
        if (combo == null || combo.Selected < 0)
        {
            return string.Empty;
        }

        return combo.GetItemText(combo.Selected);
    }

    private static string SelectedMetadata(OptionButton? combo)
    {
        if (combo == null || combo.Selected < 0)
        {
            return string.Empty;
        }

        return combo.GetItemMetadata(combo.Selected).AsString();
    }

    private static void SelectByText(OptionButton combo, string? value)
    {
        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (string.Equals(combo.GetItemText(i), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.Selected = i;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            // Preserve an unknown value visibly until the user chooses a supported option.
            // Falling back to item 0 would silently rewrite the model configuration on save.
            combo.AddItem(value);
            combo.Selected = combo.ItemCount - 1;
            return;
        }

        combo.Selected = combo.ItemCount > 0 ? 0 : -1;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Trim(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        text = text.Replace("\n", " ");
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static string Escape(string text)
    {
        return text.Replace("[", "［").Replace("]", "］");
    }

    private sealed record EndpointEditors(string Id, LineEdit Name, LineEdit Url, LineEdit Key, CheckBox Enabled);

    private sealed record ModelEditors(
        string Id,
        LineEdit Display,
        LineEdit ModelName,
        OptionButton Endpoint,
        CheckBox Vision,
        OptionButton ThinkingMode,
        OptionButton ThinkingIntensity,
        LineEdit ContextWindow);
}
