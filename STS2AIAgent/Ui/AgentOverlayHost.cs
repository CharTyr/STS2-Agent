using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;
using STS2AIAgent.Server;
using STS2AIAgent.Vision;

namespace STS2AIAgent.Ui;

internal sealed partial class AgentOverlayHost
{
    private const string LogPrefix = "[STS2AIAgent.Overlay]";
    private const int PanelWidth = 440;

    private static AgentOverlayHost? _instance;

    private CanvasLayer? _layer;
    private Control? _host;
    private Control? _panel;
    private PanelContainer? _dragHandle;
    private Button? _edgeTab;
    private SceneTree? _tree;
    private bool _hotkeyWasDown;
    private bool _dragging;
    private Vector2 _lastViewportSize;
    private ulong _lastRefreshMs;

    private RichTextLabel? _chatLog;
    private TextEdit? _chatInput;
    private CheckBox? _attachState;
    private CheckBox? _attachShot;
    private CheckBox? _allowAct;
    private Label? _playStatus;
    private Label? _playSummary;
    private Label? _playScreen;
    private Label? _playAction;
    private Label? _playThought;
    private Label? _playUsage;
    private LineEdit? _maxTokensEdit;
    private LineEdit? _maxRequestsEdit;
    private CheckBox? _proactiveChatToggle;
    private OptionButton? _proactiveToneCombo;

    /// <summary>The header's live state tag and the connection line under it.</summary>
    private Label? _headerStatus;
    private Label? _apiLabel;
    private Label? _dualStatus;
    private Label? _teammateLive;
    private Label? _sessionHeadline;
    private Label? _sessionDetail;
    private Label? _sessionNext;
    private Button? _dualLaunchButton;
    private Button? _dualContinueButton;
    private bool _continueLaunching;
    private CheckBox? _companionChoiceToggle;
    private Label? _dualHint;
    private RichTextLabel? _teamChat;
    private TextEdit? _teamInput;
    private Button? _teamSend;
    private Label? _teamStatus;
    private Label? _teamControlStatus;
    private Button? _teamPause;
    private Button? _teamResume;
    private Button? _playToggle;
    private Button? _stepButton;
    private Button? _sendButton;
    private CheckBox? _mcpToggle;
    private Label? _mcpStatus;
    private Label? _mcpUrlLabel;
    private TextEdit? _mcpConfigEdit;
    private Control? _mcpInfoBox;
    private Control? _pageHost;
    /// <summary>Diagnostic state: the content column to measure, and the page already reported.</summary>
    private Control? _contentColumn;
    private string? _overflowReportedFor;
    private VBoxContainer? _settingsBody;
    private int _buildAttempts;
    private bool _captureHidden;
    private bool _languageHooked;

    private readonly List<EndpointEditors> _endpointEditors = new();
    private readonly List<ModelEditors> _modelEditors = new();
    private OptionButton? _conversationCombo;
    private OptionButton? _playCombo;
    private OptionButton? _visionCombo;
    private LineEdit? _hotkeyEdit;
    private Label? _saveStatus;
    private Label? _testNotice;
    private Label? _conversationTest;
    private Label? _playTest;
    private Label? _visionTest;
    private Label? _deleteWarning;
    private CheckBox? _showAdvanced;
    private bool _settingsDirty;
    private bool _rebuildingSettings;
    private bool _showAdvancedValue;
    private string? _budgetInputError;
    private Label? _budgetHint;
    private Label? _settingsLoadNotice;
    private Label? _sessionConfigNotice;
    private Button? _resetStatsButton;
    private Button? _settingsResetStatsButton;

    public static void Install()
    {
        if (_instance != null)
        {
            return;
        }

        _instance = new AgentOverlayHost();
        AgentRuntime.Instance.Changed += _instance.OnRuntimeChanged;
        ScreenshotService.BeginCapture = HideForCapture;
        ScreenshotService.EndCapture = RestoreAfterCapture;
        _instance.TryBuildOrRetry();
    }

    public static void Uninstall()
    {
        if (_instance == null)
        {
            return;
        }

        ScreenshotService.BeginCapture = null;
        ScreenshotService.EndCapture = null;
        AgentRuntime.Instance.Changed -= _instance.OnRuntimeChanged;
        _instance.TearDown();
        _instance = null;
    }

    private static void HideForCapture()
    {
        if (_instance?._panel == null || !_instance._panel.Visible)
        {
            return;
        }

        _instance._captureHidden = true;
        _instance._panel.Visible = false;
        if (_instance._edgeTab != null)
        {
            _instance._edgeTab.Visible = false;
        }
    }

    private static void RestoreAfterCapture()
    {
        if (_instance == null || !_instance._captureHidden)
        {
            return;
        }

        _instance._captureHidden = false;
        if (_instance._panel != null)
        {
            _instance._panel.Visible = true;
        }

        if (_instance._edgeTab != null)
        {
            _instance._edgeTab.Visible = true;
        }
    }

    private void TryBuildOrRetry()
    {
        if (_layer != null)
        {
            return;
        }

        if (NGame.Instance != null)
        {
            Build();
            return;
        }

        if (_buildAttempts++ > 180)
        {
            Log.Warn($"{LogPrefix} NGame never became ready; overlay unavailable this session");
            return;
        }

        Callable.From(TryBuildOrRetry).CallDeferred();
    }

    private void Build()
    {
        var game = NGame.Instance;
        if (game == null)
        {
            Log.Warn($"{LogPrefix} NGame is not ready; overlay retry scheduled");
            TryBuildOrRetry();
            return;
        }

        // The palette has to be selected before the first control is built: every stylebox below
        // reads it, and a control keeps the colours it was created with.
        var startupSettings = AgentRuntime.Instance.Settings;
        UiFactory.UseTheme(startupSettings.OverlayTheme);

        _tree = game.GetTree();
        var root = _tree.Root;
        _layer = new CanvasLayer
        {
            Name = "STS2AIAgentOverlay",
            Layer = 128,
            ProcessMode = Node.ProcessModeEnum.Always
        };

        var host = new Control
        {
            Name = "OverlayHost",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _host = host;

        _edgeTab = UiFactory.Button("AI", ToggleVisible);
        _edgeTab.CustomMinimumSize = new Vector2(36, 72);
        _edgeTab.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _edgeTab.Position = new Vector2(-80, 0);

        _panel = new Control
        {
            Name = "AgentPanel",
            MouseFilter = Control.MouseFilterEnum.Stop,
            ClipContents = true
        };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.CustomMinimumSize = new Vector2(PanelWidth, 360);

        var chrome = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        chrome.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        UiFactory.TagSurface(chrome, UiFactory.SurfaceRole.Chrome, 10);

        var layout = UiFactory.Column();
        _contentColumn = layout;
        layout.AddChild(BuildHeader());
        layout.AddChild(BuildTabs());

        _pageHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        BuildPages(_pageHost);
        layout.AddChild(_pageHost);
        chrome.AddChild(layout);
        _panel.AddChild(chrome);
        _panel.Visible = startupSettings.OverlayVisibleOnStart || !startupSettings.HasSeenFirstRunGuide;

        host.AddChild(_edgeTab);
        host.AddChild(_panel);
        _layer.AddChild(host);
        _layer.TreeEntered += OnOverlayEnteredTree;
        root.CallDeferred(Node.MethodName.AddChild, _layer);
        _tree.ProcessFrame += OnProcessFrame;
        // Land on the tab that gets the player playing: settings when nothing is configured yet,
        // otherwise the play page (which hosts both the solo and the multiplayer modes). The
        // companion instance has no overlay (ModEntry skips it), so there is no companion branch here.
        if (!startupSettings.HasSeenFirstRunGuide)
        {
            ShowTab("settings");
        }
        else
        {
            ShowTab("play");
        }
        RefreshDynamic();
        var languageBefore = Loc.Current;
        LocSource.Initialize();
        HookLanguageChanges();
        if (Loc.Current != languageBefore)
        {
            // The first localization pass can switch the language after this overlay's text was
            // built; rebuild once we are safely out of this method.
            Callable.From(OnLanguageChanged).CallDeferred();
        }

        Log.Info($"{LogPrefix} Attach scheduled");
    }

    private void OnOverlayEnteredTree()
    {
        if (_layer != null)
        {
            _layer.TreeEntered -= OnOverlayEnteredTree;
        }

        Log.Info($"{LogPrefix} Installed");
        Callable.From(() => ApplyPlacement()).CallDeferred();
    }

    private Control BuildHeader()
    {
        _dragHandle = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Move,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        UiFactory.TagSurface(_dragHandle, UiFactory.SurfaceRole.ChromeRaised, 8);
        _dragHandle.GuiInput += OnDragHandleGuiInput;
        // No wrapping on either of these: the title is a fixed string and the status is a chip, and a
        // tight row is exactly where WordSmart would break them one character per line. Observed live
        // on 2026-09-21 -- the title rendered as a vertical column of single letters.
        var title = UiFactory.Label("STS2 AI Agent", UiFactory.FontTitle, wrap: false);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.AddThemeColorOverride("font_color", UiFactory.Accent);

        // The live state tag: the answer to "is it playing" is in the title bar, where the player
        // already looks to find the window, instead of one tab away.
        _headerStatus = UiFactory.Label(Loc.T("○ 待机"), UiFactory.FontCaption, muted: true, wrap: false);
        _headerStatus.MouseFilter = Control.MouseFilterEnum.Ignore;
        _headerStatus.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var titleRow = UiFactory.TightRow(title, _headerStatus);
        titleRow.MouseFilter = Control.MouseFilterEnum.Ignore;

        var hint = UiFactory.Label(Loc.T("拖动移动"), UiFactory.FontCaption, muted: true, wrap: false);
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        var handleColumn = UiFactory.Column();
        handleColumn.MouseFilter = Control.MouseFilterEnum.Ignore;
        handleColumn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        handleColumn.AddThemeConstantOverride("separation", 2);
        handleColumn.AddChild(titleRow);
        handleColumn.AddChild(hint);
        _dragHandle.AddChild(handleColumn);

        var hide = UiFactory.Button(Loc.T("隐藏"), ToggleVisible, UiFactory.ButtonKind.Ghost);
        hide.CustomMinimumSize = new Vector2(56, 0);
        hide.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

        // The connection line doubles as the header's status chip: the colour says whether the local
        // API is reachable before the player has read a word of it.
        _apiLabel = UiFactory.Label("", UiFactory.FontCaption, muted: true);
        var column = UiFactory.Column();
        column.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiFactory.SpaceSm);
        row.AddChild(_dragHandle);
        row.AddChild(hide);
        column.AddChild(row);
        column.AddChild(_apiLabel);
        return column;
    }

    private void AddEndpoint()
    {
        var settings = HarvestSettings();
        settings.Endpoints.Add(new LlmEndpoint { Name = Loc.T("新端点") });
        PersistHarvested(settings);
    }

    private void AddModel()
    {
        var settings = HarvestSettings();
        settings.Models.Add(new LlmModelConfig
        {
            EndpointId = settings.Endpoints.FirstOrDefault()?.Id ?? string.Empty,
            Model = "gpt-4o",
            DisplayName = Loc.T("新模型"),
            ThinkingIntensity = "medium"
        });
        PersistHarvested(settings);
    }

    private void RemoveEndpoint(int index)
    {
        var settings = HarvestSettings();
        if (index < 0 || index >= settings.Endpoints.Count)
        {
            return;
        }

        var impact = SettingsBinding.EndpointRemoval(settings, settings.Endpoints[index].Id);
        if (impact.Blocked)
        {
            ShowDeleteWarning(impact.Message);
            return;
        }

        settings.Endpoints.RemoveAt(index);
        ModelRoleProbe.InvalidateMismatched(settings);
        PersistHarvested(settings);
    }

    private void RemoveModel(int index)
    {
        var settings = HarvestSettings();
        if (index < 0 || index >= settings.Models.Count)
        {
            return;
        }

        var impact = SettingsBinding.ModelRemoval(settings, settings.Models[index].Id);
        if (impact.Blocked)
        {
            ShowDeleteWarning(impact.Message);
            return;
        }

        settings.Models.RemoveAt(index);
        ModelRoleProbe.InvalidateMismatched(settings);
        PersistHarvested(settings);
    }

    private void ShowDeleteWarning(string message)
    {
        if (_deleteWarning != null)
        {
            _deleteWarning.Text = message;
        }
    }

    private void SaveSettingsFromUi()
    {
        var settings = HarvestSettings();
        ModelRoleProbe.InvalidateMismatched(settings);
        var saveText = string.IsNullOrWhiteSpace(_budgetInputError)
            ? Loc.T("已保存")
            : Loc.T("已保存（预算未改：{0}）", _budgetInputError);
        if (!PersistHarvested(settings))
        {
            return;
        }

        if (_saveStatus != null)
        {
            _saveStatus.Text = saveText;
        }

        if (_budgetHint != null && !string.IsNullOrWhiteSpace(_budgetInputError))
        {
            _budgetHint.Text = _budgetInputError;
        }
    }

    private bool PersistHarvested(AgentSettings settings)
    {
        try
        {
            var themeChanged = !string.Equals(
                OverlayThemeCatalog.Normalize(settings.OverlayTheme),
                UiFactory.Palette.Id,
                StringComparison.Ordinal);
            AgentRuntime.Instance.SaveSettings(settings);
            _settingsDirty = false;
            RebuildSettingsForm();
            if (themeChanged)
            {
                // The palette is baked into every stylebox at build time, so a saved theme needs a
                // rebuild rather than a repaint. Same path a language change takes, so the window
                // keeps its position, its visibility and the tab the player is on.
                RebuildInPlace();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveStatus != null)
            {
                _saveStatus.Text = FormatSettingsNotice(fallback: Loc.T("保存失败，原配置未被覆盖。"));
            }

            return false;
        }
    }

    private void ResetSessionStatsFromUi()
    {
        AgentRuntime.Instance.TryResetSessionStats(out _);
        RefreshDynamic();
    }

    private static string FormatSettingsNotice(string? fallback = null)
    {
        var notice = AgentRuntime.Instance.SettingsNotice;
        if (notice.HasMessage)
        {
            var text = notice.Message;
            if (!string.IsNullOrWhiteSpace(notice.BackupPath))
            {
                text += Loc.T(" 备份：{0}", notice.BackupPath);
            }

            return text;
        }

        return fallback ?? "";
    }

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
            model.SupportsTools = editor.Tools.ButtonPressed;
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

        if (_playCombo != null)
        {
            current.PlayModelId = EmptyToNull(SelectedMetadata(_playCombo));
        }

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

        return current;
    }

    private static AgentSettings CloneSettings(AgentSettings source)
    {
        // The copy lives in Config so the executable test project can cover it.
        return SettingsClone.Clone(source);
    }

    private void ToggleVisible()
    {
        if (_panel == null)
        {
            return;
        }

        if (_panel.Visible)
        {
            FlushSettingsIfDirty();
        }

        _panel.Visible = !_panel.Visible;
        AgentRuntime.Instance.PersistOverlayVisible(_panel.Visible);
        if (_panel.Visible) RefreshDynamic();
    }

    private static void CopyText(string text)
    {
        DisplayServer.ClipboardSet(text);
    }

    private void CopyDiagnostics()
    {
        CopyText(AgentRuntime.Instance.ExportDiagnostics());
        AgentRuntime.Instance.NotifyStatus(Loc.T("诊断已复制到剪贴板（不含 API Key 和对话正文）。"));
    }

    private void MarkSettingsDirty()
    {
        if (_rebuildingSettings)
        {
            return;
        }

        _settingsDirty = true;
        if (_saveStatus != null)
        {
            _saveStatus.Text = Loc.T("未保存");
        }
    }

    private void WatchLine(LineEdit edit)
    {
        edit.TextChanged += _ => MarkSettingsDirty();
    }

    private void WatchCheck(CheckBox check)
    {
        check.Toggled += _ => MarkSettingsDirty();
    }

    private void WatchCombo(OptionButton combo)
    {
        combo.ItemSelected += _ => MarkSettingsDirty();
    }

    private void FlushSettingsIfDirty()
    {
        if (!_settingsDirty || _settingsBody == null)
        {
            return;
        }

        SaveSettingsFromUi();
    }

    private async Task LaunchDualAsync()
    {
        FlushSettingsIfDirty();
        // Same route choice as POST /action invite_ai_teammate: a verified play model means the
        // teammate plays by itself; otherwise it launches for external takeover and comes up paused.
        var settings = HarvestSettings();
        var companionAutoPlay = FirstRunSetup.Evaluate(settings).ReadyToInvite;
        await AgentRuntime.Instance.LaunchDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        RefreshDynamic();
    }

    private async Task ContinueDualAsync()
    {
        FlushSettingsIfDirty();
        var settings = HarvestSettings();
        var companionAutoPlay = FirstRunSetup.Evaluate(settings).ReadyToInvite;
        _continueLaunching = true;
        try
        {
            RefreshDynamic();
            await AgentRuntime.Instance.ContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        }
        finally
        {
            _continueLaunching = false;
        }
        RefreshDynamic();
    }

    private static string DualHintText()
    {
        var settings = AgentRuntime.Instance.Settings;
        if (!settings.CompanionAutoSelectCharacter)
        {
            return Loc.T("请从主菜单邀请。第二窗口打开后，会停在选角界面让 AI 自己决定选角，也可以你切过去给它选好、点出发；之后它自己点开局事件并进图。");
        }

        return FirstRunSetup.Evaluate(settings).ReadyToInvite
            ? Loc.T("请从主菜单邀请。第二窗口打开后，AI 会自己选角、点开局事件并进图。你继续在这个窗口操作自己的角色；轮到它时，它会自动出牌。")
            : Loc.T("请从主菜单邀请。第二窗口打开后，AI 会加入并进图，然后停在原地等待外部接管，不会自己出牌；你继续在这个窗口操作自己的角色。");
    }

    /// <summary>Continue is offered only where continue_ai_teammate would be accepted: host main menu with a co-op save on disk.</summary>
    private static bool CanOfferContinue()
    {
        if (InstanceRole.IsCompanion) return false;
        try
        {
            return GameStateService.CanContinueAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Invite is offered only where invite_ai_teammate would be advertised, including DualLaunching and PlayRunning.</summary>
    private static bool CanOfferInvite()
    {
        if (InstanceRole.IsCompanion) return false;
        try
        {
            return GameStateService.CanInviteAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The screen behind the button changes with no runtime event the overlay could subscribe to, so
    /// availability is re-read on the panel tick as well as whenever the tab comes into view. Without
    /// the tick, a panel already sitting on this tab kept the button greyed out across the boot modal
    /// clearing, and only a tab switch brought it back.
    /// </summary>
    private void RefreshContinueAvailability()
    {
        if (_dualLaunchButton != null)
        {
            var canInvite = CanOfferInvite();
            _dualLaunchButton.Disabled = AgentRuntime.Instance.DualLaunching || !canInvite;
        }

        if (_dualContinueButton == null)
        {
            return;
        }

        var canContinue = CanOfferContinue();
        _dualContinueButton.Disabled = AgentRuntime.Instance.DualLaunching || !canContinue;
        _dualContinueButton.TooltipText = canContinue ? "" : Loc.T("主菜单上有联机存档时可用。");
    }

    /// <summary>
    /// The runtime's own change notification, marshalled onto the game thread: it can be raised from
    /// a model callback or an HTTP request thread, and every control it repaints belongs to the game.
    /// </summary>
    private void OnRuntimeChanged()
    {
        _ = GameThread.InvokeAsync(RefreshDynamic);
    }

    /// <summary>
    /// TEMPORARY: reports, once per page, which control's minimum width exceeds the panel. Removed
    /// once the offending control is known -- the clipping has survived two earlier guesses.
    /// </summary>
    private void ReportOverflowOnce()
    {
        if (_panel == null || _contentColumn == null || !_panel.Visible)
        {
            return;
        }

        if (_overflowReportedFor == _tab)
        {
            return;
        }

        _overflowReportedFor = _tab;
        var limit = _panel.Size.X;
        var lines = new System.Collections.Generic.List<string>();
        CollectWide(_contentColumn, limit, lines);
        Godot.GD.Print($"[STS2AIAgent.Wide] tab={_tab} panel={limit:0} offenders={lines.Count}");
        foreach (var line in lines)
        {
            Godot.GD.Print("  " + line);
        }
    }

    private static void CollectWide(Node node, float limit, System.Collections.Generic.List<string> lines)
    {
        if (node is Control control && control.Visible)
        {
            var minimum = control.GetCombinedMinimumSize().X;
            if (minimum > limit)
            {
                var text = control is Label label ? " text=" + label.Text : string.Empty;
                lines.Add($"{control.GetType().Name} minW={minimum:0} sizeW={control.Size.X:0}{text}");
            }
        }

        foreach (var child in node.GetChildren())
        {
            CollectWide(child, limit, lines);
        }
    }

    private void OnProcessFrame()
    {
        var hotkeyName = AgentRuntime.Instance.Settings.Hotkey;
        UiFactory.TryParseHotkey(hotkeyName, out var key);
        var down = Input.IsPhysicalKeyPressed(key);
        if (down && !_hotkeyWasDown)
        {
            ToggleVisible();
        }

        _hotkeyWasDown = down;

        if (_host != null && _host.Size != _lastViewportSize)
        {
            ApplyPlacement(keepCurrent: _lastViewportSize != Vector2.Zero);
        }

        var now = Time.GetTicksMsec();
        if (now - _lastRefreshMs > 800)
        {
            _lastRefreshMs = now;
            ReportOverflowOnce();
            if (_panel?.Visible == true)
            {
                if (_apiLabel != null)
                {
                    _apiLabel.Text = Loc.T("{0}  ·  {1}  ·  热键 {2}", HttpServer.Instance.Prefix, InstanceRole.Current, hotkeyName);
                }

                if (IsTabVisible(OverlayTabCatalog.Play) && _playScreen != null)
                {
                    try
                    {
                        _playScreen.Text = Loc.T("屏幕：{0}", GameStateService.BuildStatePayload().screen);
                    }
                    catch
                    {
                        _playScreen.Text = Loc.T("屏幕：-");
                    }
                }

                // The play page hosts the multiplayer section, the decision log and the Jev panel, all
                // of which mirror state that can change with no runtime event to subscribe to (an action
                // submitted over the HTTP API records a decision without raising Changed). The page that
                // is already open re-reads them on the same tick.
                if (IsTabVisible(OverlayTabCatalog.Play))
                {
                    RefreshContinueAvailability();
                    // Cached inside the runtime and throttled there: this runs on the game thread, so
                    // it asks for a refresh and renders whatever the last one produced.
                    AgentRuntime.Instance.RequestTeammateStatusRefresh();
                    if (_teammateLive != null)
                    {
                        _teammateLive.Text = TeammateLiveText();
                    }

                    RefreshDecisionPage(AgentRuntime.Instance.PlayerFacing());
                }
            }
        }
    }

    private void TearDown()
    {
        FlushSettingsIfDirty();

        if (_languageHooked)
        {
            _languageHooked = false;
            Loc.LanguageChanged -= OnLanguageChanged;
        }

        if (_dragHandle != null)
        {
            _dragHandle.GuiInput -= OnDragHandleGuiInput;
        }

        if (_layer != null)
        {
            _layer.TreeEntered -= OnOverlayEnteredTree;
        }

        if (_tree != null)
        {
            _tree.ProcessFrame -= OnProcessFrame;
        }

        _layer?.QueueFree();
        _layer = null;
        _host = null;
        _panel = null;
        _dragHandle = null;
        _tree = null;
    }

    private void HookLanguageChanges()
    {
        if (_languageHooked)
        {
            return;
        }

        _languageHooked = true;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // The game can raise this from its own thread; the overlay must be rebuilt on the game thread.
        _ = GameThread.InvokeAsync(RebuildInPlace);
    }

    /// <summary>
    /// Tears the overlay down and builds it again, keeping where the player left it.
    /// </summary>
    /// <remarks>
    /// Used by the two things that cannot be applied to an existing tree: a language change, because
    /// every string was baked in, and a theme change, because every stylebox was. Both want the same
    /// three facts preserved -- open or closed, which tab, and where the window sits -- so they share
    /// one path instead of each growing its own.
    /// </remarks>
    private void RebuildInPlace()
    {
        // Keep the window where the player left it instead of flashing closed or jumping to chat.
        var hadPanel = _panel != null;
        var wasVisible = _panel?.Visible ?? false;
        var tab = _tab;
        TearDown();
        TryBuildOrRetry();
        if (hadPanel && _panel != null)
        {
            _panel.Visible = wasVisible;
        }

        ShowTab(tab);
    }

    private void OnDragHandleGuiInput(InputEvent evt)
    {
        if (_panel == null || _dragHandle == null)
        {
            return;
        }

        if (evt is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _dragging = button.Pressed;
            _dragHandle.AcceptEvent();
            if (!button.Pressed)
            {
                PersistCurrentPlacement();
            }

            return;
        }

        if (evt is InputEventMouseMotion motion && _dragging)
        {
            MovePanel(_panel.Position + motion.Relative);
            _dragHandle.AcceptEvent();
        }
    }

    private void ResetPlacement()
    {
        AgentRuntime.Instance.PersistOverlayPlacement(null, null);
        _lastViewportSize = Vector2.Zero;
        ApplyPlacement();
    }

    private void ApplyPlacement(bool keepCurrent = false)
    {
        if (_panel == null || _host == null || !_panel.IsInsideTree())
        {
            return;
        }

        var viewport = ReadViewportSize();
        if (viewport.X < 32 || viewport.Y < 32)
        {
            return;
        }

        var size = PanelSizeFor(viewport);
        Vector2 position;
        if (keepCurrent)
        {
            position = _panel.Position;
        }
        else
        {
            var settings = AgentRuntime.Instance.Settings;
            var defaultX = viewport.X - size.X - 12;
            var defaultY = 40f;
            position = new Vector2(settings.OverlayLeft ?? defaultX, settings.OverlayTop ?? defaultY);
        }

        _panel.Size = size;
        _panel.CustomMinimumSize = new Vector2(PanelWidth, Math.Min(size.Y, 360));
        // A ceiling on the content column, not just a floor on the panel. A Godot container expands to
        // fit its children's minimums, and one unwrapped sentence can report a minimum wider than the
        // panel -- measured live on 2026-09-21 at 691 against a 440 panel, which clipped every card
        // against the panel edge. Pinning the scroll body to the panel width makes over-wide content
        // reflow instead of pushing the layout out.
        if (_pageHost != null)
        {
            _pageHost.CustomMinimumSize = new Vector2(size.X - 2 * UiFactory.SpaceMd, 0);
        }

        MovePanel(position, persist: false);
        _lastViewportSize = viewport;
    }

    private void MovePanel(Vector2 position, bool persist = false)
    {
        if (_panel == null || _host == null)
        {
            return;
        }

        var viewport = ReadViewportSize();
        if (viewport.X < 32 || viewport.Y < 32)
        {
            return;
        }

        var size = _panel.Size;
        if (size.X < 32 || size.Y < 32)
        {
            size = PanelSizeFor(viewport);
            _panel.Size = size;
        }

        var maxX = Math.Max(8, viewport.X - size.X - 8);
        var maxY = Math.Max(8, viewport.Y - size.Y - 8);
        var clamped = new Vector2(
            Math.Clamp(position.X, 8, maxX),
            Math.Clamp(position.Y, 8, maxY));
        _panel.Position = clamped;
        PlaceEdgeTab(viewport);
        if (persist)
        {
            PersistCurrentPlacement();
        }
    }

    private void PlaceEdgeTab(Vector2 viewport)
    {
        if (_edgeTab == null || _panel == null)
        {
            return;
        }

        var tabSize = _edgeTab.CustomMinimumSize;
        var onLeft = _panel.Position.X + _panel.Size.X * 0.5f < viewport.X * 0.5f;
        var y = Math.Clamp(_panel.Position.Y + 16, 8, Math.Max(8, viewport.Y - tabSize.Y - 8));
        _edgeTab.Position = onLeft
            ? new Vector2(4, y)
            : new Vector2(viewport.X - tabSize.X - 4, y);
    }

    private void PersistCurrentPlacement()
    {
        if (_panel == null)
        {
            return;
        }

        AgentRuntime.Instance.PersistOverlayPlacement(_panel.Position.X, _panel.Position.Y);
    }

    private Vector2 ReadViewportSize()
    {
        if (_host == null)
        {
            return Vector2.Zero;
        }

        if (_host.Size.X >= 32 && _host.Size.Y >= 32)
        {
            return _host.Size;
        }

        if (!_host.IsInsideTree())
        {
            return Vector2.Zero;
        }

        return _host.GetViewportRect().Size;
    }

    private static Vector2 PanelSizeFor(Vector2 viewport)
    {
        var height = Math.Clamp(viewport.Y * 0.72f, 520f, Math.Max(520f, viewport.Y - 80f));
        return new Vector2(PanelWidth, height);
    }
}