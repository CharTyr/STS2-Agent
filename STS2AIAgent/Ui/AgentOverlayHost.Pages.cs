using System.Collections.Generic;
using Godot;
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
    /// <summary>The chat page's first-run state: shown while no turn has been recorded.</summary>
    private Control? _chatEmpty;

    /// <summary>The decision page's three live labels: usage, run spend, and the log itself.</summary>
    private RichTextLabel? _decisionLog;
    private Label? _decisionUsage;
    private Label? _decisionRunSpend;

    /// <summary>The teammate line and the tone it is currently drawn in. See <see cref="ApplyTone"/>.</summary>
    private Control? _teammateLivePill;
    private UiFactory.ButtonKind? _teammateLiveTone;

    private Control BuildChatPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };

        // The empty state is a card, not a line of grey text: a first-run player opening the panel got
        // an empty box and no idea what to type. It disappears the moment a turn exists.
        _chatEmpty = UiFactory.Card(
            Loc.T("和 AI 聊聊这局"),
            UiFactory.Wrapped(Loc.T("问它这手牌怎么打、这个遗物值不值得买、刚才那步为什么那么出。")),
            UiFactory.Wrapped(Loc.T("对话默认只读；要它真的动手，勾选下方的「允许代打」。")));
        _chatEmpty.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        page.AddChild(_chatEmpty);

        _chatLog = UiFactory.Rich();
        _chatLog.FitContent = false;
        _chatLog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _chatLog.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = UiFactory.Scroll(_chatLog, 120);
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.AddChild(scroll);
        return page;
    }

    private Control BuildChatFooter()
    {
        var footer = UiFactory.Column();
        footer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var settings = AgentRuntime.Instance.Settings;
        _attachState = UiFactory.Check(Loc.T("附带当前状态"), settings.AttachStateInChat);
        _attachShot = UiFactory.Check(Loc.T("附带截图（视觉）"), settings.AttachScreenshotInChat);
        _allowAct = UiFactory.Check(Loc.T("允许代打"), false);
        _attachState.Toggled += _ => PersistChatFlags();
        _attachShot.Toggled += _ => PersistChatFlags();

        // One row for the three switches and a shorter box for the message. The footer is a fixed cost
        // against a panel that is 72% of the viewport, and two stacked rows of checkboxes plus a
        // 70-pixel editor was spending most of a third of the page on flags the player sets once --
        // height the message log never got back.
        footer.AddChild(UiFactory.Row(_attachState, _attachShot, _allowAct));
        _chatInput = UiFactory.Multiline("", 52);
        footer.AddChild(_chatInput);
        _sendButton = UiFactory.Button(Loc.T("发送"), () => _ = SendChatAsync(), UiFactory.ButtonKind.Primary);
        var clear = UiFactory.Button(Loc.T("清空"), () => AgentRuntime.Instance.ClearChat(), UiFactory.ButtonKind.Ghost);
        footer.AddChild(UiFactory.Row(_sendButton, clear));
        return footer;
    }

    /// <summary>
    /// The play dashboard: a status hero, a three-tile row, then the reasoning.
    /// </summary>
    /// <remarks>
    /// Four flat label rows used to run down this page, which is why "is it playing, and what is it
    /// doing" took a second read every time. The state is now the largest thing on the page and the
    /// three facts that qualify it -- which screen, which action, what it has cost -- are tiles beside
    /// each other where a glance lands, instead of three more lines to read in order.
    /// </remarks>
    private Control BuildPlayPage()
    {
        var page = UiFactory.Column();

        // The hero names the state, and colours it: green while the loop runs, muted while it waits.
        _playStatus = UiFactory.Label(Loc.T("状态：-"), UiFactory.FontTitle);
        _playSummary = UiFactory.Label(Loc.T("等待开始。"), UiFactory.FontCaption, muted: true);
        _playToggle = UiFactory.Button(Loc.T("开始自动游玩"), TogglePlay, UiFactory.ButtonKind.Primary);
        _stepButton = UiFactory.Button(Loc.T("单步"), () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        page.AddChild(UiFactory.Card(
            Loc.T("当前回合"),
            _playStatus,
            _playSummary,
            UiFactory.Row(_playToggle, _stepButton)));

        _playScreen = UiFactory.Label("-", UiFactory.FontHeading);
        _playAction = UiFactory.Label("-", UiFactory.FontHeading);
        _playUsage = UiFactory.Label("-", UiFactory.FontHeading);
        page.AddChild(UiFactory.Row(
            UiFactory.MetricTile(Loc.T("屏幕"), _playScreen),
            UiFactory.MetricTile(Loc.T("最近动作"), _playAction),
            UiFactory.MetricTile(Loc.T("Token"), _playUsage)));

        _playThought = UiFactory.Label("-", UiFactory.FontBody, muted: true);
        page.AddChild(UiFactory.Card(Loc.T("思考"), _playThought));
        page.AddChild(UiFactory.Wrapped(Loc.T("自动游玩走 compact 状态和工具，与 MCP 相同，不需要视觉即可打完全部流程。对话默认只读；勾选「允许代打」或明确说「帮我打」才会执行动作。")));
        return UiFactory.Scroll(page, 120);
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
    private Control BuildDualPage()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Label(Loc.T("和 AI 一起爬塔"), UiFactory.FontTitle));
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
        _dualStatus = UiFactory.Label(Loc.T("队友尚未加入。"), UiFactory.FontBody, muted: true);
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
        _teamControlStatus = UiFactory.Label(AgentRuntime.Instance.TeamControlStatus, UiFactory.FontCaption, muted: true);        page.AddChild(UiFactory.Card(
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
        // The teammate page is the longest one: cards, four action rows and a chat box. Without a
        // scroll container the bottom half is simply unreachable -- the panel clips its contents.
        var scroll = UiFactory.Scroll(page, 120);
        scroll.Resized += () => LayoutDualChat(scroll.Size.Y);
        return scroll;
    }

    /// <summary>
    /// Sizes the teammate chat's log box from the page height, so the log gives up room before the
    /// action rows above it are pushed out of view.
    /// </summary>
    /// <remarks>
    /// A floor as well as a ceiling: below roughly a third of the page an empty two-line log is
    /// useless, and the scroll container can carry the difference. Above it, the log grows with the
    /// window instead of the fixed 130 pixels it used to claim on every window size.
    /// </remarks>
    private void LayoutDualChat(float pageHeight)
    {
        if (_teamChat == null || pageHeight < 120)
        {
            return;
        }

        _teamChat.CustomMinimumSize = new Vector2(0, Math.Clamp(pageHeight * 0.22f, 96f, 220f));
    }

    private Control BuildConnectPage()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Label(Loc.T("MCP 接入"), UiFactory.FontTitle));
        page.AddChild(UiFactory.Wrapped(Loc.T("选择：一起玩只用游戏内窗口，不必打开 MCP。外部客户端用本页开关。Python sidecar 仅 stdio / layered / full。")));
        _mcpToggle = UiFactory.Check(Loc.T("打开 MCP 服务"), AgentRuntime.Instance.McpRunning);
        _mcpToggle.Toggled += on => AgentRuntime.Instance.SetMcpEnabled(on);
        _mcpStatus = UiFactory.Wrapped(AgentRuntime.Instance.McpStatus, UiFactory.FontBody);

        _mcpInfoBox = UiFactory.Column();
        _mcpUrlLabel = UiFactory.Label("", UiFactory.FontBody);
        _mcpInfoBox.AddChild(_mcpUrlLabel);
        var copyUrl = UiFactory.Button(Loc.T("复制地址"), () =>
        {
            var url = AgentRuntime.Instance.McpUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                CopyText(url);
            }
        }, UiFactory.ButtonKind.Primary);
        var copyCfg = UiFactory.Button(Loc.T("复制配置"), () => CopyText(AgentRuntime.Instance.McpClientConfig));
        _mcpInfoBox.AddChild(UiFactory.Row(copyUrl, copyCfg));
        _mcpConfigEdit = UiFactory.Multiline("", 120);
        _mcpConfigEdit.Editable = false;
        _mcpInfoBox.AddChild(_mcpConfigEdit);
        _mcpInfoBox.AddChild(UiFactory.Wrapped(Loc.T("把配置贴进外部客户端的 MCP 设置。服务只监听本机 127.0.0.1。")));
        _mcpInfoBox.Visible = AgentRuntime.Instance.McpRunning;

        page.AddChild(UiFactory.Card(
            Loc.T("服务开关"),
            UiFactory.Wrapped(Loc.T("游戏内自动打不需要打开。只有 Cursor / Claude / Codex 等外部客户端才需要。")),
            _mcpToggle,
            _mcpStatus));
        page.AddChild(UiFactory.Card(Loc.T("客户端配置"), _mcpInfoBox));
        page.AddChild(UiFactory.Wrapped(Loc.T("本机 HTTP API 始终可用：GET /health /state ，POST /action。MCP 打开后才会在同一端口暴露 /mcp。地址以本页复制为准，不要写死 8080 或 8765。")));
        return UiFactory.Scroll(page, 120);
    }

    /// <summary>
    /// The decision log tab: this session's usage on top, then the recorded decisions newest first.
    /// </summary>
    private Control BuildDecisionPage()
    {
        var page = UiFactory.Column();
        _decisionUsage = UiFactory.Label("-", UiFactory.FontHeading);
        _decisionRunSpend = UiFactory.Label("-", UiFactory.FontHeading);

        // Two counters side by side rather than two sentences: "what has this cost me" is the
        // question this page is opened to answer, and a number to read beats a clause to parse.
        page.AddChild(UiFactory.Card(
            Loc.T("用量"),
            UiFactory.Row(
                UiFactory.MetricTile(Loc.T("本次会话"), _decisionUsage),
                UiFactory.MetricTile(Loc.T("本局"), _decisionRunSpend))));
        page.AddChild(UiFactory.Heading(Loc.T("决策记录")));
        page.AddChild(UiFactory.Wrapped(Loc.T("最新在前：动作、理由、来源，以及该步消耗的 Token。")));
        _decisionLog = UiFactory.Rich();
        _decisionLog.FitContent = false;
        // Newest first, so follow-to-bottom would scroll away from the line that just arrived.
        _decisionLog.ScrollFollowing = false;
        _decisionLog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _decisionLog.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(UiFactory.Scroll(_decisionLog, 120));
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
            _decisionLog.AppendText(Accent($"{entry.id}. {entry.action}") + "  " + Muted(string.Join(" · ", parts)) + "\n\n");
        }
    }

    private async Task SendChatAsync()
    {
        var text = _chatInput?.Text ?? string.Empty;
        if (_chatInput != null)
        {
            _chatInput.Text = string.Empty;
        }

        PersistChatFlags();
        await AgentRuntime.Instance.SendChatAsync(
            text,
            _attachState?.ButtonPressed ?? true,
            _attachShot?.ButtonPressed ?? false,
            _allowAct?.ButtonPressed ?? false,
            CancellationToken.None);
    }

    private async Task TestConnectionAsync()
    {
        SaveSettingsFromUi();
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

    /// <summary>How many turns the chat log keeps: enough for the current exchange, not a transcript.</summary>
    private const int ChatTailLimit = 60;

    /// <summary>
    /// Repaints every live control on every page.
    /// </summary>
    /// <remarks>
    /// One pass rather than a per-page callback, because the values it writes arrive from paths with
    /// no event to subscribe to -- an action submitted over the HTTP API updates the decision log
    /// without raising <c>Changed</c>. The guard at the top is what keeps it honest: this runs on the
    /// game thread, and a page whose control is gone returns early instead of throwing into the tick.
    /// </remarks>
    private void RefreshDynamic()
    {
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
            _playStatus.Text = AgentRuntime.Instance.Status;
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
                _playScreen.Text = GameStateService.BuildStatePayload().screen;
            }
            catch
            {
                _playScreen.Text = "-";
            }
        }

        if (_playAction != null)
        {
            _playAction.Text = Trim(AgentRuntime.Instance.LastAction, 24);
        }

        if (_playThought != null)
        {
            var thought = Trim(AgentRuntime.Instance.LastThought, 400);
            _playThought.Text = thought == "-" ? Loc.T("还没有思考记录。开始自动游玩或单步一次后会显示。") : thought;
        }

        if (_playUsage != null)
        {
            _playUsage.Text = Trim(PlayerFacingSession.FormatUsage(
                AgentRuntime.Instance.SessionUsageKnown,
                AgentRuntime.Instance.SessionUsage,
                AgentRuntime.Instance.SessionRequests), 24);
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
        }

        if (_sendButton != null)
        {
            _sendButton.Disabled = playing;
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

        if (_chatLog != null)
        {
            _chatLog.Clear();
            var history = AgentRuntime.Instance.History;
            if (_chatEmpty != null)
            {
                _chatEmpty.Visible = history.Count == 0;
            }

            if (history.Count > 0)
            {
                // Only the tail is drawn. A long session's history would otherwise be re-rendered in
                // full on every refresh, on the game thread, for a box that shows a dozen lines.
                var start = Math.Max(0, history.Count - ChatTailLimit);
                for (var index = start; index < history.Count; index++)
                {
                    var turn = history[index];
                    _chatLog.AppendText(FormatTurn(turn.Role == "user" ? Loc.T("你") : Loc.T("助手"), turn.Text));
                }
            }
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
        CheckBox Tools,
        OptionButton ThinkingMode,
        OptionButton ThinkingIntensity);
}
