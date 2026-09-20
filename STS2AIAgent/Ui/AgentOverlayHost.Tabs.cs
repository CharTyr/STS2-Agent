using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The overlay's tab construction: the tab state, the header row, one page per
/// <see cref="OverlayTabCatalog"/> entry, and the settings form that fills the settings page.
/// </summary>
/// <remarks>
/// These members were moved here from <c>AgentOverlayHost.cs</c> unchanged. That file is the one the
/// size ratchet and the architecture table watch, and the sixth tab would otherwise have grown it
/// instead of landing next to the five pages it belongs with. The tab list itself is data in
/// <see cref="OverlayTabCatalog"/>, so which tabs exist, what they are called and which ones re-read
/// on entry live in one place that compiles offline.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>The built page per tab id, and the tab currently shown. Tab state stays with the tab code.</summary>
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private string _tab = "chat";

    private RichTextLabel? _decisionLog;
    private Label? _decisionUsage;

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
            OverlayTabCatalog.Chat => BuildChatPage(),
            OverlayTabCatalog.Settings => BuildSettingsPage(),
            OverlayTabCatalog.Play => BuildPlayPage(),
            OverlayTabCatalog.Dual => BuildDualPage(),
            OverlayTabCatalog.Connect => BuildConnectPage(),
            OverlayTabCatalog.Decisions => BuildDecisionPage(),
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
        foreach (var (id, page) in _pages)
        {
            page.Visible = string.Equals(id, tab, StringComparison.Ordinal);
        }

        if (_chatFooter != null) _chatFooter.Visible = tab == OverlayTabCatalog.Chat;
        // The Continue button's availability follows the current screen, which changes without any
        // runtime event; re-read it whenever this tab comes into view.
        if (OverlayTabCatalog.RefreshesOnShow(tab)) RefreshDynamic();
    }

    private Control BuildTabs()
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var tab in OverlayTabCatalog.Tabs)
        {
            var button = UiFactory.Button(OverlayTabCatalog.Label(tab.Id), () => ShowTab(tab.Id));
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(button);
        }

        return row;
    }

    private Control BuildChatPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
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
        footer.AddChild(UiFactory.Row(_attachState, _attachShot));
        footer.AddChild(_allowAct);
        _chatInput = UiFactory.Multiline("", 70);
        footer.AddChild(_chatInput);
        _sendButton = UiFactory.Button(Loc.T("发送"), () => _ = SendChatAsync());
        var clear = UiFactory.Button(Loc.T("清空"), () => AgentRuntime.Instance.ClearChat());
        footer.AddChild(UiFactory.Row(_sendButton, clear));
        return footer;
    }

    private Control BuildSettingsPage()
    {
        var page = UiFactory.Column();
        _settingsBody = UiFactory.Column();
        page.AddChild(UiFactory.Scroll(_settingsBody, 80));
        var addEndpoint = UiFactory.Button(Loc.T("添加端点"), AddEndpoint);
        var addModel = UiFactory.Button(Loc.T("添加模型"), AddModel);
        var save = UiFactory.Button(Loc.T("保存设置"), SaveSettingsFromUi);
        var test = UiFactory.Button(Loc.T("测试连接"), () => _ = TestConnectionAsync());
        page.AddChild(UiFactory.Row(addEndpoint, addModel));
        page.AddChild(UiFactory.Row(save, test));
        RebuildSettingsForm();
        return page;
    }

    private Control BuildPlayPage()
    {
        var page = UiFactory.Column();
        _playStatus = UiFactory.Label(Loc.T("状态：-"));
        _playScreen = UiFactory.Label(Loc.T("屏幕：-"));
        _playAction = UiFactory.Label(Loc.T("最近动作：-"));
        _playThought = UiFactory.Label(Loc.T("思考：-"), 13, muted: true);
        _playToggle = UiFactory.Button(Loc.T("开始自动游玩"), TogglePlay);
        _stepButton = UiFactory.Button(Loc.T("单步"), () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        page.AddChild(_playStatus);
        page.AddChild(_playScreen);
        page.AddChild(_playAction);
        page.AddChild(_playThought);
        _playUsage = UiFactory.Label(Loc.T("Token 消耗：-"), 13, muted: true);
        page.AddChild(_playUsage);
        page.AddChild(UiFactory.Row(_playToggle, _stepButton));
        page.AddChild(UiFactory.Label(Loc.T("自动游玩走 compact 状态和工具，与 MCP 相同，不需要视觉即可打完全部流程。对话默认只读；勾选「允许代打」或明确说「帮我打」才会执行动作。"), 12, muted: true));
        return page;
    }

    private Control BuildDualPage()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Label(Loc.T("和 AI 一起爬塔"), 18));
        page.AddChild(UiFactory.Label(Loc.T("可以一起玩：你打自己的角色，AI 打另一个角色，同一座塔往上爬。大厅仍是 4 人位，还可以再邀 2 名在线玩家。"), 13));
        _sessionHeadline = UiFactory.Label(Loc.T("状态：-"), 16);
        _sessionDetail = UiFactory.Label("-", 13, muted: true);
        _sessionNext = UiFactory.Label(Loc.T("下一步：-"), 13);
        page.AddChild(_sessionHeadline);
        page.AddChild(_sessionDetail);
        page.AddChild(_sessionNext);
        _sessionConfigNotice = UiFactory.Label("", 12);
        page.AddChild(_sessionConfigNotice);
        _resetStatsButton = UiFactory.Button(Loc.T(SessionBudgetLimits.ResetStatsActionLabel), ResetSessionStatsFromUi);
        page.AddChild(_resetStatsButton);
        _firstRunHint = UiFactory.Label(FirstRunHintText(), 13, muted: true);
        page.AddChild(_firstRunHint);
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
        page.AddChild(_companionChoiceToggle);
        _dualHint = UiFactory.Label(DualHintText(), 12, muted: true);
        page.AddChild(_dualHint);
        page.AddChild(UiFactory.Button(Loc.T("导出诊断"), CopyDiagnostics));
        _dualLaunchButton = UiFactory.Button(Loc.T("邀请 AI 队友"), () => _ = LaunchDualAsync());
        page.AddChild(_dualLaunchButton);
        // In-game entry for continue_ai_teammate: the game's own Load button opens a saved co-op run
        // over Steam networking and rejects the local-connection NetIds, so the way back into a saved
        // run has to live here, one row under the invite.
        _dualContinueButton = UiFactory.Button(Loc.T("继续上次联机对局"), () => _ = ContinueDualAsync());
        page.AddChild(_dualContinueButton);
        _dualStatus = UiFactory.Label(Loc.T("队友尚未加入。"), 13, muted: true);
        page.AddChild(_dualStatus);
        _teamPause = UiFactory.Button(Loc.T("暂停队友"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(false, CancellationToken.None));
        _teamResume = UiFactory.Button(Loc.T("继续游玩"), () => _ = AgentRuntime.Instance.ControlTeammateAsync(true, CancellationToken.None));
        page.AddChild(UiFactory.Row(_teamPause, _teamResume));
        _teamControlStatus = UiFactory.Label(AgentRuntime.Instance.TeamControlStatus, 12, muted: true);
        page.AddChild(_teamControlStatus);
        page.AddChild(UiFactory.Label(Loc.T("队伍交流"), 15));
        _teamChat = UiFactory.Rich();
        _teamChat.FitContent = false;
        _teamChat.CustomMinimumSize = new Vector2(0, 130);
        page.AddChild(_teamChat);
        _teamInput = UiFactory.Multiline("", 56);
        _teamInput.PlaceholderText = Loc.T("一起集火哪个敌人？这条路线你怎么看？");
        page.AddChild(_teamInput);
        _teamSend = UiFactory.Button(Loc.T("和队友说"), () => _ = SendTeamMessageAsync());
        page.AddChild(_teamSend);
        _teamStatus = UiFactory.Label(AgentRuntime.Instance.TeamStatus, 12, muted: true);
        page.AddChild(_teamStatus);
        page.AddChild(UiFactory.Label(Loc.T("聊天不会替你出牌，也不会恢复已暂停的队友。建议会供队友下一次决策参考。"), 12, muted: true));
        page.AddChild(UiFactory.Label(Loc.T("如果队友窗口未能连接，请检查游戏日志和 Steam 双开限制。"), 12, muted: true));
        return page;
    }

    private Control BuildConnectPage()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Label(Loc.T("MCP 接入"), 15));
        page.AddChild(UiFactory.Label(Loc.T("选择：一起玩只用游戏内窗口，不必打开 MCP。外部客户端用本页开关。Python sidecar 仅 stdio / layered / full。"), 12, muted: true));
        page.AddChild(UiFactory.Label(Loc.T("游戏内自动打不需要打开。只有 Cursor / Claude / Codex 等外部客户端才需要。"), 12, muted: true));
        _mcpToggle = UiFactory.Check(Loc.T("打开 MCP 服务"), AgentRuntime.Instance.McpRunning);
        _mcpToggle.Toggled += on => AgentRuntime.Instance.SetMcpEnabled(on);
        page.AddChild(_mcpToggle);
        _mcpStatus = UiFactory.Label(AgentRuntime.Instance.McpStatus, 12, muted: true);
        page.AddChild(_mcpStatus);

        _mcpInfoBox = UiFactory.Column();
        _mcpUrlLabel = UiFactory.Label("", 13);
        _mcpInfoBox.AddChild(_mcpUrlLabel);
        var copyUrl = UiFactory.Button(Loc.T("复制地址"), () =>
        {
            var url = AgentRuntime.Instance.McpUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                CopyText(url);
            }
        });
        var copyCfg = UiFactory.Button(Loc.T("复制配置"), () => CopyText(AgentRuntime.Instance.McpClientConfig));
        _mcpInfoBox.AddChild(UiFactory.Row(copyUrl, copyCfg));
        _mcpConfigEdit = UiFactory.Multiline("", 120);
        _mcpConfigEdit.Editable = false;
        _mcpInfoBox.AddChild(_mcpConfigEdit);
        _mcpInfoBox.AddChild(UiFactory.Label(Loc.T("把配置贴进外部客户端的 MCP 设置。服务只监听本机 127.0.0.1。"), 12, muted: true));
        _mcpInfoBox.Visible = AgentRuntime.Instance.McpRunning;
        page.AddChild(_mcpInfoBox);

        page.AddChild(UiFactory.Label(Loc.T("本机 HTTP API 始终可用：GET /health /state ，POST /action。MCP 打开后才会在同一端口暴露 /mcp。地址以本页复制为准，不要写死 8080 或 8765。"), 12, muted: true));
        return page;
    }

    /// <summary>
    /// The decision log tab: this session's usage on top, then the recorded decisions newest first.
    /// </summary>
    private Control BuildDecisionPage()
    {
        var page = UiFactory.Column();
        _decisionUsage = UiFactory.Label(Loc.T("Token 消耗：-"), 13, muted: true);
        page.AddChild(_decisionUsage);
        page.AddChild(UiFactory.Label(Loc.T("最新在前：动作、理由、来源，以及该步消耗的 Token。"), 12, muted: true));
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

        if (_decisionLog == null)
        {
            return;
        }

        _decisionLog.Clear();
        var lines = DecisionLogView.Lines(AgentRuntime.Instance.RecentDecisions(DecisionLogView.RecentLimit));
        if (lines.Count == 0)
        {
            _decisionLog.AppendText("[color=#b3b3ad]" + Loc.T("还没有决策记录。自动游玩或外部客户端执行动作后会出现在这里。") + "[/color]\n");
            return;
        }

        foreach (var line in lines)
        {
            _decisionLog.AppendText(Escape(line) + "\n");
        }
    }

    private void RebuildSettingsForm()
    {
        if (_settingsBody == null)
        {
            return;
        }

        _rebuildingSettings = true;
        try
        {
        foreach (var child in _settingsBody.GetChildren().ToArray())
        {
            _settingsBody.RemoveChild(child);
            child.QueueFree();
        }

        _endpointEditors.Clear();
        _modelEditors.Clear();
        var settings = CloneSettings(AgentRuntime.Instance.Settings);
        var firstRun = FirstRunSetup.Evaluate(settings);

        _saveStatus = UiFactory.Label(_settingsDirty ? Loc.T("未保存") : Loc.T("已保存"), 12, muted: true);
        _settingsBody.AddChild(_saveStatus);
        _settingsLoadNotice = UiFactory.Label(FormatSettingsNotice(), 12);
        _settingsBody.AddChild(_settingsLoadNotice);
        _settingsBody.AddChild(UiFactory.Label(Loc.T("首次配置"), 15));
        _settingsBody.AddChild(UiFactory.Label(Loc.T("添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试连接 → 保存。通过后再去「AI 队友」从主菜单邀请。默认网址和模型名不算已经可用。"), 12, muted: true));
        _testNotice = UiFactory.Label(Loc.T("测试连接会向配置的服务发送测试请求。对话通过不等于游玩已通过。本地服务可以留空 API Key。"), 12, muted: true);
        _settingsBody.AddChild(_testNotice);
        _conversationTest = UiFactory.Label(ModelRoleProbe.FormatLine(firstRun.Conversation), 12, muted: true);
        _playTest = UiFactory.Label(ModelRoleProbe.FormatLine(firstRun.Play), 12, muted: true);
        _visionTest = UiFactory.Label(ModelRoleProbe.FormatLine(firstRun.Vision), 12, muted: true);
        _settingsBody.AddChild(_conversationTest);
        _settingsBody.AddChild(_playTest);
        _settingsBody.AddChild(_visionTest);
        if (firstRun.Play.Status == "failed" && !string.IsNullOrWhiteSpace(firstRun.Play.NextStep))
        {
            _settingsBody.AddChild(UiFactory.Label(Loc.T("下一步：{0}", firstRun.Play.NextStep), 12));
        }

        _deleteWarning = UiFactory.Label("", 12);
        _settingsBody.AddChild(_deleteWarning);

        _settingsBody.AddChild(UiFactory.Label(Loc.T("端点"), 15));
        for (var i = 0; i < settings.Endpoints.Count; i++)
        {
            _settingsBody.AddChild(BuildEndpointCard(settings.Endpoints[i], i));
        }

        _settingsBody.AddChild(UiFactory.Label(Loc.T("模型"), 15));
        for (var i = 0; i < settings.Models.Count; i++)
        {
            _settingsBody.AddChild(BuildModelCard(settings.Models[i], i, settings));
        }

        _settingsBody.AddChild(UiFactory.Label(Loc.T("角色绑定"), 15));
        _conversationCombo = FillModelCombo(settings, settings.ConversationModelId, includeEmpty: false);
        _playCombo = FillModelCombo(settings, settings.PlayModelId, includeEmpty: true);
        _visionCombo = FillModelCombo(settings, settings.VisionModelId, includeEmpty: true);
        _settingsBody.AddChild(Labeled(Loc.T("主对话模型"), _conversationCombo));
        _settingsBody.AddChild(Labeled(Loc.T("游玩模型（可空=主对话）"), _playCombo));
        _settingsBody.AddChild(Labeled(Loc.T("外挂视觉模型（可空）"), _visionCombo));
        WatchCombo(_conversationCombo);
        WatchCombo(_playCombo);
        WatchCombo(_visionCombo);
        _showAdvanced = UiFactory.Check(Loc.T("显示高级选项"), _showAdvancedValue);
        _showAdvanced.Toggled += on =>
        {
            _showAdvancedValue = on;
            FlushSettingsIfDirty();
            RebuildSettingsForm();
        };
        _settingsBody.AddChild(_showAdvanced);
        if (_showAdvancedValue)
        {
            _settingsBody.AddChild(UiFactory.Label(Loc.T("视觉可选。不勾选「视觉」、不配外挂视觉时，仍用 compact 状态与工具打完全部内容。"), 11, muted: true));
            _hotkeyEdit = UiFactory.Line(settings.Hotkey, "F8");
            WatchLine(_hotkeyEdit);
            _settingsBody.AddChild(Labeled(Loc.T("开关热键"), _hotkeyEdit));
            _maxTokensEdit = UiFactory.Line(settings.MaxSessionTokens?.ToString() ?? "", Loc.T("不限（留空或0）"));
            _maxRequestsEdit = UiFactory.Line(settings.MaxSessionRequests?.ToString() ?? "", Loc.T("不限（留空或0）"));
            WatchLine(_maxTokensEdit);
            WatchLine(_maxRequestsEdit);
            _settingsBody.AddChild(Labeled(Loc.T("会话 Token 上限"), _maxTokensEdit));
            _settingsBody.AddChild(Labeled(Loc.T("会话请求上限"), _maxRequestsEdit));
            _budgetHint = UiFactory.Label(_budgetInputError ?? Loc.T("非法预算输入会保留原来的安全上限，不会静默变成不限。"), 11, muted: true);
            _settingsBody.AddChild(_budgetHint);
            _settingsBody.AddChild(UiFactory.Label(Loc.T("预算护栏：达到上限时优雅停止自动游玩并提示，避免意外耗尽额度。"), 11, muted: true));
            _settingsResetStatsButton = UiFactory.Button(Loc.T(SessionBudgetLimits.ResetStatsActionLabel), ResetSessionStatsFromUi);
            _settingsBody.AddChild(_settingsResetStatsButton);
            _proactiveChatToggle = UiFactory.Check(Loc.T("主动发言（AI 队友偶尔主动说一句）"), settings.ProactiveChatEnabled);
            WatchCheck(_proactiveChatToggle);
            _settingsBody.AddChild(_proactiveChatToggle);
            _proactiveToneCombo = UiFactory.Combo();
            foreach (var option in ProactiveChatTones.Options)
            {
                _proactiveToneCombo.AddItem(option.Label);
                _proactiveToneCombo.SetItemMetadata(_proactiveToneCombo.ItemCount - 1, option.Id);
            }
            SelectByText(_proactiveToneCombo, ProactiveChatTones.Label(settings.ProactiveChatTone));
            WatchCombo(_proactiveToneCombo);
            _settingsBody.AddChild(Labeled(Loc.T("交流风格"), _proactiveToneCombo));
            _settingsBody.AddChild(UiFactory.Label(Loc.T("默认关闭。开启后仅在战斗开始与结束时各说一句，每次开始自动游玩最多 6 句，两句之间至少间隔 75 秒（暂停或继续自动游玩不会缩短这个间隔）；不会代打，也遵守预算上限。"), 11, muted: true));
            _settingsBody.AddChild(UiFactory.Button(Loc.T("重置窗口位置"), ResetPlacement));
            _settingsBody.AddChild(UiFactory.Label(Loc.T("拖动标题栏可移动窗口，位置会保存。"), 11, muted: true));
            _settingsBody.AddChild(UiFactory.Label(Loc.T("配置文件：{0}", AgentRuntime.Instance.SettingsPath), 11, muted: true));
        }
        else
        {
            _hotkeyEdit = null;
            _maxTokensEdit = null;
            _maxRequestsEdit = null;
            _budgetHint = null;
            _settingsResetStatsButton = null;
            _proactiveChatToggle = null;
            _proactiveToneCombo = null;
        }
        }
        finally
        {
            _rebuildingSettings = false;
        }
    }

    private Control BuildEndpointCard(LlmEndpoint endpoint, int index)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle(UiFactory.BgRaised, 6));
        var column = UiFactory.Column();
        var name = UiFactory.Line(endpoint.Name, Loc.T("名称"));
        var url = UiFactory.Line(endpoint.BaseUrl, "https://api.openai.com/v1");
        var key = UiFactory.Line(endpoint.ApiKey, "API Key", secret: true);
        WatchLine(name);
        WatchLine(url);
        WatchLine(key);
        var enabled = UiFactory.Check(Loc.T("启用"), endpoint.Enabled);
        WatchCheck(enabled);
        var remove = UiFactory.Button(Loc.T("删除"), () => RemoveEndpoint(index));
        column.AddChild(UiFactory.Row(name, enabled, remove));
        column.AddChild(url);
        column.AddChild(key);
        box.AddChild(column);
        _endpointEditors.Add(new EndpointEditors(endpoint.Id, name, url, key, enabled));
        return box;
    }

    private Control BuildModelCard(LlmModelConfig model, int index, AgentSettings settings)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle(UiFactory.BgRaised, 6));
        var column = UiFactory.Column();
        var display = UiFactory.Line(model.DisplayName, Loc.T("显示名"));
        var modelName = UiFactory.Line(model.Model, Loc.T("模型名"));
        WatchLine(display);
        WatchLine(modelName);
        var endpointCombo = UiFactory.Combo();
        var selectedEndpointIndex = -1;
        for (var i = 0; i < settings.Endpoints.Count; i++)
        {
            var endpoint = settings.Endpoints[i];
            endpointCombo.AddItem(string.IsNullOrWhiteSpace(endpoint.Name) ? endpoint.Id : endpoint.Name);
            endpointCombo.SetItemMetadata(i, endpoint.Id);
            if (string.Equals(endpoint.Id, model.EndpointId, StringComparison.OrdinalIgnoreCase))
            {
                selectedEndpointIndex = i;
            }
        }

        if (selectedEndpointIndex < 0)
        {
            var missingEndpoint = string.IsNullOrWhiteSpace(model.EndpointId)
                ? Loc.T("(未绑定端点)")
                : Loc.T("(当前端点不可用：{0})", model.EndpointId);
            endpointCombo.AddItem(missingEndpoint);
            endpointCombo.SetItemMetadata(endpointCombo.ItemCount - 1, model.EndpointId ?? "");
            selectedEndpointIndex = endpointCombo.ItemCount - 1;
        }

        endpointCombo.Selected = selectedEndpointIndex;
        WatchCombo(endpointCombo);

        var vision = UiFactory.Check(Loc.T("视觉"), model.SupportsVision);
        var tools = UiFactory.Check(Loc.T("工具调用"), model.SupportsTools);
        WatchCheck(vision);
        WatchCheck(tools);
        var thinkingMode = UiFactory.Combo();
        foreach (var item in new[] { "auto", "reasoning_effort", "deepseek", "prompt" })
        {
            thinkingMode.AddItem(item);
        }

        SelectByText(thinkingMode, model.ThinkingMode);
        WatchCombo(thinkingMode);
        var thinkingIntensity = UiFactory.Combo();
        foreach (var item in new[] { "off", "low", "medium", "high" })
        {
            thinkingIntensity.AddItem(item);
        }

        SelectByText(thinkingIntensity, model.ThinkingIntensity);
        WatchCombo(thinkingIntensity);
        var remove = UiFactory.Button(Loc.T("删除"), () => RemoveModel(index));
        column.AddChild(UiFactory.Row(display, remove));
        column.AddChild(UiFactory.Row(modelName, endpointCombo));
        if (_showAdvancedValue)
        {
            column.AddChild(UiFactory.Row(vision, tools));
            column.AddChild(Labeled(Loc.T("思考方式"), thinkingMode));
            column.AddChild(Labeled(Loc.T("思考强度"), thinkingIntensity));
        }
        box.AddChild(column);
        _modelEditors.Add(new ModelEditors(model.Id, display, modelName, endpointCombo, vision, tools, thinkingMode, thinkingIntensity));
        return box;
    }

    private static Control Labeled(string label, Control child)
    {
        var row = new HBoxContainer();
        var text = UiFactory.Label(label, 13);
        text.CustomMinimumSize = new Vector2(160, 0);
        text.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(child);
        return row;
    }

    private static OptionButton FillModelCombo(AgentSettings settings, string? selectedId, bool includeEmpty)
    {
        var combo = UiFactory.Combo();
        var selectedIndex = -1;
        if (includeEmpty)
        {
            combo.AddItem(Loc.T("(默认/无)"));
            combo.SetItemMetadata(0, "");
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedIndex = 0;
            }
        }

        var start = includeEmpty ? 1 : 0;
        if (!includeEmpty && string.IsNullOrWhiteSpace(selectedId))
        {
            combo.AddItem(Loc.T("(未选择)"));
            combo.SetItemMetadata(0, "");
            selectedIndex = 0;
            start = 1;
        }

        for (var i = 0; i < settings.Models.Count; i++)
        {
            var model = settings.Models[i];
            combo.AddItem(model.Label);
            combo.SetItemMetadata(start + i, model.Id);
            if (string.Equals(model.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = start + i;
            }
        }

        if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(selectedId))
        {
            combo.AddItem(Loc.T("(当前绑定不可用：{0})", selectedId));
            combo.SetItemMetadata(combo.ItemCount - 1, selectedId);
            selectedIndex = combo.ItemCount - 1;
        }

        combo.Selected = selectedIndex;
        return combo;
    }
}
