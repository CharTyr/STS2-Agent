using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Server;
using STS2AIAgent.Vision;

namespace STS2AIAgent.Ui;

internal sealed class AgentOverlayHost
{
    private const string LogPrefix = "[STS2AIAgent.Overlay]";
    private const int PanelWidth = 440;

    private static AgentOverlayHost? _instance;

    private CanvasLayer? _layer;
    private Control? _host;
    private Control? _panel;
    private Control? _dragHandle;
    private Button? _edgeTab;
    private SceneTree? _tree;
    private bool _hotkeyWasDown;
    private bool _dragging;
    private Vector2 _lastViewportSize;
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
    private Button? _stepButton;
    private Button? _sendButton;
    private Button? _mcpToggle;
    private Label? _mcpStatus;
    private LineEdit? _mcpPathEdit;
    private Control? _pageHost;
    private Control? _chatFooter;
    private Control? _chatPage;
    private Control? _settingsPage;
    private Control? _playPage;
    private Control? _dualPage;
    private Control? _connectPage;
    private VBoxContainer? _settingsBody;
    private int _buildAttempts;
    private bool _captureHidden;

    private readonly List<EndpointEditors> _endpointEditors = new();
    private readonly List<ModelEditors> _modelEditors = new();
    private OptionButton? _conversationCombo;
    private OptionButton? _playCombo;
    private OptionButton? _visionCombo;
    private LineEdit? _hotkeyEdit;

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
        chrome.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle());

        var layout = UiFactory.Column();
        layout.AddChild(BuildHeader());
        layout.AddChild(BuildTabs());

        _pageHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _chatPage = BuildChatPage();
        _settingsPage = BuildSettingsPage();
        _playPage = BuildPlayPage();
        _dualPage = BuildDualPage();
        _connectPage = BuildConnectPage();
        foreach (var page in new Control[] { _chatPage, _settingsPage, _playPage, _dualPage, _connectPage })
        {
            page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _pageHost.AddChild(page);
        }

        layout.AddChild(_pageHost);
        _chatFooter = BuildChatFooter();
        layout.AddChild(_chatFooter);
        chrome.AddChild(layout);
        _panel.AddChild(chrome);
        _panel.Visible = AgentRuntime.Instance.Settings.OverlayVisibleOnStart;

        host.AddChild(_edgeTab);
        host.AddChild(_panel);
        _layer.AddChild(host);
        _layer.TreeEntered += OnOverlayEnteredTree;
        root.CallDeferred(Node.MethodName.AddChild, _layer);
        _tree.ProcessFrame += OnProcessFrame;
        ShowTab("chat");
        RefreshDynamic();
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
        _dragHandle.AddThemeStyleboxOverride("panel", UiFactory.PanelStyle(UiFactory.BgRaised, 6));
        _dragHandle.GuiInput += OnDragHandleGuiInput;
        var title = UiFactory.Label("STS2 AI Agent", 16);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        var hint = UiFactory.Label("拖动移动", 11, muted: true);
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        var handleColumn = UiFactory.Column();
        handleColumn.MouseFilter = Control.MouseFilterEnum.Ignore;
        handleColumn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        handleColumn.AddChild(title);
        handleColumn.AddChild(hint);
        _dragHandle.AddChild(handleColumn);

        var hide = UiFactory.Button("隐藏", ToggleVisible);
        hide.CustomMinimumSize = new Vector2(64, 0);
        hide.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _apiLabel = UiFactory.Label("", 12, muted: true);
        var column = UiFactory.Column();
        column.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(_dragHandle);
        row.AddChild(hide);
        column.AddChild(row);
        column.AddChild(_apiLabel);
        return column;
    }

    private Control BuildTabs()
    {
        var chat = UiFactory.Button("对话", () => ShowTab("chat"));
        var settings = UiFactory.Button("设置", () => ShowTab("settings"));
        var play = UiFactory.Button("游玩", () => ShowTab("play"));
        var dual = UiFactory.Button("双开", () => ShowTab("dual"));
        var connect = UiFactory.Button("接入", () => ShowTab("connect"));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var button in new[] { chat, settings, play, dual, connect })
        {
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
        _attachState = UiFactory.Check("附带当前状态", settings.AttachStateInChat);
        _attachShot = UiFactory.Check("附带截图（视觉）", settings.AttachScreenshotInChat);
        _allowAct = UiFactory.Check("允许代打", false);
        _attachState.Toggled += _ => PersistChatFlags();
        _attachShot.Toggled += _ => PersistChatFlags();
        footer.AddChild(UiFactory.Row(_attachState, _attachShot));
        footer.AddChild(_allowAct);
        _chatInput = UiFactory.Multiline("", 70);
        footer.AddChild(_chatInput);
        _sendButton = UiFactory.Button("发送", () => _ = SendChatAsync());
        var clear = UiFactory.Button("清空", () => AgentRuntime.Instance.ClearChat());
        footer.AddChild(UiFactory.Row(_sendButton, clear));
        return footer;
    }

    private Control BuildSettingsPage()
    {
        var page = UiFactory.Column();
        _settingsBody = UiFactory.Column();
        page.AddChild(UiFactory.Scroll(_settingsBody, 80));
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
        _stepButton = UiFactory.Button("单步", () => _ = AgentRuntime.Instance.StepOnceAsync(CancellationToken.None));
        page.AddChild(_playStatus);
        page.AddChild(_playScreen);
        page.AddChild(_playAction);
        page.AddChild(_playThought);
        page.AddChild(UiFactory.Row(_playToggle, _stepButton));
        page.AddChild(UiFactory.Label("自动游玩走 compact 状态和工具，与 MCP 相同，不需要视觉即可打完全部流程。对话默认只读；勾选「允许代打」或明确说「帮我打」才会执行动作。", 12, muted: true));
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

    private Control BuildConnectPage()
    {
        var page = UiFactory.Column();
        var settings = AgentRuntime.Instance.Settings;
        page.AddChild(UiFactory.Label("外部 Agent 接入", 15));
        page.AddChild(UiFactory.Label("游戏内自动打不需要 MCP。只有 Cursor / Claude / Codex 等外部客户端才需要启动 MCP。", 12, muted: true));

        var detected = McpProcessLauncher.FindMcpRoot(settings.McpServerPath) ?? settings.McpServerPath;
        _mcpPathEdit = UiFactory.Line(detected, "mcp_server 目录");
        page.AddChild(Labeled("mcp_server 路径", _mcpPathEdit));
        _mcpToggle = UiFactory.Button(AgentRuntime.Instance.McpRunning ? "停止 MCP" : "一键启动 MCP", ToggleMcp);
        var copyApi = UiFactory.Button("复制 API", () => CopyText(HttpServer.Instance.Prefix));
        var copyMcp = UiFactory.Button("复制 MCP", () => CopyText(AgentRuntime.Instance.McpUrl ?? "http://127.0.0.1:8765/mcp"));
        page.AddChild(UiFactory.Row(_mcpToggle, copyApi, copyMcp));
        _mcpStatus = UiFactory.Label(AgentRuntime.Instance.McpStatus, 12, muted: true);
        page.AddChild(_mcpStatus);

        page.AddChild(UiFactory.Label("1) 本机 HTTP API（无需 MCP）", 14));
        page.AddChild(UiFactory.Label("GET /health  /state  /actions/available    POST /action    SSE /events/stream", 12, muted: true));
        page.AddChild(UiFactory.Label("默认 http://127.0.0.1:8080 ，被占用会自动改绑。外部脚本可直接调这些接口。", 12, muted: true));

        page.AddChild(UiFactory.Label("2) Cursor / Claude / Codex（推荐点上面的按钮）", 14));
        page.AddChild(UiFactory.Label("启动后把 MCP URL 配进客户端，例如：", 12, muted: true));
        page.AddChild(UiFactory.Label("{\"mcpServers\":{\"sts2-ai-agent\":{\"url\":\"http://127.0.0.1:8765/mcp\"}}}", 11, muted: true));
        page.AddChild(UiFactory.Label("并设置环境变量 STS2_API_BASE_URL 为当前 API 地址。需要 uv + 本机 mcp_server 目录。", 12, muted: true));

        page.AddChild(UiFactory.Label("3) 命令行 stdio（不点按钮时）", 14));
        page.AddChild(UiFactory.Label("scripts/start-mcp-stdio.ps1  或  cd mcp_server && uv run sts2-mcp-server", 12, muted: true));
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
        _settingsBody.AddChild(UiFactory.Label("视觉可选。不勾选「视觉」、不配外挂视觉时，仍用 compact 状态与工具打完全部内容。", 11, muted: true));
        _hotkeyEdit = UiFactory.Line(settings.Hotkey, "F8");
        _settingsBody.AddChild(Labeled("开关热键", _hotkeyEdit));
        _settingsBody.AddChild(UiFactory.Button("重置窗口位置", ResetPlacement));
        _settingsBody.AddChild(UiFactory.Label("拖动标题栏可移动窗口，位置会保存。", 11, muted: true));
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
        var thinkingMode = UiFactory.Combo();
        foreach (var item in new[] { "auto", "reasoning_effort", "deepseek", "prompt" })
        {
            thinkingMode.AddItem(item);
        }

        SelectByText(thinkingMode, model.ThinkingMode);
        var thinkingIntensity = UiFactory.Combo();
        foreach (var item in new[] { "off", "low", "medium", "high" })
        {
            thinkingIntensity.AddItem(item);
        }

        SelectByText(thinkingIntensity, model.ThinkingIntensity);
        var remove = UiFactory.Button("删除", () => RemoveModel(index));
        column.AddChild(UiFactory.Row(display, remove));
        column.AddChild(UiFactory.Row(modelName, endpointCombo));
        column.AddChild(UiFactory.Row(vision, tools));
        column.AddChild(Labeled("思考方式", thinkingMode));
        column.AddChild(Labeled("思考强度", thinkingIntensity));
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
            DisplayName = "新模型",
            ThinkingIntensity = "medium"
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
            model.ThinkingMode = SelectedText(editor.ThinkingMode);
            model.ThinkingIntensity = SelectedText(editor.ThinkingIntensity);
            if (editor.Endpoint.Selected >= 0)
            {
                model.EndpointId = editor.Endpoint.GetItemMetadata(editor.Endpoint.Selected).AsString();
            }
        }

        current.ConversationModelId = SelectedMetadata(_conversationCombo);
        current.PlayModelId = EmptyToNull(SelectedMetadata(_playCombo));
        current.VisionModelId = EmptyToNull(SelectedMetadata(_visionCombo));
        current.ThinkingIntensity = current.FindModel(current.ConversationModelId)?.ThinkingIntensity
            ?? current.ThinkingIntensity;
        current.Hotkey = _hotkeyEdit?.Text.Trim() is { Length: > 0 } hotkey ? hotkey : "F8";
        current.AttachStateInChat = _attachState?.ButtonPressed ?? true;
        current.AttachScreenshotInChat = _attachShot?.ButtonPressed ?? false;
        current.McpServerPath = _mcpPathEdit?.Text.Trim() ?? current.McpServerPath;
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
                ThinkingMode = model.ThinkingMode,
                ThinkingIntensity = model.ThinkingIntensity
            }).ToList(),
            ConversationModelId = source.ConversationModelId,
            PlayModelId = source.PlayModelId,
            VisionModelId = source.VisionModelId,
            ThinkingIntensity = source.ThinkingIntensity,
            Hotkey = source.Hotkey,
            AttachStateInChat = source.AttachStateInChat,
            AttachScreenshotInChat = source.AttachScreenshotInChat,
            OverlayVisibleOnStart = source.OverlayVisibleOnStart,
            OverlayLeft = source.OverlayLeft,
            OverlayTop = source.OverlayTop,
            McpServerPath = source.McpServerPath,
            McpPort = source.McpPort
        };
    }

    private void ShowTab(string tab)
    {
        _tab = tab;
        if (_chatPage != null) _chatPage.Visible = tab == "chat";
        if (_settingsPage != null) _settingsPage.Visible = tab == "settings";
        if (_playPage != null) _playPage.Visible = tab == "play";
        if (_dualPage != null) _dualPage.Visible = tab == "dual";
        if (_connectPage != null) _connectPage.Visible = tab == "connect";
        if (_chatFooter != null) _chatFooter.Visible = tab == "chat";
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
        ShowTab("chat");
    }

    private void PersistChatFlags()
    {
        AgentRuntime.Instance.PersistChatAttachFlags(
            _attachState?.ButtonPressed ?? true,
            _attachShot?.ButtonPressed ?? false);
    }

    private void ToggleMcp()
    {
        if (AgentRuntime.Instance.McpRunning)
        {
            AgentRuntime.Instance.StopMcp();
            RefreshDynamic();
            return;
        }

        var settings = HarvestSettings();
        AgentRuntime.Instance.SaveSettings(settings);
        _ = AgentRuntime.Instance.StartMcpAsync(CancellationToken.None);
    }

    private static void CopyText(string text)
    {
        DisplayServer.ClipboardSet(text);
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

        if (_stepButton != null)
        {
            _stepButton.Disabled = AgentRuntime.Instance.PlayRunning;
        }

        if (_sendButton != null)
        {
            _sendButton.Disabled = AgentRuntime.Instance.PlayRunning;
        }

        if (_dualStatus != null)
        {
            _dualStatus.Text = AgentRuntime.Instance.DualStatus;
        }

        if (_mcpStatus != null)
        {
            _mcpStatus.Text = AgentRuntime.Instance.McpStatus;
        }

        if (_mcpToggle != null)
        {
            _mcpToggle.Text = AgentRuntime.Instance.McpRunning ? "停止 MCP" : "一键启动 MCP";
        }

        if (_chatLog != null)
        {
            _chatLog.Clear();
            var history = AgentRuntime.Instance.History;
            if (history.Count == 0)
            {
                _chatLog.AppendText("[color=#b3b3ad]在下方输入后点发送。[/color]\n");
            }
            else
            {
                foreach (var turn in history)
                {
                    var who = turn.Role == "user" ? "你" : "助手";
                    _chatLog.AppendText($"[b]{who}[/b]\n{Escape(turn.Text)}\n\n");
                }
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

        if (_host != null && _host.Size != _lastViewportSize)
        {
            ApplyPlacement(keepCurrent: _lastViewportSize != Vector2.Zero);
        }

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
        OptionButton ThinkingMode,
        OptionButton ThinkingIntensity);
}
