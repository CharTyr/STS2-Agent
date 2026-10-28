using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The settings page's fast-judgment section: provider picker (with logos), address, format, the key
/// remembered per provider, model and its documented alternatives, links, threshold, timeout and the
/// connection test. Split from the settings form so neither file outgrows its budget.
/// </summary>
internal sealed partial class AgentOverlayHost
{
    /// <summary>The Jev section's editors and its connection-test feedback.</summary>
    private OptionButton? _jevProviderCombo;
    private OptionButton? _jevProtocolCombo;
    private OptionButton? _jevModelPicker;
    private Button? _jevKeyLink;
    private Button? _jevDocsLink;

    /// <summary>
    /// Keys per provider while the form is open (seeded from settings), so switching the picker brings
    /// back that provider's own key. <c>_jevProviderId</c> is the provider the key field belongs to.
    /// </summary>
    private Dictionary<string, string> _jevKeys = new(StringComparer.OrdinalIgnoreCase);
    private string _jevProviderId = CustomJevProvider;
    private const string CustomJevProvider = "custom";
    private LineEdit? _jevBaseUrlEdit;
    private LineEdit? _jevApiKeyEdit;
    private LineEdit? _jevModelEdit;
    private LineEdit? _jevThresholdEdit;
    private LineEdit? _jevTimeoutEdit;
    private Button? _jevTestButton;
    private Label? _jevTestStatus;

    /// <summary>
    /// The fast-judgment section. These settings feed the dual-layer decision mode toggled from the
    /// play page.
    /// </summary>
    private Control BuildJevSection(AgentSettings settings)
    {
        var column = UiFactory.Column();
        var preset = JevEndpoint.FindPreset(settings.JevBaseUrl);
        _jevProviderId = preset?.Id ?? CustomJevProvider;
        _jevKeys = new Dictionary<string, string>(
            settings.JevProviderKeys ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(settings.JevApiKey)) _jevKeys[_jevProviderId] = settings.JevApiKey;

        _jevBaseUrlEdit = UiFactory.Line(settings.JevBaseUrl, "https://api.typesafe.ai");
        _jevApiKeyEdit = UiFactory.Line(settings.JevApiKey, "API Key", secret: true);
        _jevModelEdit = UiFactory.Line(settings.JevModel, "jev-latest");
        _jevThresholdEdit = UiFactory.Line(SettingsInput.FormatThreshold(settings.JevConfidenceThreshold), "0.35");
        _jevTimeoutEdit = UiFactory.Line(settings.JevRequestTimeoutSeconds?.ToString() ?? "", Loc.T("默认 90 秒"));
        WatchLine(_jevBaseUrlEdit);
        WatchLine(_jevApiKeyEdit);
        WatchLine(_jevModelEdit);
        WatchLine(_jevThresholdEdit);
        WatchLine(_jevTimeoutEdit);

        _jevProtocolCombo = UiFactory.Combo();
        _jevProtocolCombo.AddItem(Loc.T("System One 格式"));
        _jevProtocolCombo.AddItem("OpenAI Decisions");
        _jevProtocolCombo.Select(settings.JevProtocol == JevProtocol.OpenAiDecisions ? 1 : 0);
        WatchCombo(_jevProtocolCombo);

        // Picks one of the preset's documented models into the free-text field; hidden for a single model.
        _jevModelPicker = UiFactory.Combo();
        _jevModelPicker.ClipText = true;
        _jevModelPicker.FitToLongestItem = false;
        _jevModelPicker.CustomMinimumSize = new Vector2(80, 0);
        _jevModelPicker.ItemSelected += index =>
        {
            if (_jevModelEdit == null || _jevModelPicker == null || index < 0) return;
            _jevModelEdit.Text = _jevModelPicker.GetItemText((int)index);
            MarkSettingsDirty();
        };
        var modelRow = UiFactory.Row(_jevModelEdit, _jevModelPicker);
        _jevModelPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _jevKeyLink = UiFactory.Button(Loc.T("获取 Key"), () => OpenJevLink(key: true), UiFactory.ButtonKind.Ghost);
        _jevDocsLink = UiFactory.Button(Loc.T("文档"), () => OpenJevLink(key: false), UiFactory.ButtonKind.Ghost);

        column.AddChild(Labeled(Loc.T("服务商"), BuildJevProviderCombo(settings.JevBaseUrl)));
        column.AddChild(Labeled(Loc.T("地址"), _jevBaseUrlEdit));
        column.AddChild(Labeled(Loc.T("接口格式"), _jevProtocolCombo));
        column.AddChild(Labeled(Loc.T("API Key"), _jevApiKeyEdit));
        column.AddChild(Labeled(Loc.T("模型"), modelRow));
        column.AddChild(UiFactory.Row(_jevKeyLink, _jevDocsLink));
        column.AddChild(Labeled(Loc.T("置信度阈值（0-1）"), _jevThresholdEdit));
        column.AddChild(Labeled(Loc.T("请求超时（秒）"), _jevTimeoutEdit));
        RefreshJevPresetExtras(preset);

        _jevTestButton = UiFactory.Button(Loc.T("测试快速判断"), () => _ = TestJevConnectionAsync(), UiFactory.ButtonKind.Ghost);
        _jevTestStatus = UiFactory.StatusLine();
        column.AddChild(UiFactory.Row(_jevTestButton));
        column.AddChild(_jevTestStatus);
        return HelpCard.Build(
            "settings.jev",
            Loc.T("快速判断"),
            Loc.T("在游玩页勾选「用快速判断出牌」后，由快速判断模型（Jev 等）逐步操作游戏，LLM 只做战略规划；单人、多人分别开启。置信度低于阈值时由 LLM 接手。\n选服务商会填好地址、格式和模型，每家的 Key 分开记住。地址可填完整请求地址，也可只填服务根地址。\n「测试」会真实调用一次服务。连续失败 3 次会暂停快速判断一段时间，期间由 LLM 接手。\n预设按各家文档整理，以实际可用为准。"),
            column);
    }

    /// <summary>
    /// Provider picker for the fast-judgment section. Choosing a preset fills the address, format and
    /// model and swaps in that provider's remembered key; editing the address by hand moves the picker
    /// to Custom (or to the preset it now matches) without touching the key.
    /// </summary>
    private OptionButton BuildJevProviderCombo(string currentUrl)
    {
        var combo = UiFactory.Combo();
        combo.AddItem("自定义 / Custom");
        foreach (var preset in JevEndpoint.Presets)
        {
            var icon = JevProviderIcons.Get(preset.Id);
            if (icon != null) combo.AddIconItem(icon, preset.Name);
            else combo.AddItem(preset.Name);
        }
        _jevProviderCombo = combo;

        static int IndexFor(JevEndpoint.Preset? match)
        {
            for (var i = 0; match != null && i < JevEndpoint.Presets.Count; i++)
                if (ReferenceEquals(JevEndpoint.Presets[i], match)) return i + 1;
            return 0;
        }

        combo.Select(IndexFor(JevEndpoint.FindPreset(currentUrl)));
        combo.ItemSelected += index =>
        {
            if (_jevBaseUrlEdit == null || _jevModelEdit == null || _jevApiKeyEdit == null) return;
            var preset = index <= 0 ? null : JevEndpoint.Presets[(int)index - 1];
            var nextId = preset?.Id ?? CustomJevProvider;
            if (string.Equals(nextId, _jevProviderId, StringComparison.OrdinalIgnoreCase)) return;
            RememberJevKey();
            _jevProviderId = nextId;
            _jevApiKeyEdit.Text = _jevKeys.TryGetValue(nextId, out var key) ? key : "";
            if (preset != null)
            {
                _jevBaseUrlEdit.Text = preset.Url;
                _jevModelEdit.Text = preset.Model;
                _jevProtocolCombo?.Select(preset.Protocol == JevProtocol.OpenAiDecisions ? 1 : 0);
            }

            RefreshJevPresetExtras(preset);
            MarkSettingsDirty();
        };
        _jevBaseUrlEdit!.TextChanged += text =>
        {
            if (_jevProviderCombo == null || !GodotObject.IsInstanceValid(_jevProviderCombo)) return;
            var match = JevEndpoint.FindPreset(text);
            _jevProviderCombo.Select(IndexFor(match));
            var nextId = match?.Id ?? CustomJevProvider;
            if (!string.Equals(nextId, _jevProviderId, StringComparison.OrdinalIgnoreCase) && _jevApiKeyEdit != null)
            {
                // The address now names another provider: file the key under the old one, show the new one's own.
                RememberJevKey();
                _jevProviderId = nextId;
                _jevApiKeyEdit.Text = _jevKeys.TryGetValue(nextId, out var key) ? key : "";
            }

            RefreshJevPresetExtras(match);
        };
        return combo;
    }

    /// <summary>Shows the preset's model list and its key/docs links, or hides them for a custom address.</summary>
    private void RefreshJevPresetExtras(JevEndpoint.Preset? preset)
    {
        if (_jevModelPicker != null && GodotObject.IsInstanceValid(_jevModelPicker))
        {
            _jevModelPicker.Clear();
            var models = preset?.Models ?? Array.Empty<string>();
            foreach (var model in models) _jevModelPicker.AddItem(model);
            _jevModelPicker.Select(Array.FindIndex(models, m => string.Equals(m, _jevModelEdit?.Text.Trim(), StringComparison.Ordinal)));
            _jevModelPicker.Visible = models.Length > 1;
        }

        if (_jevKeyLink != null && GodotObject.IsInstanceValid(_jevKeyLink))
            _jevKeyLink.Visible = !string.IsNullOrEmpty(preset?.KeyUrl);
        if (_jevDocsLink != null && GodotObject.IsInstanceValid(_jevDocsLink))
            _jevDocsLink.Visible = !string.IsNullOrEmpty(preset?.DocsUrl);
    }

    private void OpenJevLink(bool key)
    {
        var preset = JevEndpoint.Presets.FirstOrDefault(p => string.Equals(p.Id, _jevProviderId, StringComparison.OrdinalIgnoreCase));
        var url = key ? preset?.KeyUrl : preset?.DocsUrl;
        if (!string.IsNullOrEmpty(url)) OS.ShellOpen(url);
    }

    /// <summary>Stores the key field under the provider it belongs to; a blank field forgets that provider's key.</summary>
    private void RememberJevKey()
    {
        if (_jevApiKeyEdit == null) return;
        var key = _jevApiKeyEdit.Text.Trim();
        if (key.Length == 0) _jevKeys.Remove(_jevProviderId);
        else _jevKeys[_jevProviderId] = key;
    }

    private async Task TestJevConnectionAsync()
    {
        var before = AgentRuntime.Instance.Settings;
        SaveSettingsFromUi();
        if (!ReferenceEquals(before, AgentRuntime.Instance.Settings))
            await SaveSettingsAndSyncJevAsync();
        await GameThread.InvokeAsync(() =>
        {
            if (_jevTestStatus != null && _jevTestStatus.IsInsideTree()) UiFactory.SetStatusLine(_jevTestStatus, Loc.T("正在测试…"));
        });
        var result = await AgentRuntime.Instance.TestJevConnectionAsync(CancellationToken.None);
        await GameThread.InvokeAsync(() =>
        {
            if (_jevTestStatus != null && _jevTestStatus.IsInsideTree()) UiFactory.SetStatusLine(_jevTestStatus, result);
        });
    }
}
