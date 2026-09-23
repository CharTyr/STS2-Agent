using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The settings page's form: the endpoint and model editors, the role bindings, the budget fields and
/// the appearance selector, plus the harvest records that read them back.
/// </summary>
/// <remarks>
/// Split out of <c>AgentOverlayHost.Pages.cs</c> for the same reason that file was split out of the
/// host: the settings form is a third of the pages by line count and changes for its own reasons.
/// Keeping it beside the live pages meant a change to the budget hint and a change to the dashboard
/// arrived in the same diff. The harvest records live here because this file is the only thing that
/// constructs them.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>
    /// The settings page's scrollable form and, per section, the heading a jump button scrolls to.
    /// </summary>
    /// <remarks>
    /// The anchors exist because this is the longest page in the panel and it grows with the number of
    /// endpoints and models: a player who wants to change the play model is two screens below a player
    /// who wants to change the theme, and nothing on the page said so. The map is filled as the form
    /// is built, so a section exists in exactly one place -- the heading that is added and the entry
    /// that names it are the same call site.
    /// </remarks>
    private ScrollContainer? _settingsScroll;
    private readonly Dictionary<string, Control> _settingsAnchors = new(StringComparer.Ordinal);
    private bool _settingsJumpPending;

    /// <summary>
    /// The theme the swatch grid shows as selected. The player's choice, not a cache of the file.
    /// </summary>
    /// <remarks>
    /// This field is the single source of truth for the selection, and <see cref="RebuildSettingsForm"/>
    /// seeds it from settings only once. Reading it back from the runtime on every rebuild looks
    /// harmless and is not: the theme change reaches the runtime through a save that rebuilds this page,
    /// so the rebuild could observe the pre-save value and write the default back over the choice the
    /// player just made. Observed live on 2026-09-21 -- pick ivory, press Save, get slate -- and the
    /// only durable fix is to stop treating a persisted copy as the authority.
    /// </remarks>
    private string? _themeSelectedId;
    private Control? _themePicker;

    /// <summary>The Jev section's editors and its connection-test feedback.</summary>
    private LineEdit? _jevBaseUrlEdit;
    private LineEdit? _jevApiKeyEdit;
    private LineEdit? _jevModelEdit;
    private LineEdit? _jevThresholdEdit;
    private LineEdit? _jevTimeoutEdit;
    private Button? _jevTestButton;
    private Label? _jevTestStatus;

    /// <summary>The selection, seeded from settings the first time this page is built.</summary>
    private string SelectedTheme => _themeSelectedId ??= OverlayThemeCatalog.DefaultId;

    /// <summary>
    /// The settings page: jump bar, scrollable form, and a footer that never scrolls away.
    /// </summary>
    /// <remarks>
    /// The footer is the point of this layout. Save used to sit at the bottom of the scrolling column,
    /// which meant that after changing anything in the top half of a long form the player had to scroll
    /// past every endpoint and model to reach the button that commits it -- and the two halves of that
    /// gesture are exactly where a change gets lost to a tab switch. Add and Test stay with it, because
    /// all three act on the form as a whole.
    /// </remarks>
    private Control BuildSettingsPage()
    {
        var page = UiFactory.Column();
        page.AddChild(BuildSettingsJumpBar());

        _settingsBody = UiFactory.Column();
        _settingsScroll = UiFactory.Scroll(_settingsBody, 80);
        page.AddChild(_settingsScroll);

        var addEndpoint = UiFactory.Button(Loc.T("添加端点"), AddEndpoint);
        var addModel = UiFactory.Button(Loc.T("添加模型"), AddModel);
        var save = UiFactory.Button(Loc.T("保存设置"), SaveSettingsFromUi, UiFactory.ButtonKind.Primary);
        var test = UiFactory.Button(Loc.T("测试连接"), () => _ = TestConnectionAsync());
        page.AddChild(UiFactory.Row(addEndpoint, addModel));
        page.AddChild(UiFactory.Row(save, test));
        RebuildSettingsForm();
        return page;
    }

    /// <summary>
    /// The jump bar: one button per section, scrolling that section's heading into view.
    /// </summary>
    /// <remarks>
    /// A flow container rather than a row, for the same reason the tab row is one: five Chinese labels
    /// fit on a line and their English translations do not, and a clipped jump button is worse than a
    /// wrapped one. They are ghost buttons because they navigate rather than act -- the page's one
    /// primary action is Save, and a second filled button beside it would compete with it.
    /// </remarks>
    private Control BuildSettingsJumpBar()
    {
        var bar = new HFlowContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        bar.AddThemeConstantOverride("h_separation", UiFactory.SpaceXs);
        bar.AddThemeConstantOverride("v_separation", UiFactory.SpaceXs);
        foreach (var (id, label) in SettingsSections())
        {
            var button = UiFactory.Button(label, () => JumpToSettingsSection(id), UiFactory.ButtonKind.Ghost);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            // The section is named in the tooltip so the label can stay short enough to fit the bar.
            button.TooltipText = Loc.T("跳到「{0}」", label);
            bar.AddChild(button);
        }

        return bar;
    }

    /// <summary>
    /// The page's sections, in the order the form builds them, as (anchor id, heading label).
    /// </summary>
    /// <remarks>
    /// A method rather than a field: the labels are translated, and a field would freeze them at the
    /// language the overlay was built in.
    /// </remarks>
    private static IReadOnlyList<(string Id, string Label)> SettingsSections() => new[]
    {
        ("first-run", Loc.T("首次配置")),
        ("appearance", Loc.T("外观")),
        ("endpoints", Loc.T("端点")),
        ("models", Loc.T("模型")),
        ("bindings", Loc.T("角色绑定")),
        ("jev", Loc.T("Jev 执行层")),
        ("connect", Loc.T("接入"))
    };

    /// <summary>
    /// Adds a section heading to the form and registers it as the anchor its jump button scrolls to.
    /// </summary>
    private void AddSettingsSection(string id, string label)
    {
        var heading = UiFactory.Label(label, UiFactory.FontHeading);
        _settingsAnchors[id] = heading;
        _settingsBody!.AddChild(heading);
    }

    /// <summary>
    /// Scrolls the named section to the top of the form.
    /// </summary>
    /// <remarks>
    /// The offset is computed and assigned rather than delegated to <c>EnsureControlVisible</c>, whose
    /// contract is "scroll the least amount that makes this visible": jumping downwards that puts the
    /// heading exactly against the bottom edge of the viewport with all of its content below the fold --
    /// reported live on 2026-09-21 as "it only pulls the heading into view". A jump has to land the
    /// heading at the top, which is what a reader means by going to a section.
    ///
    /// The scroll is handed to the next idle frame rather than done here. A control's position is only
    /// meaningful after the container that lays it out has run, and a jump from a button press happens
    /// before that -- scrolling immediately would use last frame's offsets and land short, which looks
    /// like a button that half works. The pending flag keeps a burst of clicks from queueing a scroll
    /// per click.
    /// </remarks>
    private void JumpToSettingsSection(string id)
    {
        if (_settingsScroll == null || !_settingsAnchors.TryGetValue(id, out var anchor))
        {
            return;
        }

        if (_settingsJumpPending)
        {
            return;
        }

        _settingsJumpPending = true;
        Callable.From(() =>
        {
            _settingsJumpPending = false;
            var scroll = _settingsScroll;
            if (scroll == null || !anchor.IsInsideTree())
            {
                return;
            }

            // Position within the scrolled column, then clamped so the last section cannot ask for more
            // scroll than exists -- an unclamped assignment is silently pinned by Godot, which would
            // leave a jump to the final section looking like it stopped early.
            var target = anchor.Position.Y;
            var maximum = MathF.Max(0f, scroll.GetChild<Control>(0).Size.Y - scroll.Size.Y);
            scroll.ScrollVertical = (int)Math.Clamp(target, 0f, maximum);
        }).CallDeferred();
    }

    /// <summary>
    /// The theme tiles, marking the stored selection and applying a click straight away.
    /// </summary>
    /// <remarks>
    /// The theme saves immediately. Replacing its swatches leaves the rest of the form's controls,
    /// dirty flag and unfinished input intact.
    /// </remarks>
    private Control BuildThemePicker()
    {
        return _themePicker = UiFactory.ThemeSwatchPicker(OverlayThemeCatalog.All, SelectedTheme, ApplyThemeChoice);
    }

    /// <summary>
    /// Selects a theme, repaints the window in it immediately, and writes it to the settings file.
    /// </summary>
    /// <remarks>
    /// The choice is saved on the spot rather than waiting for the page's Save. It is the one setting
    /// here that gives immediate, whole-window feedback -- press a tile and the panel changes colour --
    /// so a player who likes what they see has no reason to look for a Save button, and one who closes
    /// the game without pressing it loses the choice. Reported live on 2026-09-21 as "the theme jumps
    /// back to the default after saving", which was the player discovering that the preview was never
    /// persisted at all.
    ///
    /// <see cref="RepaintForPalette"/> is what makes the change reach controls that already exist; a
    /// palette is read when a control is built, so switching it alone would leave every visible
    /// stylebox in the previous theme.
    /// </remarks>
    private void ApplyThemeChoice(string id)
    {
        var themeId = OverlayThemeCatalog.Normalize(id);
        if (!PersistThemeChoice(themeId)) return;
        _themeSelectedId = themeId;
        UiFactory.UseTheme(SelectedTheme);
        // From the host, not the panel: the chat footer is a sibling of the panel rather than a child
        // of it, so a walk rooted at the panel left its buttons painted in the previous palette --
        // observed live on 2026-09-21 as a grey-on-grey Hide button under the light theme.
        RepaintForPalette(_host);
        // Replace only the swatches. Rebuilding the whole form loses partial numeric input,
        // focus and scroll state, even if a typed settings draft is copied first.
        var previous = _themePicker;
        var parent = previous?.GetParent();
        if (parent != null && previous != null)
        {
            var index = previous.GetIndex();
            parent.RemoveChild(previous);
            previous.QueueFree();
            var replacement = BuildThemePicker();
            parent.AddChild(replacement);
            parent.MoveChild(replacement, index);
        }
    }

    /// <summary>
    /// Writes the chosen theme to the settings file, leaving every other field alone.
    /// </summary>
    /// <remarks>
    /// Copy persisted settings rather than harvesting the form. Unfinished endpoint, model and
    /// budget edits remain in their controls until the player saves the form.
    /// </remarks>
    private bool PersistThemeChoice(string themeId)
    {
        var settings = CloneSettings(AgentRuntime.Instance.Settings);
        settings.OverlayTheme = themeId;
        try
        {
            AgentRuntime.Instance.SaveSettings(settings);
            if (_saveStatus != null) _saveStatus.Text = Loc.T(_settingsDirty ? "未保存" : "已保存");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveStatus != null) _saveStatus.Text = FormatSettingsNotice();
            return false;
        }
    }

    /// <summary>
    /// Re-applies the active palette to controls that were built under the previous one.
    /// </summary>
    /// <remarks>
    /// Walks the built tree rather than tracking a list of controls: every control already knows which
    /// kind it is, and a registry would be a second thing to keep in step with the builders. Labels
    /// take the theme's text or muted colour according to how they were created, which is the only
    /// property that tells a heading from a hint.
    ///
    /// The tree walk is necessary because a palette is read when a control is built. Switching it
    /// alone would leave every visible stylebox in the previous theme, which is what made the first
    /// version of the live switch look like it did nothing.
    /// </remarks>
    private static void RepaintForPalette(Node? root)
    {
        if (root == null)
        {
            return;
        }

        foreach (var child in root.GetChildren())
        {
            // The derived kinds are matched before Button: CheckBox and OptionButton both inherit from
            // it, so a Button arm written first would swallow them and repaint them as plain buttons.
            switch (child)
            {
                case Godot.CheckBox check:
                    UiFactory.ReapplyCheckTheme(check);
                    break;
                case Godot.OptionButton combo:
                    UiFactory.ReapplyComboTheme(combo);
                    break;
                case Godot.LineEdit line:
                    UiFactory.ReapplyLineTheme(line);
                    break;
                case Godot.TextEdit multiline:
                    UiFactory.ReapplyMultilineTheme(multiline);
                    break;
                case Godot.Button button:
                    UiFactory.ReapplyButtonTheme(button);
                    break;
                case Godot.Label label:
                    UiFactory.ReapplyLabelTheme(label);
                    break;
                case Godot.RichTextLabel rich:
                    UiFactory.ReapplyRichTheme(rich);
                    break;
                case Godot.Panel panel:
                    UiFactory.ReapplyPanelTheme(panel);
                    break;
                case Godot.PanelContainer container:
                    UiFactory.ReapplySurfaceTheme(container);
                    break;
            }

            RepaintForPalette(child);
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
        _settingsAnchors.Clear();
        var settings = CloneSettings(AgentRuntime.Instance.Settings);
        var firstRun = FirstRunSetup.Evaluate(settings);

        // Not an anchored section: it is the form's own state line, and a jump button that scrolls to
        // "unsaved" would be a button whose only job is to tell the player the form is dirty.
        _saveStatus = UiFactory.Label(_settingsDirty ? Loc.T("未保存") : Loc.T("已保存"), 12, muted: true);
        _settingsBody.AddChild(_saveStatus);
        _settingsLoadNotice = UiFactory.Wrapped(FormatSettingsNotice(), 12);
        _settingsBody.AddChild(_settingsLoadNotice);
        AddSettingsSection("first-run", Loc.T("首次配置"));
        _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("四步上手：① 在「端点」填接口地址和 Key（本地 Ollama / LM Studio 可留空 Key）→ ② 在「模型」添加模型并绑定端点 → ③ 点模型卡片上的「测试」做一次真实调用（含工具调用检测），通过会显示 ✅ → ④ 在「模型绑定」选主模型并保存。然后切到「游玩」页点「开始自动游玩」。想玩双人：游玩页顶部切到「多人」，邀请 AI 队友。")));
        _testNotice = UiFactory.Wrapped(Loc.T("「测试」是对该模型的真实调用：先连通，再验证它会不会调用工具——不会调用工具的模型无法自动游玩。页脚的「测试连接」测的是当前主模型。"), 12);
        _settingsBody.AddChild(_testNotice);
        _conversationTest = UiFactory.Wrapped(ModelRoleProbe.FormatLine(firstRun.Conversation), 12);
        _playTest = UiFactory.Wrapped(ModelRoleProbe.FormatLine(firstRun.Play), 12);
        _visionTest = UiFactory.Wrapped(ModelRoleProbe.FormatLine(firstRun.Vision), 12);
        _settingsBody.AddChild(_conversationTest);
        _settingsBody.AddChild(_playTest);
        _settingsBody.AddChild(_visionTest);
        if (firstRun.Play.Status == "failed" && !string.IsNullOrWhiteSpace(firstRun.Play.NextStep))
        {
            _settingsBody.AddChild(UiFactory.Label(Loc.T("下一步：{0}", firstRun.Play.NextStep), 12));
        }

        _deleteWarning = UiFactory.Label("", 12);
        _settingsBody.AddChild(_deleteWarning);

        // Appearance sits above the endpoint forms on purpose: it is the one setting on this page a
        // player changes for fun rather than to make the agent work, and it should not be behind the
        // advanced toggle that hides the budget knobs.
        //
        // Drawn as swatches rather than a drop-down: "Crimson" and "Emerald" are words, and the thing
        // being chosen is a colour scheme. The tiles are rebuilt on click rather than restyled, which
        // is why the selection lives in a field -- the control that was clicked is freed by the
        // rebuild that follows it.
        // Seeded once, from the file. A rebuild must not overwrite the player's own selection with the
        // last persisted value: the theme change reaches the runtime through a save that rebuilds this
        // page, so a rebuild that re-reads it can write the default back over the choice it just
        // handled. See the field's remarks.
        _themeSelectedId ??= OverlayThemeCatalog.Normalize(settings.OverlayTheme);
        AddSettingsSection("appearance", Loc.T("外观"));
        var appearanceCard = UiFactory.Card(
            Loc.T("外观"),
            UiFactory.Label(Loc.T("界面主题"), 13),
            BuildThemePicker(),
            UiFactory.Label(Loc.T("只改这个窗口的配色，保存后立即生效。"), UiFactory.FontCaption, muted: true));
        _settingsBody.AddChild(appearanceCard);

        AddSettingsSection("endpoints", Loc.T("端点"));
        for (var i = 0; i < settings.Endpoints.Count; i++)
        {
            _settingsBody.AddChild(BuildEndpointCard(settings.Endpoints[i], i));
        }

        AddSettingsSection("models", Loc.T("模型"));
        for (var i = 0; i < settings.Models.Count; i++)
        {
            _settingsBody.AddChild(BuildModelCard(settings.Models[i], i, settings));
        }

        AddSettingsSection("bindings", Loc.T("模型绑定"));
        _conversationCombo = FillModelCombo(settings, settings.ConversationModelId, includeEmpty: false);
        _settingsBody.AddChild(Labeled(Loc.T("主模型（对话与游玩）"), _conversationCombo));
        WatchCombo(_conversationCombo);
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
            _visionCombo = FillModelCombo(settings, settings.VisionModelId, includeEmpty: true);
            _settingsBody.AddChild(Labeled(Loc.T("外挂视觉模型（可空）"), _visionCombo));
            WatchCombo(_visionCombo);
            _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("视觉可选。不勾选「视觉」、不配外挂视觉时，仍用 compact 状态与工具打完全部内容。"), 11));
            _hotkeyEdit = UiFactory.Line(settings.Hotkey, "F8");
            WatchLine(_hotkeyEdit);
            _settingsBody.AddChild(Labeled(Loc.T("开关热键"), _hotkeyEdit));
            _maxTokensEdit = UiFactory.Line(settings.MaxSessionTokens?.ToString() ?? "", Loc.T("不限（留空或0）"));
            _maxRequestsEdit = UiFactory.Line(settings.MaxSessionRequests?.ToString() ?? "", Loc.T("不限（留空或0）"));
            WatchLine(_maxTokensEdit);
            WatchLine(_maxRequestsEdit);
            _settingsBody.AddChild(Labeled(Loc.T("会话 Token 上限"), _maxTokensEdit));
            _settingsBody.AddChild(Labeled(Loc.T("会话请求上限"), _maxRequestsEdit));
            _llmTimeoutEdit = UiFactory.Line(settings.LlmRequestTimeoutSeconds?.ToString() ?? "", Loc.T("默认 600 秒"));
            WatchLine(_llmTimeoutEdit);
            _settingsBody.AddChild(Labeled(Loc.T("单次请求超时（秒）"), _llmTimeoutEdit));
            _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("服务商长时间不应答时按此时长报错，而不是一直转圈。"), 11));
            _budgetHint = UiFactory.Wrapped(_budgetInputError ?? Loc.T("非法预算输入会保留原来的安全上限，不会静默变成不限。"), 11);
            _settingsBody.AddChild(_budgetHint);
            _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("预算护栏：达到上限时优雅停止自动游玩并提示，避免意外耗尽额度。"), 11));
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
            _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("默认关闭。开启后仅在战斗开始与结束时各说一句，每次开始自动游玩最多 6 句，两句之间至少间隔 75 秒（暂停或继续自动游玩不会缩短这个间隔）；不会代打，也遵守预算上限。"), 11));
            _settingsBody.AddChild(UiFactory.Button(Loc.T("重置窗口位置"), ResetPlacement));
            _settingsBody.AddChild(UiFactory.Label(Loc.T("拖动标题栏可移动窗口，位置会保存。"), 11, muted: true));
            // Wrapped: a Windows profile path is one long word wider than the panel.
            _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("配置文件：{0}", AgentRuntime.Instance.SettingsPath), 11));
        }
        else
        {
            _visionCombo = null;
            _hotkeyEdit = null;
            _maxTokensEdit = null;
            _maxRequestsEdit = null;
            _llmTimeoutEdit = null;
            _budgetHint = null;
            _settingsResetStatsButton = null;
            _proactiveChatToggle = null;
            _proactiveToneCombo = null;
        }

        AddSettingsSection("jev", Loc.T("Jev 执行层"));
        _settingsBody.AddChild(BuildJevSection(settings));

        AddSettingsSection("connect", Loc.T("接入"));
        _settingsBody.AddChild(BuildConnectSection());
        }
        finally
        {
            _rebuildingSettings = false;
        }
    }

    /// <summary>
    /// The Jev execution-model section: base URL, API key, model and the confidence threshold, plus a
    /// connection test. These feed the dual-layer decision mode toggled from the play page.
    /// </summary>
    private Control BuildJevSection(AgentSettings settings)
    {
        var column = UiFactory.Column();
        column.AddChild(UiFactory.Wrapped(Loc.T("双层决策模式由 Jev（TypeSafe System One 模型）逐步操作游戏，LLM 只做战略规划。在游玩页按模式开启。"), UiFactory.FontCaption));

        _jevBaseUrlEdit = UiFactory.Line(settings.JevBaseUrl, "https://api.typesafe.ai");
        _jevApiKeyEdit = UiFactory.Line(settings.JevApiKey, "API Key", secret: true);
        _jevModelEdit = UiFactory.Line(settings.JevModel, "jev-latest");
        _jevThresholdEdit = UiFactory.Line(settings.JevConfidenceThreshold.ToString("0.00"), "0.35");
        _jevTimeoutEdit = UiFactory.Line(settings.JevRequestTimeoutSeconds?.ToString() ?? "", Loc.T("默认 90 秒"));
        WatchLine(_jevBaseUrlEdit);
        WatchLine(_jevApiKeyEdit);
        WatchLine(_jevModelEdit);
        WatchLine(_jevThresholdEdit);
        WatchLine(_jevTimeoutEdit);
        column.AddChild(Labeled(Loc.T("Base URL"), _jevBaseUrlEdit));
        column.AddChild(Labeled(Loc.T("API Key"), _jevApiKeyEdit));
        column.AddChild(Labeled(Loc.T("模型"), _jevModelEdit));
        column.AddChild(Labeled(Loc.T("置信度阈值（0-1，低于则回退 LLM）"), _jevThresholdEdit));
        column.AddChild(Labeled(Loc.T("单次请求超时（秒）"), _jevTimeoutEdit));

        _jevTestButton = UiFactory.Button(Loc.T("测试 Jev 连接"), () => _ = TestJevConnectionAsync(), UiFactory.ButtonKind.Ghost);
        _jevTestStatus = UiFactory.Wrapped("", UiFactory.FontCaption);
        column.AddChild(UiFactory.Row(_jevTestButton));
        column.AddChild(_jevTestStatus);
        return UiFactory.Card(Loc.T("Jev 执行层"), column);
    }

    private async Task TestJevConnectionAsync()
    {
        var before = AgentRuntime.Instance.Settings;
        SaveSettingsFromUi();
        if (!ReferenceEquals(before, AgentRuntime.Instance.Settings))
            await SaveSettingsAndSyncJevAsync();
        await GameThread.InvokeAsync(() =>
        {
            if (_jevTestStatus != null && _jevTestStatus.IsInsideTree()) _jevTestStatus.Text = Loc.T("正在测试…");
        });
        var result = await AgentRuntime.Instance.TestJevConnectionAsync(CancellationToken.None);
        await GameThread.InvokeAsync(() =>
        {
            if (_jevTestStatus != null && _jevTestStatus.IsInsideTree()) _jevTestStatus.Text = result;
        });
    }

    /// <summary>
    /// The connect section: the MCP service switch and the client configuration to paste into an
    /// external client. Moved into settings from its own tab; the settings page's scroll carries it.
    /// </summary>
    private Control BuildConnectSection()
    {
        var page = UiFactory.Column();
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
        return page;
    }

    private Control BuildEndpointCard(LlmEndpoint endpoint, int index)
    {
        var box = new PanelContainer();
        UiFactory.TagSurface(box, UiFactory.SurfaceRole.ChromeRaised, 6);
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
        UiFactory.TagSurface(box, UiFactory.SurfaceRole.ChromeRaised, 6);
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
        WatchCheck(vision);
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
        var contextWindow = UiFactory.Line(model.ContextWindow?.ToString() ?? "", Loc.T("默认 256000"));
        WatchLine(contextWindow);
        var remove = UiFactory.Button(Loc.T("删除"), () => RemoveModel(index));

        // The per-model test: a real ping plus a real tool-calling probe, and the badge beside it is
        // the recorded verdict for exactly this endpoint+model+key. The badge lives on the card
        // because "which model is proven" is the question the player asks while looking at the card,
        // not while reading a summary paragraph at the top of the page.
        var testButton = UiFactory.Button(Loc.T("测试"), () => _ = TestModelFromUiAsync(model.Id), UiFactory.ButtonKind.Ghost);
        var badge = UiFactory.Wrapped(ModelTestBadgeText(model.Id), UiFactory.FontCaption);
        column.AddChild(UiFactory.Row(display, remove));
        column.AddChild(UiFactory.Row(modelName, endpointCombo));
        column.AddChild(UiFactory.Row(testButton));
        column.AddChild(badge);
        if (_showAdvancedValue)
        {
            column.AddChild(UiFactory.Row(vision));
            column.AddChild(Labeled(Loc.T("思考方式"), thinkingMode));
            column.AddChild(Labeled(Loc.T("思考强度"), thinkingIntensity));
            column.AddChild(Labeled(Loc.T("上下文窗口"), contextWindow));
        }
        box.AddChild(column);
        _modelEditors.Add(new ModelEditors(model.Id, display, modelName, endpointCombo, vision, thinkingMode, thinkingIntensity, contextWindow));
        return box;
    }

    /// <summary>The badge line under a model card: the recorded verdict, or the nudge to test.</summary>
    private string ModelTestBadgeText(string modelId)
    {
        var record = AgentRuntime.Instance.ModelTestFor(modelId);
        if (record == null)
        {
            return Loc.T("⚪ 未测试。点「测试」做一次真实调用（含工具调用检测）。");
        }

        if (!AgentRuntime.Instance.IsModelVerified(modelId))
        {
            return record.Status switch
            {
                "verified" => Loc.T("⚪ 配置已修改，需重新测试。"),
                // A failed test must not look like "never tested": the player just pressed the
                // button and needs the reason on the card, not in a line the rebuild erases.
                "failed" => Loc.T("⚠️ 测试失败（{0}）：{1}", ShortTime(record.TestedAt), record.Error ?? ""),
                _ => Loc.T("⚪ 未测试。点「测试」做一次真实调用（含工具调用检测）。")
            };
        }

        return record.Tools == "supported"
            ? Loc.T("✅ 已验证 · 工具调用可用（{0}）", ShortTime(record.TestedAt))
            : Loc.T("⚠️ 已连通，但工具调用不可用——该模型无法自动游玩（{0}）", ShortTime(record.TestedAt));
    }

    private static string ShortTime(string? iso)
    {
        return DateTimeOffset.TryParse(iso, out var at) ? at.ToLocalTime().ToString("HH:mm") : "";
    }

    private async Task TestModelFromUiAsync(string modelId)
    {
        // Harvest first so the test runs against what the player sees, not the last saved copy.
        SaveSettingsFromUi();
        SetSaveStatus(Loc.T("正在测试模型…"));
        var result = await AgentRuntime.Instance.TestModelAsync(modelId, CancellationToken.None);
        // Rebuild first: RebuildSettingsForm recreates _saveStatus, so a status written before it
        // was discarded and the player never saw the test verdict.
        RebuildSettingsForm();
        SetSaveStatus(result);
    }

    /// <summary>The settings page's state line: reused for the per-model test's progress.</summary>
    private void SetSaveStatus(string text)
    {
        if (_saveStatus != null)
        {
            _saveStatus.Text = text;
        }
    }

    /// <summary>
    /// Reads every editor on the settings form into a fresh settings clone. Lives beside the form
    /// that builds those editors: a field added to the form without a harvest line here is a setting
    /// that silently never saves, and the two belong in one file so that mistake is visible.
    /// </summary>
    private AgentSettings HarvestSettings()
    {
        var current = CloneSettings(AgentRuntime.Instance.Settings);
        for (var i = 0; i < Math.Min(current.Endpoints.Count, _endpointEditors.Count); i++)
        {
            var editor = _endpointEditors[i];
            var endpoint = current.Endpoints[i];
            endpoint.Name = editor.Name.Text.Trim();
            endpoint.BaseUrl = editor.Url.Text.Trim();
            endpoint.ApiKey = editor.Key.Text;
            endpoint.Enabled = editor.Enabled.ButtonPressed;
        }

        for (var i = 0; i < Math.Min(current.Models.Count, _modelEditors.Count); i++)
        {
            var editor = _modelEditors[i];
            var model = current.Models[i];
            model.DisplayName = editor.Display.Text.Trim();
            model.Model = editor.ModelName.Text.Trim();
            model.SupportsVision = editor.Vision.ButtonPressed;
            // SupportsTools is not harvested from a checkbox anymore: the per-model test's
            // tool-calling probe is the authority, and a hand tick could claim a capability the
            // provider does not have.
            model.ThinkingMode = SelectedText(editor.ThinkingMode);
            model.ThinkingIntensity = SelectedText(editor.ThinkingIntensity);
            model.ContextWindow = int.TryParse(editor.ContextWindow.Text.Trim(), out var contextWindow) && contextWindow > 0
                ? contextWindow
                : null;
            if (editor.Endpoint.Selected >= 0)
            {
                model.EndpointId = editor.Endpoint.GetItemMetadata(editor.Endpoint.Selected).AsString();
            }
        }

        if (_conversationCombo != null)
        {
            current.ConversationModelId = EmptyToNull(SelectedMetadata(_conversationCombo));
        }

        // One main model plays and chats: the play-role combo is gone from the page, and the stored
        // PlayModelId is cleared so the runtime's fallback (play = conversation) is the only rule.
        current.PlayModelId = null;

        if (_visionCombo != null)
        {
            current.VisionModelId = EmptyToNull(SelectedMetadata(_visionCombo));
        }
        current.ThinkingIntensity = current.FindModel(current.ConversationModelId)?.ThinkingIntensity
            ?? current.ThinkingIntensity;
        if (_hotkeyEdit != null)
        {
            current.Hotkey = _hotkeyEdit.Text.Trim() is { Length: > 0 } hotkey ? hotkey : "F8";
        }

        if (_proactiveChatToggle != null)
        {
            current.ProactiveChatEnabled = _proactiveChatToggle.ButtonPressed;
        }

        if (_companionChoiceToggle != null)
        {
            current.CompanionAutoSelectCharacter = !_companionChoiceToggle.ButtonPressed;
        }

        if (_proactiveToneCombo != null)
        {
            current.ProactiveChatTone = ProactiveChatTones.Normalize(SelectedMetadata(_proactiveToneCombo));
        }

        // The swatch grid keeps the selection in a field rather than in a control: the tile that was
        // clicked is freed by the rebuild that follows it. Normalised on the way in, so an id this
        // build does not know cannot be written back out and leave the next start choosing between a
        // stored value and a drawn one.
        current.OverlayTheme = OverlayThemeCatalog.Normalize(SelectedTheme);

        current.AttachStateInChat = _attachState?.ButtonPressed ?? true;
        current.AttachScreenshotInChat = _attachShot?.ButtonPressed ?? false;
        current.McpEnabled = _mcpToggle?.ButtonPressed ?? current.McpEnabled;
        _budgetInputError = null;
        if (_maxTokensEdit != null || _maxRequestsEdit != null)
        {
            if (!SessionBudgetLimits.TryHarvestBudget(
                    current,
                    _maxTokensEdit?.Text,
                    _maxRequestsEdit?.Text,
                    out var budgetError))
            {
                _budgetInputError = budgetError;
            }
        }

        if (_llmTimeoutEdit != null)
        {
            current.LlmRequestTimeoutSeconds =
                int.TryParse(_llmTimeoutEdit.Text.Trim(), out var seconds) && seconds > 0
                    ? seconds
                    : null;
        }

        ModelRoleProbe.InvalidateMismatched(current);

        if (_jevBaseUrlEdit != null)
        {
            current.JevBaseUrl = _jevBaseUrlEdit.Text.Trim();
        }

        if (_jevApiKeyEdit != null)
        {
            current.JevApiKey = _jevApiKeyEdit.Text.Trim();
        }

        if (_jevModelEdit != null)
        {
            current.JevModel = _jevModelEdit.Text.Trim();
        }

        if (_jevThresholdEdit != null &&
            double.TryParse(_jevThresholdEdit.Text.Trim(), out var threshold))
        {
            current.JevConfidenceThreshold = threshold;
        }

        if (_jevTimeoutEdit != null)
        {
            current.JevRequestTimeoutSeconds =
                int.TryParse(_jevTimeoutEdit.Text.Trim(), out var jevSeconds) && jevSeconds > 0
                    ? jevSeconds
                    : null;
        }

        return current;
    }

    private static AgentSettings CloneSettings(AgentSettings source)
    {
        // The copy lives in Config so the executable test project can cover it.
        return SettingsClone.Clone(source);
    }

    private static Control Labeled(string label, Control child)
    {
        var row = new HBoxContainer();
        var text = UiFactory.Wrapped(label, 13);
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
