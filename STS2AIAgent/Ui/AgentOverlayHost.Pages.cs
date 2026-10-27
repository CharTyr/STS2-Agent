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
    private OptionButton? _companionMode;
    /// <summary>Pause and resume, shown only while a teammate exists.</summary>
    private Control? _teamControlRow;
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

    /// <summary>The teammate line and the tone it is currently drawn in. See <see cref="ApplyTone"/>.</summary>
    private Control? _teammateLivePill;

    /// <summary>The team chat's redraw-only-on-change wrapper. See <see cref="RichLog"/>.</summary>
    private RichLog? _teamChatView;
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
    /// The play page's setup line and whether the main model is ready. Kept by the refresh pass, so
    /// the start buttons and the line agree with the gate <c>StartAutoPlay</c> applies.
    /// </summary>
    private Control? _readinessBanner;
    private Label? _readinessText;
    private bool _playReady;

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
        page.AddChild(BuildReadinessBanner());

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

    /// <summary>
    /// The line that says what is missing before the AI can play, with the button that goes and
    /// fixes it. Hidden once the main model passes its test.
    /// </summary>
    /// <remarks>
    /// Without it, a player who opened the play page first pressed Start and got one sentence in the
    /// status line pointing at another tab. The details and the error stay on the settings page; this
    /// line only has to say that something is missing and where.
    /// </remarks>
    private Control BuildReadinessBanner()
    {
        _readinessText = UiFactory.Wrapped("", UiFactory.FontBody, muted: false);
        var row = UiFactory.Row(_readinessText);
        var go = UiFactory.Button(Loc.T("去设置"), () => ShowTab(OverlayTabCatalog.Settings), UiFactory.ButtonKind.Primary);
        go.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        row.AddChild(go);
        _readinessBanner = UiFactory.Card(null, row);
        _readinessBanner.Visible = false;
        return _readinessBanner;
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
        // Two caption lines instead of three metric tiles: what the AI sees and did last, and what the
        // session has spent. The tiles took a quarter of the page for three short readings, and the
        // spend was drawn a second time in the decision card below.
        _playSummary = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _playUsage = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _playToggle = UiFactory.Button(Loc.T("开始自动游玩"), TogglePlay, UiFactory.ButtonKind.Primary);
        _stepButton = UiFactory.Button(Loc.T("单步"), () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        // A horizontal row cannot hold "暂停" and "单步" inside a 440px panel: the primary
        // button's natural width plus padding pushes the second control past the card edge, which
        // is the clipped empty frame measured live on 2026-09-22. Stack them, and hide the step
        // button while autoplay owns the turn so the pause control gets the full card width.
        var playControls = UiFactory.Column();
        playControls.AddChild(_playToggle);
        playControls.AddChild(_stepButton);
        section.AddChild(HelpCard.Build(
            "play.auto",
            Loc.T("自动游玩"),
            Loc.T("「开始自动游玩」让 AI 持续决策并操作，「单步」只走一步。\n用的是与 MCP 相同的精简状态和工具，不需要视觉。"),
            _playStatus,
            _playSummary,
            _playUsage,
            playControls));

        // The dual-layer toggle for solo play. When it is on, Jev picks each action and the LLM only
        // plans strategy; the panel below the conversation shows what Jev chose.
        _dualLayerSoloCheck = UiFactory.Check(
            Loc.T("双层决策（Jev）"),
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
        return section;
    }

    private Control BuildNonCombatOnlyControl()
    {
        _nonCombatOnlyCheck = UiFactory.Check(
            Loc.T("战斗中不自动操作"),
            AgentRuntime.Instance.Settings.NonCombatOnlyEnabled);
        _nonCombatOnlyCheck.Toggled += on =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.NonCombatOnlyEnabled = on;
            AgentRuntime.Instance.SaveSettings(next);
            _ = SaveSettingsAndSyncJevAsync();
            RefreshDynamic();
        };
        return HelpCard.Build(
            "play.combat",
            Loc.T("战斗"),
            Loc.T("开启后，战斗中 AI 不请求模型也不操作，战斗结束后自动恢复。\n适合手动打战斗，或交给外部战斗控制器。"),
            _nonCombatOnlyCheck);
    }

    /// <summary>
    /// The Jev panel: shown under the conversation while solo dual-layer mode is on. It reads the
    /// execution layer's last reading from the runtime -- the choice, its confidence and the option
    /// scores the decider reported -- and stays blank until a dual-layer turn actually happens.
    /// </summary>
    private Control BuildJevPanel()
    {
        var column = UiFactory.Column();
        _jevStatus = UiFactory.Wrapped(Loc.T("未配置 Jev，请先在「设置」填写。"), UiFactory.FontCaption);
        _jevLastChoice = UiFactory.Wrapped("-", UiFactory.FontBody, muted: false);
        _jevProbabilities = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _jevDanger = UiFactory.Wrapped("-", UiFactory.FontCaption);
        _jevLatency = UiFactory.Wrapped("-", UiFactory.FontCaption);
        column.AddChild(_jevStatus);
        column.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("最近选择"), _jevLastChoice),
            UiFactory.MetricTile(Loc.T("概率"), _jevProbabilities)));
        column.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("危险度"), _jevDanger),
            UiFactory.MetricTile(Loc.T("耗时"), _jevLatency)));
        // The dual-layer split for this session: how often Jev committed vs how often the LLM had
        // to finish a turn Jev could not. "-" until the first dual-layer turn.
        _jevFallbackRate = UiFactory.Wrapped("-", UiFactory.FontCaption);
        column.AddChild(_jevFallbackRate);
        _jevPanel = HelpCard.Build(
            "play.jev",
            Loc.T("Jev"),
            Loc.T("开启「双层决策」后，由 Jev 逐步选择动作，LLM 只负责战略规划。\nJev 的地址和 Key 在「设置」里填写；置信度低于阈值时由 LLM 接手。"),
            column);
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
        return HelpCard.Build(
            "play.decisions",
            Loc.T("决策记录"),
            Loc.T("最新的在最前。每条包含动作、理由、来源和该步消耗的 Token。"),
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
    /// The teammate's live line. Null means "not asked yet" rather than "no teammate", so the label
    /// distinguishes the two instead of claiming the teammate is gone while the first poll is in
    /// flight.
    /// </summary>
    private static string TeammateLiveText()
    {
        var status = AgentRuntime.Instance.TeammateLiveStatus;
        if (status != null)
        {
            return Loc.T("队友：{0}", status);
        }

        return LocalDualInstanceLauncher.Connection == null
            ? Loc.T("队友：未组队")
            : Loc.T("队友：读取中…");
    }

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
        _companionMode = UiFactory.Combo();
        _companionMode.AddItem(Loc.T("AI 自动游玩"));
        _companionMode.AddItem(Loc.T("外部 Agent"));
        _companionMode.Select(AgentRuntime.Instance.Settings.CompanionControlMode == "external" ? 1 : 0);
        _companionMode.ItemSelected += index =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.CompanionControlMode = index == 1 ? "external" : "autoplay";
            AgentRuntime.Instance.SaveSettings(next);
            RefreshDynamic();
        };
        page.AddChild(Labeled(Loc.T("队友模式"), _companionMode));
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
        _companionChoiceToggle = UiFactory.Check(Loc.T("手动选队友角色"), !AgentRuntime.Instance.Settings.CompanionAutoSelectCharacter);
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
        _dualStatus = UiFactory.Wrapped(Loc.T("队友未加入"), UiFactory.FontBody, muted: true);
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
        _dualContinueButton = UiFactory.Button(Loc.T("继续联机存档"), () => _ = ContinueDualAsync());
        _teamPause = UiFactory.Button(Loc.T("暂停队友"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(false, CancellationToken.None));
        _teamResume = UiFactory.Button(Loc.T("继续游玩"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(true, CancellationToken.None));
        _teamControlStatus = UiFactory.Wrapped(AgentRuntime.Instance.TeamControlStatus, UiFactory.FontCaption, muted: true);
        _teamControlRow = UiFactory.Row(_teamPause, _teamResume);
        // The card shows one phase at a time (see RefreshCoopPhase): before a teammate exists, the
        // character switch, invite and -- when a save exists -- continue; once one exists, pause and
        // resume. It used to show all nine controls at once, most of them disabled.
        page.AddChild(HelpCard.Build(
            "coop.invite",
            Loc.T("邀请与控制"),
            Loc.T("你打自己的角色，AI 打另一个角色，同一座塔往上爬。\n在主菜单邀请；第二窗口打开后，AI 会自动选角、过开局事件并进图。\n大厅仍是 4 人位，还可再邀 2 名在线玩家。\n队友窗口连不上时，检查游戏日志和 Steam 双开限制。"),
            _companionChoiceToggle,
            _dualHint,
            _dualLaunchButton,
            // In-game entry for continue_ai_teammate: the game's own Load button opens a saved co-op
            // run over Steam networking and rejects the local-connection NetIds, so the way back into
            // a saved run has to live here, under the invite.
            _dualContinueButton,
            _teamControlRow,
            _teamControlStatus));

        // Diagnostics and the stats reset are for when something went wrong, so they sit as a
        // secondary row at the bottom of the status card instead of between invite and pause.
        var tools = new HFlowContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        tools.AddThemeConstantOverride("h_separation", UiFactory.SpaceXs);
        tools.AddThemeConstantOverride("v_separation", UiFactory.SpaceXs);
        tools.AddChild(UiFactory.Button(Loc.T("复制诊断"), CopyDiagnostics, UiFactory.ButtonKind.Ghost));
        tools.AddChild(UiFactory.Button(Loc.T("保存诊断"), SaveDiagnostics, UiFactory.ButtonKind.Ghost));
        tools.AddChild(_resetStatsButton);
        page.AddChild(UiFactory.Card(
            Loc.T("组队状态"),
            _sessionHeadline,
            _sessionDetail,
            _sessionNext,
            _sessionConfigNotice,
            _teammateLivePill,
            _dualStatus,
            tools));

        _teamChat = UiFactory.Rich();
        _teamChat.FitContent = false;
        _teamChat.CustomMinimumSize = new Vector2(0, 120);
        _teamChatView = new RichLog(_teamChat, RichLog.LogAnchor.Bottom);
        _teamInput = UiFactory.Multiline("", 56);
        _teamInput.PlaceholderText = Loc.T("对队友说点什么…");
        // A text editor reports its placeholder as its minimum width, and the placeholder here is a full
        // question -- measured live on 2026-09-21 as the 525-pixel panel container that was still
        // pushing the co-op page past its 440-pixel panel after the labels were fixed. Set after the
        // placeholder, because the editor recomputes its minimum when the text it holds changes.
        _teamInput.CustomMinimumSize = new Vector2(UiFactory.MinReflowWidth, 56);
        _teamSend = UiFactory.Button(Loc.T("发送"), () => _ = SendTeamMessageAsync(), UiFactory.ButtonKind.Primary);
        _teamStatus = UiFactory.Wrapped(AgentRuntime.Instance.TeamStatus, UiFactory.FontCaption);
        page.AddChild(HelpCard.Build(
            "coop.chat",
            Loc.T("队伍交流"),
            Loc.T("聊天不会替你出牌，也不会恢复已暂停的队友。\n你的建议会作为队友下一次决策的参考。"),
            _teamChat,
            _teamInput,
            UiFactory.TightRow(_teamSend),
            _teamStatus));

        // The dual-layer toggle for the multiplayer teammate. The companion instance runs its own Jev
        // loop (it has no overlay), so this switch is the host-side control for it.
        _dualLayerCoopCheck = UiFactory.Check(
            Loc.T("双层决策（Jev）"),
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

            parts.Add(Loc.T("来源：{0}", entry.source));
            parts.Add(entry.total_tokens is { } tokens
                ? Loc.T("Token {0}", tokens.ToString("N0"))
                : Loc.T("Token 未知"));
            // The action stays on its own line. A reason such as "Play Dismantle (14 dmg with STR 3)
            // on the 1st Inklet" plus source and token count is wider than the panel even after the
            // rich label wraps, and one joined line is what the 2026-09-22 screenshot clipped.
            lines.Add(Accent($"{entry.id}. {entry.action}") + "\n" + Muted(string.Join("\n", parts)) + "\n\n");
        }

        _decisionLogView.Show(lines);
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

    private Task TestConnectionAsync() => RunModelTestFromUiAsync(null);

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
    /// Repaints every live control on every page, one section at a time.
    /// </summary>
    /// <remarks>
    /// One pass rather than a per-page callback, because the values it writes arrive from paths with
    /// no event to subscribe to -- an action submitted over the HTTP API updates the decision log
    /// without raising <c>Changed</c>. It runs on the game thread, and a section whose control is gone
    /// returns early instead of throwing into the tick.
    ///
    /// The conversation is drawn first and every section is guarded on its own. This used to be one
    /// straight line of blocks inside one try, so the first one that threw -- <c>BuildStatePayload</c>
    /// against a screen that had just gone away was enough -- left every block after it, the
    /// conversation among them, frozen at the last snapshot.
    ///
    /// The logs and labels are cheap to re-assign now: <see cref="RichLog"/> leaves an unchanged log
    /// alone, so running this on every runtime change no longer resets the reader's scroll.
    /// </remarks>
    private void RefreshDynamic()
    {
        // First, so no later section can keep it from being reached.
        Guarded("chat", RefreshChatLog);

        var playing = AgentRuntime.Instance.PlayRunning;
        Guarded("ready", RefreshReadiness);
        Guarded("header", () => RefreshHeader(playing));
        Guarded("solo", () => RefreshSoloSection(playing));

        PlayerFacingView? facing = null;
        Guarded("session", () =>
        {
            var view = AgentRuntime.Instance.PlayerFacing();
            facing = view;
            RefreshDecisionPage(view);
        });
        Guarded("coop", () => RefreshCoopSection(facing));
        Guarded("settings", RefreshSettingsLive);
        Guarded("connect", RefreshConnectSection);

        // The mode switch and the dual-layer toggles mirror settings that can change off this page
        // (a settings save, or the mode switch itself), so the refresh pass re-syncs them rather
        // than trusting the last click to be the only writer. This is also the one place that decides
        // which play and teammate buttons are enabled.
        Guarded("mode", () =>
        {
            var mode = AgentRuntime.Instance.Settings;
            var coopMode = mode.OverlayPlayMode == "coop";
            if (_dualLayerSoloCheck != null) _dualLayerSoloCheck.SetPressedNoSignal(mode.DualLayerSoloEnabled);
            if (_nonCombatOnlyCheck != null) _nonCombatOnlyCheck.SetPressedNoSignal(mode.NonCombatOnlyEnabled);
            if (_dualLayerCoopCheck != null) _dualLayerCoopCheck.SetPressedNoSignal(mode.DualLayerCoopEnabled);
            RefreshModeControls(coopMode, coopMode ? mode.DualLayerCoopEnabled : mode.DualLayerSoloEnabled);
            RefreshJevReading(coopMode, mode);
        });
    }

    /// <summary>Runs one section of the refresh pass; a failure is logged and the next section still runs.</summary>
    private static void Guarded(string section, Action refresh)
    {
        try
        {
            refresh();
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} refresh pass failed in {section}: {ex.Message}");
        }
    }

    /// <summary>The play page's setup line, and the readiness the start buttons are gated on.</summary>
    private void RefreshReadiness()
    {
        var settings = AgentRuntime.Instance.Settings;
        var blocker = FirstRunSetup.PlayBlocker(FirstRunSetup.Evaluate(settings));
        _playReady = blocker == null;
        if (_readinessBanner == null || _readinessText == null)
        {
            return;
        }

        // The external-agent teammate never calls a model, so a missing model blocks nothing there.
        var external = settings.OverlayPlayMode == "coop" && settings.CompanionControlMode == "external";
        _readinessText.Text = blocker ?? "";
        _readinessBanner.Visible = blocker != null && !external;
    }

    /// <summary>The title bar's state tag and the edge tab.</summary>
    private void RefreshHeader(bool playing)
    {
        if (_headerStatus != null)
        {
            _headerStatus.Text = playing ? Loc.T("● 游玩中") : Loc.T("○ 待机");
            _headerStatus.AddThemeColorOverride(
                "font_color",
                playing ? UiFactory.ToGodot(UiFactory.Palette.Positive) : UiFactory.ToGodot(UiFactory.Palette.Muted));
        }

        if (_edgeTab != null)
        {
            _edgeTab.Text = playing ? "AI ▶" : "AI";
        }
    }

    /// <summary>The solo section's turn card and metric tiles.</summary>
    private void RefreshSoloSection(bool playing)
    {
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
            _playSummary.Text = PlaySummaryText();
        }

        if (_playUsage != null)
        {
            // Whole sentence on its own wrapping line (see BuildSoloSection).
            _playUsage.Text = PlayerFacingSession.FormatUsage(
                AgentRuntime.Instance.SessionUsageKnown,
                AgentRuntime.Instance.SessionUsage,
                AgentRuntime.Instance.SessionRequests);
        }

        if (_playToggle != null)
        {
            _playToggle.Text = playing ? Loc.T("暂停") : Loc.T("开始自动游玩");
        }

        if (_stepButton != null)
        {
            _stepButton.Disabled = playing || !_playReady;
            _stepButton.Visible = !playing;
        }
    }

    /// <summary>The auto-play card's facts line: the screen the AI is on and the last action it took.</summary>
    private static string PlaySummaryText()
    {
        string screen;
        try
        {
            screen = GameStateService.CurrentScreenName();
        }
        catch (Exception ex)
        {
            WarnOnce("screen name read failed", ex);
            screen = "-";
        }

        return Loc.T("屏幕 {0} · 动作 {1}", screen, Trim(AgentRuntime.Instance.LastAction, 48));
    }

    /// <summary>
    /// The multiplayer section: session status, invite and continue, the teammate chip, and the team
    /// chat. <paramref name="facing"/> is null when reading it failed; the status lines then keep
    /// their last text.
    /// </summary>
    private void RefreshCoopSection(PlayerFacingView? facing)
    {
        if (facing is { } view)
        {
            if (_sessionHeadline != null) _sessionHeadline.Text = view.Headline;
            if (_sessionDetail != null) _sessionDetail.Text = view.Detail;
            if (_sessionNext != null) _sessionNext.Text = Loc.T("下一步：{0}", view.NextAction);
        }

        if (_resetStatsButton != null)
        {
            _resetStatsButton.Disabled = !CanResetSessionStats();
        }

        if (_sessionConfigNotice != null)
        {
            _sessionConfigNotice.Text = FormatSettingsNotice();
            _sessionConfigNotice.Visible = AgentRuntime.Instance.SettingsNotice.HasMessage;
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
            _dualLaunchButton.Text = AgentRuntime.Instance.DualLaunching && !_continueLaunching ? Loc.T("邀请中…") : Loc.T("邀请 AI 队友");
        }

        if (_dualContinueButton != null)
        {
            _dualContinueButton.Text = AgentRuntime.Instance.DualLaunching && _continueLaunching ? Loc.T("正在读档…") : Loc.T("继续联机存档");
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
            _teamSend.Text = AgentRuntime.Instance.TeamMessagePending ? Loc.T("等待回复…") : Loc.T("发送");
        }

        if (_teamStatus != null) _teamStatus.Text = AgentRuntime.Instance.TeamStatus;
        if (_teamControlStatus != null) _teamControlStatus.Text = AgentRuntime.Instance.TeamControlStatus;
        if (_teamChatView != null)
        {
            var team = new List<string>();
            foreach (var turn in AgentRuntime.Instance.TeamHistory)
            {
                team.Add(FormatTurn(turn.Role == "user" ? Loc.T("你") : Loc.T("AI 队友"), turn.Text));
            }

            _teamChatView.Show(team);
        }
    }

    /// <summary>The settings page's live lines: load notice, budget hint, model probes, stats reset.</summary>
    private void RefreshSettingsLive()
    {
        if (_settingsResetStatsButton != null)
        {
            _settingsResetStatsButton.Disabled = !CanResetSessionStats();
        }

        if (_settingsLoadNotice != null)
        {
            _settingsLoadNotice.Text = FormatSettingsNotice();
            _settingsLoadNotice.Visible = _settingsLoadNotice.Text.Length > 0;
        }

        if (_budgetHint != null && !string.IsNullOrWhiteSpace(_budgetInputError))
        {
            _budgetHint.Text = _budgetInputError;
            _budgetHint.Visible = true;
        }

        if (_quickTestBadge != null)
        {
            _quickTestBadge.Text = QuickTestBadgeText();
        }

        if (_playTest != null)
        {
            var firstRun = FirstRunSetup.Evaluate(AgentRuntime.Instance.Settings);
            if (_conversationTest != null) _conversationTest.Text = ModelRoleProbe.FormatLine(firstRun.Conversation);
            _playTest.Text = ModelRoleProbe.FormatLine(firstRun.Play);
            if (_visionTest != null) _visionTest.Text = ModelRoleProbe.FormatLine(firstRun.Vision);
        }
    }

    /// <summary>The settings page's MCP section.</summary>
    private void RefreshConnectSection()
    {
        if (_mcpStatus != null)
        {
            _mcpStatus.Text = AgentRuntime.Instance.McpStatus;
        }

        if (_mcpToggle != null)
        {
            _mcpToggle.SetPressedNoSignal(AgentRuntime.Instance.McpRunning);
        }

        if (_apiAddressLabel != null)
        {
            _apiAddressLabel.Text = Loc.T("本机 API：{0}", HttpServer.Instance.Prefix);
        }

        if (_mcpClientCard != null)
        {
            _mcpClientCard.Visible = AgentRuntime.Instance.McpRunning;
        }

        if (_mcpUrlLabel != null)
        {
            var url = AgentRuntime.Instance.McpUrl;
            _mcpUrlLabel.Text = string.IsNullOrWhiteSpace(url) ? "" : Loc.T("地址：{0}", url);
        }

        // Only on a real change: assigning a TextEdit's text resets its caret, selection and scroll,
        // so a player selecting the config to copy it lost the selection on every refresh.
        var mcpConfig = AgentRuntime.Instance.McpClientConfigFor(_mcpClient);
        if (_mcpConfigEdit != null && _mcpConfigEdit.Text != mcpConfig)
        {
            _mcpConfigEdit.Text = mcpConfig;
        }
    }

    private static bool CanResetSessionStats()
    {
        return SessionBudgetLimits.CanResetSessionStats(
            AgentRuntime.Instance.PlayRunning,
            AgentRuntime.Instance.PlayPhase);
    }

    /// <summary>
    /// One chat turn as the log draws it: a coloured speaker, then the body with any BBCode in it
    /// neutralised. A turn that arrives containing <c>[b]</c> must not be able to restyle the panel
    /// it is drawn in.
    /// </summary>
    private static string FormatTurn(string speaker, string text)
    {
        return Accent(speaker) + "\n" + BbText.Escape(text) + "\n\n";
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
        return BbText.Colored(UiFactory.Accent.ToHtml(false), text, bold: true);
    }

    /// <summary>BBCode in the muted colour, for hints and empty states.</summary>
    private static string Muted(string text)
    {
        return BbText.Colored(UiFactory.Muted.ToHtml(false), text);
    }
}
