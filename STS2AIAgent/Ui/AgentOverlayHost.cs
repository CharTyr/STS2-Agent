using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent.Ui;

internal sealed class AgentOverlayHost
{
    private const string LogPrefix = "[STS2AIAgent.Overlay]";
    private const int PanelWidth = 440;

    private static AgentOverlayHost? _instance;

    private CanvasLayer? _layer;
    private Control? _panel;
    private Button? _edgeTab;
    private SceneTree? _tree;
    private bool _hotkeyWasDown;
    private ulong _lastRefreshMs;
    private string _tab = "chat";

    private RichTextLabel? _chatLog;
    private TextEdit? _chatInput;
    private CheckBox? _attachState;
    private CheckBox? _attachShot;
    private CheckBox? _allowAct;
    private Label? _playStatus;
    private Label? _playScreen;
    private Label? _playAction;
    private Label? _playThought;
    private Label? _apiLabel;
    private Label? _dualStatus;
    private Button? _playToggle;
    private Control? _chatPage;
    private Control? _settingsPage;
    private Control? _playPage;
    private Control? _dualPage;
    private VBoxContainer? _settingsBody;

    private readonly List<EndpointEditors> _endpointEditors = new();
    private readonly List<ModelEditors> _modelEditors = new();
    private OptionButton? _conversationCombo;
    private OptionButton? _playCombo;
    private OptionButton? _visionCombo;
    private OptionButton? _thinkingCombo;
    private LineEdit? _hotkeyEdit;

    public static void Install()
    {
        if (_instance != null)
        {
            return;
        }

        _instance = new AgentOverlayHost();
        _instance.Build();
        AgentRuntime.Instance.Changed += _instance.OnRuntimeChanged;
    }

    public static void Uninstall()
    {
        if (_instance == null)
        {
            return;
        }

        AgentRuntime.Instance.Changed -= _instance.OnRuntimeChanged;
        _instance.TearDown();
        _instance = null;
    }

    private void Build()
    {
        var game = NGame.Instance;
        if (game == null)
        {
            Log.Warn($"{LogPrefix} NGame is not ready; overlay skipped");
            return;
        }

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

        _edgeTab = UiFactory.Button("AI", ToggleVisible);
        _edgeTab.CustomMinimumSize = new Vector2(36, 72);
        _edgeTab.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _edgeTab.OffsetLeft = -40;
        _edgeTab.OffsetRight = -4;
        _edgeTab.OffsetTop = -36;
        _edgeTab.OffsetBottom = 36;

        _panel = new PanelContainer
        {
            Name = "AgentPanel",
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _panel.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle());
        _panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _panel.OffsetLeft = -PanelWidth - 12;
        _panel.OffsetRight = -12;
        _panel.OffsetTop = 40;
        _panel.OffsetBottom = -40;
        _panel.Visible = AgentRuntime.Instance.Settings.OverlayVisibleOnStart;

        var layout = UiFactory.Column();
        layout.AddChild(BuildHeader());
        layout.AddChild(BuildTabs());
        _chatPage = BuildChatPage();
        _settingsPage = BuildSettingsPage();
        _playPage = BuildPlayPage();
        _dualPage = BuildDualPage();
        layout.AddChild(_chatPage);
        layout.AddChild(_settingsPage);
        layout.AddChild(_playPage);
        layout.AddChild(_dualPage);
        _panel.AddChild(layout);

        host.AddChild(_edgeTab);
        host.AddChild(_panel);
        _layer.AddChild(host);
        root.AddChild(_layer);
        _tree.ProcessFrame += OnProcessFrame;
        ShowTab("chat");
        RefreshDynamic();
        Log.Info($"{LogPrefix} Installed");
    }

    private Control BuildHeader()
    {
        var title = UiFactory.Label("STS2 AI Agent", 16);
        var hide = UiFactory.Button("隐藏", ToggleVisible);
        hide.CustomMinimumSize = new Vector2(64, 0);
        hide.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _apiLabel = UiFactory.Label("", 12, muted: true);
        var column = UiFactory.Column();
        column.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        column.AddChild(UiFactory.Row(title, hide));
        column.AddChild(_apiLabel);
        return column;
    }

    private Control BuildTabs()
    {
        var chat = UiFactory.Button("对话", () => ShowTab("chat"));
        var settings = UiFactory.Button("设置", () => ShowTab("settings"));
        var play = UiFactory.Button("游玩", () => ShowTab("play"));
        var dual = UiFactory.Button("双开", () => ShowTab("dual"));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var button in new[] { chat, settings, play, dual })
        {
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(button);
        }

        return row;
    }

    private Control BuildChatPage()
    {
        var page = UiFactory.Column();
        _chatLog = UiFactory.Rich();
        page.AddChild(UiFactory.Scroll(_chatLog));
        _attachState = UiFactory.Check("附带当前状态", true);
        _attachShot = UiFactory.Check("附带截图（视觉）", false);
        _allowAct = UiFactory.Check("允许代打", false);
        page.AddChild(UiFactory.Row(_attachState, _attachShot));
        page.AddChild(_allowAct);
        _chatInput = UiFactory.Multiline("", 70);
        page.AddChild(_chatInput);
        var send = UiFactory.Button("发送", () => _ = SendChatAsync());
        var clear = UiFactory.Button("清空", () => AgentRuntime.Instance.ClearChat());
        page.AddChild(UiFactory.Row(send, clear));
        return page;
    }

    private Control BuildSettingsPage()
    {
        var page = UiFactory.Column();
        _settingsBody = UiFactory.Column();
        page.AddChild(UiFactory.Scroll(_settingsBody));
        var addEndpoint = UiFactory.Button("添加端点", AddEndpoint);
        var addModel = UiFactory.Button("添加模型", AddModel);
        var save = UiFactory.Button("保存设置", SaveSettingsFromUi);
        var test = UiFactory.Button("测试连通", () => _ = TestConnectionAsync());
        page.AddChild(UiFactory.Row(addEndpoint, addModel));
        page.AddChild(UiFactory.Row(save, test));
        RebuildSettingsForm();
        return page;
    }

    private Control BuildPlayPage()
    {
        var page = UiFactory.Column();
        _playStatus = UiFactory.Label("状态：-");
        _playScreen = UiFactory.Label("屏幕：-");
        _playAction = UiFactory.Label("最近动作：-");
        _playThought = UiFactory.Label("思考：-", 13, muted: true);
        _playToggle = UiFactory.Button("开始自动游玩", TogglePlay);
        var step = UiFactory.Button("单步", () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        page.AddChild(_playStatus);
        page.AddChild(_playScreen);
        page.AddChild(_playAction);
        page.AddChild(_playThought);
        page.AddChild(UiFactory.Row(_playToggle, step));
        page.AddChild(UiFactory.Label("自动游玩会按 compact 状态调用工具并执行一个动作。对话默认只读；勾选「允许代打」或明确说「帮我打」才会执行动作。", 12, muted: true));
        return page;
    }

    private Control BuildDualPage()
    {
        var page = UiFactory.Column();
        page.AddChild(UiFactory.Label("从当前窗口启动第二个游戏进程，本机主持大厅，同伴实例由模型自动加入并游玩。", 13));
        page.AddChild(UiFactory.Label("限制：走游戏 debug「multiplayer test」大厅；Steam 可能阻止双开；不要用会杀掉已有进程的启动脚本。", 12, muted: true));
        var launch = UiFactory.Button("启动本地双实例与模型联机", () => _ = LaunchDualAsync());
        page.AddChild(launch);
        _dualStatus = UiFactory.Label("尚未启动双开。", 13, muted: true);
        page.AddChild(_dualStatus);
        return page;
    }

    private void RebuildSettingsForm()
    {
        if (_settingsBody == null)
        {
            return;
        }

        foreach (var child in _settingsBody.GetChildren().ToArray())
        {
            _settingsBody.RemoveChild(child);
            child.QueueFree();
        }

        _endpointEditors.Clear();
        _modelEditors.Clear();
        var settings = CloneSettings(AgentRuntime.Instance.Settings);

        _settingsBody.AddChild(UiFactory.Label("端点", 15));
        for (var i = 0; i < settings.Endpoints.Count; i++)
        {
            _settingsBody.AddChild(BuildEndpointCard(settings.Endpoints[i], i));
        }

        _settingsBody.AddChild(UiFactory.Label("模型", 15));
        for (var i = 0; i < settings.Models.Count; i++)
        {
            _settingsBody.AddChild(BuildModelCard(settings.Models[i], i, settings));
        }

        _settingsBody.AddChild(UiFactory.Label("角色绑定", 15));
        _conversationCombo = FillModelCombo(settings, settings.ConversationModelId, includeEmpty: false);
        _playCombo = FillModelCombo(settings, settings.PlayModelId, includeEmpty: true);
        _visionCombo = FillModelCombo(settings, settings.VisionModelId, includeEmpty: true);
        _settingsBody.AddChild(Labeled("主对话模型", _conversationCombo));
        _settingsBody.AddChild(Labeled("游玩模型（可空=主对话）", _playCombo));
        _settingsBody.AddChild(Labeled("外挂视觉模型（可空）", _visionCombo));

        _thinkingCombo = UiFactory.Combo();
        foreach (var item in new[] { "off", "low", "medium", "high" })
        {
            _thinkingCombo.AddItem(item);
        }

        SelectByText(_thinkingCombo, settings.ThinkingIntensity);
        _settingsBody.AddChild(Labeled("思考强度", _thinkingCombo));
        _hotkeyEdit = UiFactory.Line(settings.Hotkey, "F8");
        _settingsBody.AddChild(Labeled("开关热键", _hotkeyEdit));
        _settingsBody.AddChild(UiFactory.Label("配置文件：" + AgentRuntime.Instance.SettingsPath, 11, muted: true));
    }

    private Control BuildEndpointCard(LlmEndpoint endpoint, int index)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle(UiFactory.BgRaised, 6));
        var column = UiFactory.Column();
        var name = UiFactory.Line(endpoint.Name, "名称");
        var url = UiFactory.Line(endpoint.BaseUrl, "https://api.openai.com/v1");
        var key = UiFactory.Line(endpoint.ApiKey, "API Key", secret: true);
        var enabled = UiFactory.Check("启用", endpoint.Enabled);
        var remove = UiFactory.Button("删除", () => RemoveEndpoint(index));
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
        var display = UiFactory.Line(model.DisplayName, "显示名");
        var modelName = UiFactory.Line(model.Model, "模型名");
        var endpointCombo = UiFactory.Combo();
        for (var i = 0; i < settings.Endpoints.Count; i++)
        {
            var endpoint = settings.Endpoints[i];
            endpointCombo.AddItem(string.IsNullOrWhiteSpace(endpoint.Name) ? endpoint.Id : endpoint.Name);
            endpointCombo.SetItemMetadata(i, endpoint.Id);
            if (endpoint.Id == model.EndpointId)
            {
                endpointCombo.Selected = i;
            }
        }

        var vision = UiFactory.Check("视觉", model.SupportsVision);
        var tools = UiFactory.Check("工具调用", model.SupportsTools);
        var thinking = UiFactory.Combo();
        foreach (var item in new[] { "auto", "reasoning_effort", "deepseek", "prompt" })
        {
            thinking.AddItem(item);
        }

        SelectByText(thinking, model.ThinkingMode);
        var remove = UiFactory.Button("删除", () => RemoveModel(index));
        column.AddChild(UiFactory.Row(display, remove));
        column.AddChild(UiFactory.Row(modelName, endpointCombo));
        column.AddChild(UiFactory.Row(vision, tools, thinking));
        box.AddChild(column);
        _modelEditors.Add(new ModelEditors(model.Id, display, modelName, endpointCombo, vision, tools, thinking));
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
        var start = 0;
        if (includeEmpty)
        {
            combo.AddItem("(默认/无)");
            combo.SetItemMetadata(0, "");
            start = 1;
        }

        for (var i = 0; i < settings.Models.Count; i++)
        {
            var model = settings.Models[i];
            combo.AddItem(model.Label);
            combo.SetItemMetadata(start + i, model.Id);
            if (model.Id == selectedId)
            {
                combo.Selected = start + i;
            }
        }

        if (combo.Selected < 0)
        {
            combo.Selected = 0;
        }

        return combo;
    }

    private void AddEndpoint()
    {
        var settings = HarvestSettings();
        settings.Endpoints.Add(new LlmEndpoint { Name = "新端点" });
        AgentRuntime.Instance.SaveSettings(settings);
        RebuildSettingsForm();
    }

    private void AddModel()
    {
        var settings = HarvestSettings();
        settings.Models.Add(new LlmModelConfig
        {
            EndpointId = settings.Endpoints.FirstOrDefault()?.Id ?? string.Empty,
            Model = "gpt-4o",
            DisplayName = "新模型"
        });
        AgentRuntime.Instance.SaveSettings(settings);
        RebuildSettingsForm();
    }

    private void RemoveEndpoint(int index)
    {
        var settings = HarvestSettings();
        if (index >= 0 && index < settings.Endpoints.Count)
        {
            settings.Endpoints.RemoveAt(index);
        }

        AgentRuntime.Instance.SaveSettings(settings);
        RebuildSettingsForm();
    }

    private void RemoveModel(int index)
    {
        var settings = HarvestSettings();
        if (index >= 0 && index < settings.Models.Count)
        {
            settings.Models.RemoveAt(index);
        }

        AgentRuntime.Instance.SaveSettings(settings);
        RebuildSettingsForm();
    }

    private void SaveSettingsFromUi()
    {
        AgentRuntime.Instance.SaveSettings(HarvestSettings());
        RebuildSettingsForm();
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
            model.ThinkingMode = SelectedText(editor.Thinking);
            if (editor.Endpoint.Selected >= 0)
            {
                model.EndpointId = editor.Endpoint.GetItemMetadata(editor.Endpoint.Selected).AsString();
            }
        }

        current.ConversationModelId = SelectedMetadata(_conversationCombo);
        current.PlayModelId = EmptyToNull(SelectedMetadata(_playCombo));
        current.VisionModelId = EmptyToNull(SelectedMetadata(_visionCombo));
        current.ThinkingIntensity = SelectedText(_thinkingCombo);
        current.Hotkey = _hotkeyEdit?.Text.Trim() is { Length: > 0 } hotkey ? hotkey : "F8";
        current.AttachStateInChat = _attachState?.ButtonPressed ?? true;
        current.AttachScreenshotInChat = _attachShot?.ButtonPressed ?? false;
        return current;
    }

    private static AgentSettings CloneSettings(AgentSettings source)
    {
        source.EnsureValidShape();
        return new AgentSettings
        {
            Endpoints = source.Endpoints.Select(endpoint => new LlmEndpoint
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                BaseUrl = endpoint.BaseUrl,
                ApiKey = endpoint.ApiKey,
                Enabled = endpoint.Enabled
            }).ToList(),
            Models = source.Models.Select(model => new LlmModelConfig
            {
                Id = model.Id,
                EndpointId = model.EndpointId,
                Model = model.Model,
                DisplayName = model.DisplayName,
                SupportsVision = model.SupportsVision,
                SupportsTools = model.SupportsTools,
                ThinkingMode = model.ThinkingMode
            }).ToList(),
            ConversationModelId = source.ConversationModelId,
            PlayModelId = source.PlayModelId,
            VisionModelId = source.VisionModelId,
            ThinkingIntensity = source.ThinkingIntensity,
            Hotkey = source.Hotkey,
            AttachStateInChat = source.AttachStateInChat,
            AttachScreenshotInChat = source.AttachScreenshotInChat,
            OverlayVisibleOnStart = source.OverlayVisibleOnStart
        };
    }

    private void ShowTab(string tab)
    {
        _tab = tab;
        if (_chatPage != null) _chatPage.Visible = tab == "chat";
        if (_settingsPage != null) _settingsPage.Visible = tab == "settings";
        if (_playPage != null) _playPage.Visible = tab == "play";
        if (_dualPage != null) _dualPage.Visible = tab == "dual";
    }

    private void ToggleVisible()
    {
        if (_panel == null)
        {
            return;
        }

        _panel.Visible = !_panel.Visible;
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

    private async Task SendChatAsync()
    {
        var text = _chatInput?.Text ?? string.Empty;
        if (_chatInput != null)
        {
            _chatInput.Text = string.Empty;
        }

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
        var result = await AgentRuntime.Instance.TestConnectionAsync(CancellationToken.None);
        if (_chatLog != null)
        {
            _chatLog.AppendText($"[b]连通测试[/b]\n{Escape(result)}\n\n");
        }

        ShowTab("chat");
    }

    private async Task LaunchDualAsync()
    {
        await AgentRuntime.Instance.LaunchDualInstanceAsync(CancellationToken.None);
        RefreshDynamic();
    }

    private void OnRuntimeChanged()
    {
        _ = GameThread.InvokeAsync(RefreshDynamic);
    }

    private void RefreshDynamic()
    {
        if (_apiLabel != null)
        {
            _apiLabel.Text = $"{HttpServer.Instance.Prefix}  ·  {InstanceRole.Current}  ·  热键 {AgentRuntime.Instance.Settings.Hotkey}";
        }

        if (_playStatus != null)
        {
            _playStatus.Text = "状态：" + AgentRuntime.Instance.Status;
        }

        if (_playScreen != null)
        {
            try
            {
                _playScreen.Text = "屏幕：" + GameStateService.BuildStatePayload().screen;
            }
            catch
            {
                _playScreen.Text = "屏幕：-";
            }
        }

        if (_playAction != null)
        {
            _playAction.Text = "最近动作：" + AgentRuntime.Instance.LastAction;
        }

        if (_playThought != null)
        {
            _playThought.Text = "思考：" + Trim(AgentRuntime.Instance.LastThought, 240);
        }

        if (_playToggle != null)
        {
            _playToggle.Text = AgentRuntime.Instance.PlayRunning ? "暂停自动游玩" : "开始自动游玩";
        }

        if (_dualStatus != null)
        {
            _dualStatus.Text = AgentRuntime.Instance.DualStatus;
        }

        if (_chatLog != null)
        {
            _chatLog.Clear();
            foreach (var turn in AgentRuntime.Instance.History)
            {
                var who = turn.Role == "user" ? "你" : "助手";
                _chatLog.AppendText($"[b]{who}[/b]\n{Escape(turn.Text)}\n\n");
            }
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

        var now = Time.GetTicksMsec();
        if (now - _lastRefreshMs > 800)
        {
            _lastRefreshMs = now;
            if (_panel?.Visible == true)
            {
                if (_apiLabel != null)
                {
                    _apiLabel.Text = $"{HttpServer.Instance.Prefix}  ·  {InstanceRole.Current}  ·  热键 {hotkeyName}";
                }

                if (_playPage?.Visible == true && _playScreen != null)
                {
                    try
                    {
                        _playScreen.Text = "屏幕：" + GameStateService.BuildStatePayload().screen;
                    }
                    catch
                    {
                        _playScreen.Text = "屏幕：-";
                    }
                }
            }
        }
    }

    private void TearDown()
    {
        if (_tree != null)
        {
            _tree.ProcessFrame -= OnProcessFrame;
        }

        _layer?.QueueFree();
        _layer = null;
        _panel = null;
        _tree = null;
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

        combo.Selected = 0;
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
        OptionButton Thinking);
}
