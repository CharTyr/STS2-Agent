using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
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
        ("bindings", Loc.T("角色绑定"))
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
    /// A click marks the form dirty as well as repainting: the appearance row shares this page's Save
    /// with the endpoint and model forms, and a theme that saved itself would be the one setting here
    /// that does not answer to it.
    /// </remarks>
    private Control BuildThemePicker()
    {
        return UiFactory.ThemeSwatchPicker(OverlayThemeCatalog.All, SelectedTheme, ApplyThemeChoice);
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
        _themeSelectedId = OverlayThemeCatalog.Normalize(id);
        UiFactory.UseTheme(SelectedTheme);
        // From the host, not the panel: the chat footer is a sibling of the panel rather than a child
        // of it, so a walk rooted at the panel left its buttons painted in the previous palette --
        // observed live on 2026-09-21 as a grey-on-grey Hide button under the light theme.
        RepaintForPalette(_host);
        PersistThemeChoice(SelectedTheme);
        RebuildSettingsForm();
    }

    /// <summary>
    /// Writes the chosen theme to the settings file, leaving every other field alone.
    /// </summary>
    /// <remarks>
    /// Harvests the form rather than copying the stored settings, so an unsaved edit elsewhere on the
    /// page is carried along instead of being silently reverted; the form stays marked dirty, so the
    /// page's Save is still what commits the rest.
    /// </remarks>
    private void PersistThemeChoice(string themeId)
    {
        var settings = HarvestSettings();
        settings.OverlayTheme = OverlayThemeCatalog.Normalize(themeId);
        AgentRuntime.Instance.SaveSettings(settings);
        _settingsDirty = true;
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
        _settingsBody.AddChild(UiFactory.Wrapped(Loc.T("添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试连接 → 保存。通过后再去「AI 队友」从主菜单邀请。默认网址和模型名不算已经可用。")));
        _testNotice = UiFactory.Wrapped(Loc.T("测试连接会向配置的服务发送测试请求。对话通过不等于游玩已通过。本地服务可以留空 API Key。"), 12);
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

        AddSettingsSection("bindings", Loc.T("角色绑定"));
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
