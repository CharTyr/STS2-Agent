using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;
using STS2AIAgent.Vision;

namespace STS2AIAgent.Ui;

internal sealed partial class AgentOverlayHost
{
    private const string LogPrefix = "[STS2AIAgent.Overlay]";

    private static AgentOverlayHost? _instance;

    private CanvasLayer? _layer;
    private Control? _host;
    private Control? _panel;
    private PanelContainer? _dragHandle;
    private Button? _edgeTab;
    private SceneTree? _tree;
    private bool _hotkeyWasDown;
    private string? _hotkeyName;
    private Key _hotkey = Key.F8;
    private int _tickCount;

    /// <summary>Failure messages already logged, so a check that fails on every tick logs once.</summary>
    private static readonly HashSet<string> WarnedOnce = new(StringComparer.Ordinal);
    private OverlayPlacement? _placement;
    private readonly OverlayOverflowSentinel _overflow = new();
    private ulong _lastRefreshMs;

    private RichTextLabel? _chatLog;
    private TextEdit? _chatInput;
    private CheckBox? _attachState;
    private CheckBox? _attachShot;
    private Label? _playStatus;
    private Label? _playSummary;
    private Label? _playUsage;
    private LineEdit? _maxTokensEdit;
    private LineEdit? _maxRequestsEdit;
    private CheckBox? _proactiveChatToggle;
    private OptionButton? _proactiveToneCombo;

    /// <summary>The header's live state tag, and the hide button whose tooltip names the hotkey.</summary>
    private Label? _headerStatus;
    private Button? _hideButton;
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
    /// <summary>The local HTTP API address, moved here from the title bar.</summary>
    private Label? _apiAddressLabel;
    private TextEdit? _mcpConfigEdit;
    private Control? _mcpInfoBox;
    private Control? _pageHost;
    /// <summary>The content column the overflow sentinel measures.</summary>
    private Control? _contentColumn;
    private VBoxContainer? _settingsBody;
    private int _buildAttempts;
    private bool _captureHidden;
    private bool _languageHooked;

    private readonly List<EndpointEditors> _endpointEditors = new();
    private readonly List<ModelEditors> _modelEditors = new();
    private OptionButton? _conversationCombo;
    private OptionButton? _visionCombo;
    private LineEdit? _hotkeyEdit;
    private LineEdit? _llmTimeoutEdit;
    private Label? _saveStatus;
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

    // Play-mode and companion-Jev state stays with the host. The play-control partial only writes it.
    private bool _playModeSwitching;
    private string? _modeSwitchNotice;
    private Label? _modeSwitchStatus;
    private string? _companionSettingsNotice;
    private Label? _companionSettingsStatus;
    private bool _companionSettingsSyncing;
    private readonly CompanionSettingsSynchronizer _companionSettingsSynchronizer = new();
    private bool _companionJevRefreshing;
    private long _companionJevAtMs;
    private CompanionJevSnapshot? _companionJev;
    private CompanionConnection? _companionJevSource;
    private Label? _jevDanger;
    private Label? _jevLatency;
    private Label? _jevFallbackRate;
    private readonly SemaphoreSlim _companionSettingsGate = new(1, 1);

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
        // The capture reads this before hiding: a screenshot must reproduce the player's view, so an
        // overlay the player had already hidden stays hidden and is never reopened on top of the game.
        ScreenshotService.OverlayVisibleProbe = IsPanelVisible;
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
        ScreenshotService.OverlayVisibleProbe = null;
        AgentRuntime.Instance.Changed -= _instance.OnRuntimeChanged;
        _instance._modelTestCancellation?.Cancel();
        _instance.TearDown();
        _instance = null;
    }

    /// <summary>
    /// True while the agent panel is on screen. Hiding is what a capture does to the panel, so this is
    /// also the answer to "did the player have the window open" at the moment a capture started.
    /// </summary>
    private static bool IsPanelVisible()
    {
        return _instance?._panel?.Visible == true;
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
        // The window can be rebuilt while a capture is in flight (a theme or language change rebuilds
        // the whole tree). The new panel was built from the saved visibility, so touching a node that
        // is no longer in the tree would only put the panel back on a detached subtree.
        if (_instance._panel is { } panel && panel.IsInsideTree())
        {
            panel.Visible = true;
        }

        if (_instance._edgeTab is { } edgeTab && edgeTab.IsInsideTree())
        {
            edgeTab.Visible = true;
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
        _panel.CustomMinimumSize = new Vector2(OverlayPlacement.PanelWidth, 360);

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

        _placement = new OverlayPlacement(host, _panel, _edgeTab, _pageHost);
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
        Callable.From(() => _placement?.Apply()).CallDeferred();
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
        var title = UiFactory.MarkAccent(UiFactory.Label("STS2 AI Agent", UiFactory.FontTitle, wrap: false));
        title.MouseFilter = Control.MouseFilterEnum.Ignore;

        // The live state tag: the answer to "is it playing" is in the title bar, where the player
        // already looks to find the window, instead of one tab away.
        _headerStatus = UiFactory.Label(Loc.T("○ 待机"), UiFactory.FontCaption, muted: true, wrap: false);
        _headerStatus.MouseFilter = Control.MouseFilterEnum.Ignore;
        _headerStatus.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var titleRow = UiFactory.TightRow(title, _headerStatus);
        titleRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        // One line: the title bar is the drag handle (its cursor says so), and the local API address
        // that used to sit under it is developer information, now in Settings → MCP.
        _dragHandle.AddChild(titleRow);

        var hide = UiFactory.Button(Loc.T("隐藏"), ToggleVisible, UiFactory.ButtonKind.Ghost);
        hide.CustomMinimumSize = new Vector2(56, 0);
        hide.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        hide.TooltipText = Loc.T("热键 {0}", AgentRuntime.Instance.Settings.Hotkey);
        _hideButton = hide;

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiFactory.SpaceSm);
        row.AddChild(_dragHandle);
        row.AddChild(hide);
        return row;
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
        AgentRuntime.Instance.NotifyStatus(Loc.T("诊断已复制（不含 Key 和对话）。"));
    }

    private void SaveDiagnostics()
    {
        try
        {
            var directory = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "sts2-agent-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, AgentRuntime.Instance.ExportDiagnostics());
            OS.ShellOpen(directory);
            AgentRuntime.Instance.NotifyStatus(Loc.T("诊断已保存：{0}", path));
        }
        catch (Exception ex) { AgentRuntime.Instance.NotifyStatus(DiagnosticExport.Redact(ex.Message)); }
    }

    public static void ShowLocalCoopContinue()
    {
        var overlay = _instance;
        if (overlay?._panel == null) return;
        overlay._panel.Visible = true;
        overlay.ShowTab(OverlayTabCatalog.Play);
        _ = overlay.ChangePlayModeAsync(1);
    }

    /// <summary>
    /// Logs a failure the first time it is seen. The checks that call this run on the panel tick and
    /// fall back to a safe answer, so a failure used to be swallowed silently; logging it on every tick
    /// would bury the game log instead.
    /// </summary>
    private static void WarnOnce(string what, Exception ex)
    {
        var message = what + ": " + ex.GetType().Name + ": " + ex.Message;
        if (WarnedOnce.Count < 64 && WarnedOnce.Add(message))
        {
            Log.Warn($"{LogPrefix} {message}");
        }
    }

    /// <summary>
    /// The runtime's own change notification, marshalled onto the game thread: it can be raised from
    /// a model callback or an HTTP request thread, and every control it repaints belongs to the game.
    /// </summary>
    private void OnRuntimeChanged()
    {
        _ = GameThread.InvokeAsync(RefreshDynamic);
    }

    private void OnProcessFrame()
    {
        var hotkeyName = AgentRuntime.Instance.Settings.Hotkey;
        if (!string.Equals(hotkeyName, _hotkeyName, StringComparison.Ordinal))
        {
            _hotkeyName = hotkeyName;
            OverlayHotkey.TryParse(hotkeyName, out _hotkey);
            if (_hideButton != null) _hideButton.TooltipText = Loc.T("热键 {0}", hotkeyName);
        }

        var down = Input.IsPhysicalKeyPressed(_hotkey);
        if (down && !_hotkeyWasDown && !IsTypingIntoTextBox(_hotkey))
        {
            ToggleVisible();
        }

        _hotkeyWasDown = down;

        _placement?.FollowViewport();

        var now = Time.GetTicksMsec();
        if (now - _lastRefreshMs > 800)
        {
            _lastRefreshMs = now;
            // The overflow sentinel walks the whole visible tree; every few seconds is plenty for a
            // diagnostic that only logs when the offender count changes.
            if (_tickCount++ % 4 == 0 && _panel != null && _contentColumn != null)
            {
                _overflow.Report(_panel, _contentColumn, _tab);
            }
            if (_panel?.Visible == true)
            {
                if (IsTabVisible(OverlayTabCatalog.Play) && _playSummary != null)
                {
                    _playSummary.Text = PlaySummaryText();
                }

                // The play page hosts the multiplayer section, the decision log and the Jev panel, all
                // of which mirror state that can change with no runtime event to subscribe to (an action
                // submitted over the HTTP API records a decision without raising Changed). The page that
                // is already open re-reads them on the same tick.
                if (IsTabVisible(OverlayTabCatalog.Play))
                {
                    // The status line carries the turn's phase with an elapsed counter; the tick is
                    // what makes the counter move while a long request is in flight.
                    if (_playStatus != null)
                    {
                        _playStatus.Text = AgentRuntime.Instance.StatusWithElapsed;
                    }

                    RefreshChatLog();
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

    /// <summary>
    /// True when a text box has focus and the hotkey is a key that types. A letter hotkey would
    /// otherwise toggle the window every time the player typed that letter into the chat box.
    /// </summary>
    private bool IsTypingIntoTextBox(Key key)
    {
        if (!OverlayHotkey.IsTypingKey(key) || _host == null || !_host.IsInsideTree())
        {
            return false;
        }

        return _host.GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit;
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
        _placement = null;
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
        // A capture in flight hid the panel; the player still had it open. Keep the new panel hidden
        // until that capture's restore shows it, instead of flashing it into the frame being read.
        var wasVisible = (_panel?.Visible ?? false) || _captureHidden;
        var tab = _tab;
        // Half-typed messages survive the rebuild; the controls holding them are about to be freed.
        var chatDraft = _chatInput?.Text ?? string.Empty;
        var teamDraft = _teamInput?.Text ?? string.Empty;
        TearDown();
        TryBuildOrRetry();
        if (hadPanel && _panel != null)
        {
            _panel.Visible = wasVisible && !_captureHidden;
            if (_captureHidden && _edgeTab != null) _edgeTab.Visible = false;
        }

        if (_chatInput != null) _chatInput.Text = chatDraft;
        if (_teamInput != null) _teamInput.Text = teamDraft;
        ShowTab(tab);
    }

    private void OnDragHandleGuiInput(InputEvent evt)
    {
        if (_dragHandle != null)
        {
            _placement?.OnDragInput(_dragHandle, evt);
        }
    }

    private void ResetPlacement()
    {
        _placement?.Reset();
    }
}
