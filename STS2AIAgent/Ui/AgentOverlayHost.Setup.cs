using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

internal sealed partial class AgentOverlayHost
{
    private OptionButton? _quickProvider;
    private LineEdit? _quickKey;
    private LineEdit? _quickModel;
    private LineEdit? _quickUrl;
    private LineEdit? _quickBudget;
    /// <summary>Picks one of the provider's suggested models into the model field.</summary>
    private OptionButton? _quickModelPicker;
    private Button? _quickKeyLink;
    private int _quickProviderIndex;

    /// <summary>
    /// What the player typed for each provider while this form is open, so trying another provider and
    /// coming back does not lose the key. The card used to clear the key and model on every switch.
    /// Not saved: only the provider that is saved keeps its key in the settings file.
    /// </summary>
    private readonly Dictionary<string, (string Key, string Model, string Url)> _quickDrafts = new(StringComparer.Ordinal);
    private bool _quickDirty;
    private bool _lastSettingsSaveSucceeded;
    private CancellationTokenSource? _modelTestCancellation;
    private readonly List<Button> _modelTestButtons = new();
    private Button? _footerModelTest;
    /// <summary>Shown under the footer once a test leaves the main model ready, until the play tab is opened.</summary>
    private Button? _goPlayButton;
    private bool _offerGoPlay;
    /// <summary>The quick card's verdict line for the model it edits; null while the full form is shown.</summary>
    private Label? _quickTestBadge;
    private Button? _addEndpointButton;
    private Button? _addModelButton;

    private Control BuildQuickSetupCard(AgentSettings settings, Control? nextStep)
    {
        var resolved = settings.TryResolvePlayModel();
        var providerIndex = QuickSetup.FindProvider(resolved?.Endpoint.BaseUrl ?? "");
        _quickProvider = UiFactory.Combo();
        foreach (var p in QuickSetup.Providers) _quickProvider.AddItem(p.Name);
        _quickProvider.Select(providerIndex);
        _quickProviderIndex = providerIndex;
        _quickDrafts.Clear();
        _quickKey = UiFactory.Line(resolved?.Endpoint.ApiKey ?? "", "API Key", secret: true);
        _quickModel = UiFactory.Line(resolved?.Model.Model ?? "", Loc.T("模型名"));
        _quickUrl = UiFactory.Line(resolved?.Endpoint.BaseUrl ?? "", "https://example.com/v1");
        _quickUrl.Visible = QuickSetup.Providers[providerIndex].Id == "custom";
        _quickBudget = UiFactory.Line(settings.MaxSessionRequests?.ToString() ?? "", Loc.T("不限（留空或0）"));
        _quickModelPicker = UiFactory.Combo();
        _quickModelPicker.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _quickKeyLink = UiFactory.Button(Loc.T("获取 Key"), OpenQuickKeyLink, UiFactory.ButtonKind.Ghost);
        void Dirty() { if (!_rebuildingSettings) { _quickDirty = true; MarkSettingsDirty(); } }
        // Assigning a LineEdit's text from code raises no TextChanged, so the pick marks the form itself.
        _quickModelPicker.ItemSelected += index =>
        {
            if (_quickModel == null || _quickModelPicker == null || index < 0) return;
            _quickModel.Text = _quickModelPicker.GetItemText((int)index);
            Dirty();
        };
        _quickKey.TextChanged += _ => Dirty();
        _quickModel.TextChanged += _ => Dirty();
        _quickUrl.TextChanged += _ => Dirty();
        _quickBudget.TextChanged += _ => MarkSettingsDirty();
        _quickProvider.ItemSelected += index => SwitchQuickProvider((int)index, Dirty);
        RefreshQuickPresetExtras();
        _quickDirty = false;
        _quickTestBadge = UiFactory.Wrapped("", 12, muted: false);
        ShowBadge(_quickTestBadge, QuickTestBadge());
        var rows = new List<Control>
        {
            Labeled(Loc.T("服务商"), _quickProvider), _quickUrl,
            Labeled("API Key", _quickKey), UiFactory.Row(_quickKeyLink),
            Labeled(Loc.T("模型"), UiFactory.Row(_quickModel, _quickModelPicker)),
            Labeled(Loc.T("请求上限"), _quickBudget),
            _quickTestBadge
        };
        if (nextStep != null) rows.Add(nextStep);
        return HelpCard.Build(
            "settings.quick",
            Loc.T("快速配置"),
            Loc.T("选择服务商，填 API Key，模型已按服务商填好常用的一个，可从右边下拉换。点「保存设置」会自动测试一次。\n测试会真实调用模型：先测连通，再测工具调用；不支持工具调用的模型无法自动游玩。\n对话和自动游玩共用这个模型；本地服务（Ollama / LM Studio）可留空 Key，模型名填你本地已下载的。\n模型列表按各家文档整理，以服务商实际提供的为准。\n请求上限：新安装默认 1500 次，大约够打完一整局；留空或 0 为不限。测试也会调用你的服务；请求数和 Token 不等于账单金额。"),
            rows.ToArray());
    }

    /// <summary>
    /// Moves the quick card to another provider: files what was typed under the old one, then shows the
    /// new one's own draft, or its default model and an empty key when it has none yet.
    /// </summary>
    private void SwitchQuickProvider(int index, Action markDirty)
    {
        if (_quickKey == null || _quickModel == null || _quickUrl == null) return;
        index = Math.Clamp(index, 0, QuickSetup.Providers.Length - 1);
        var previous = QuickSetup.Providers[Math.Clamp(_quickProviderIndex, 0, QuickSetup.Providers.Length - 1)];
        _quickDrafts[previous.Id] = (_quickKey.Text, _quickModel.Text, _quickUrl.Text);
        _quickProviderIndex = index;
        var next = QuickSetup.Providers[index];
        (string Key, string Model, string Url) draft = _quickDrafts.TryGetValue(next.Id, out var saved)
            ? saved
            : (string.Empty, next.DefaultModel, string.Empty);
        _quickKey.Text = draft.Key;
        _quickModel.Text = draft.Model;
        _quickUrl.Text = draft.Url;
        _quickUrl.Visible = next.Id == "custom";
        RefreshQuickPresetExtras();
        markDirty();
    }

    /// <summary>Shows the provider's suggested models and its key link, or hides them where it has none.</summary>
    private void RefreshQuickPresetExtras()
    {
        var preset = QuickSetup.Providers[Math.Clamp(_quickProviderIndex, 0, QuickSetup.Providers.Length - 1)];
        if (_quickModelPicker != null && GodotObject.IsInstanceValid(_quickModelPicker))
        {
            _quickModelPicker.Clear();
            foreach (var model in preset.Models) _quickModelPicker.AddItem(model);
            _quickModelPicker.Select(Array.FindIndex(preset.Models, m => string.Equals(m, _quickModel?.Text.Trim(), StringComparison.Ordinal)));
            _quickModelPicker.Visible = preset.Models.Length > 1;
        }

        if (_quickKeyLink != null && GodotObject.IsInstanceValid(_quickKeyLink))
            _quickKeyLink.Visible = preset.KeyUrl.Length > 0;
    }

    private void OpenQuickKeyLink()
    {
        var url = QuickSetup.Providers[Math.Clamp(_quickProviderIndex, 0, QuickSetup.Providers.Length - 1)].KeyUrl;
        if (url.Length > 0) OS.ShellOpen(url);
    }

    /// <summary>
    /// The footer's Save. A changed provider, key or model on the quick card is tested straight away:
    /// the play page will not start until the model is verified, and a player who pressed Save and went
    /// to play used to find "模型还没测试" waiting there.
    /// </summary>
    private void SaveSettingsFromFooter()
    {
        var retest = !_showAdvancedValue && _quickDirty;
        SaveSettingsFromUi();
        if (retest && _lastSettingsSaveSucceeded && _modelTestCancellation == null)
        {
            _ = TestConnectionAsync();
        }
    }

    /// <summary>
    /// The quick card's verdict: the main model's test badge, or a nudge to fill the card in. While
    /// the provider, key, URL or model has unsaved edits, the saved verdict belongs to the old values,
    /// so the line asks for a retest instead of showing it.
    /// </summary>
    private (string Text, UiFactory.ButtonKind Tone) QuickTestBadge()
    {
        if (_quickDirty) return (Loc.T("已修改，需重新测试"), UiFactory.ButtonKind.Ghost);
        var modelId = AgentRuntime.Instance.Settings.TryResolvePlayModel()?.Model.Id;
        return modelId == null ? (Loc.T("未配置"), UiFactory.ButtonKind.Ghost) : ModelTestBadge(modelId);
    }

    /// <summary>A verdict line: its text, in its tone's colour.</summary>
    private static void ShowBadge(Label badge, (string Text, UiFactory.ButtonKind Tone) verdict)
    {
        badge.Text = verdict.Text;
        UiFactory.SetTone(badge, verdict.Tone);
    }

    private AgentSettings HarvestQuickSetup(AgentSettings settings)
    {
        if (_showAdvancedValue || _quickProvider == null) return settings;
        var copy = _quickDirty
            ? QuickSetup.Apply(settings, _quickProvider.Selected, _quickKey?.Text ?? "", _quickModel?.Text ?? "", _quickUrl?.Text ?? "")
            : settings;
        var budget = SessionBudgetLimits.Parse(_quickBudget?.Text);
        if (!budget.Accepted) throw new ArgumentException(budget.Error);
        copy.MaxSessionRequests = budget.Value;
        return copy;
    }

    /// <summary>The footer's test button: starts a main-model test, or cancels the one running.</summary>
    private void ToggleFooterModelTest()
    {
        if (_modelTestCancellation != null)
        {
            _modelTestCancellation.Cancel();
            return;
        }

        _ = TestConnectionAsync();
    }

    private void RefreshModelTestControls()
    {
        var testing = _modelTestCancellation != null;
        foreach (var button in _modelTestButtons)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = testing;
        if (_footerModelTest != null) _footerModelTest.Text = testing ? Loc.T("取消测试") : Loc.T("测试主模型");
        // Readiness is re-read rather than trusted from the test: a save after a passing test can
        // invalidate the verification (a new key, a new model), and the offer must go with it.
        if (_goPlayButton != null)
            _goPlayButton.Visible = _offerGoPlay && !testing && FirstRunSetup.Evaluate(AgentRuntime.Instance.Settings).ReadyToInvite;
    }

    private async Task RunModelTestFromUiAsync(string? modelId)
    {
        if (_modelTestCancellation != null) return;
        _offerGoPlay = false;
        SaveSettingsFromUi();
        if (!_lastSettingsSaveSucceeded) return;
        modelId ??= AgentRuntime.Instance.Settings.TryResolvePlayModel()?.Model.Id;
        if (modelId == null) { SetSaveStatus(FirstRunSetup.SettingsHint); return; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _modelTestCancellation = cancellation;
        RefreshModelTestControls();
        SetSaveStatus(Loc.T("正在测试…（最多 45 秒）"));
        string result;
        var completed = false;
        try
        {
            result = await AgentRuntime.Instance.TestModelAsync(modelId, cancellation.Token);
            completed = true;
            // Companion pause/ack has its own status and deadline. Do not extend the model test's
            // 45-second cancellation UI while a teammate finishes an already-submitted action.
            _ = SaveSettingsAndSyncJevAsync();
        }
        catch (OperationCanceledException)
        {
            result = cancellation.IsCancellationRequested ? Loc.T("已取消或超时，配置已保留。") : Loc.T("已取消。");
        }
        catch (Exception ex) { result = DiagnosticExport.Redact(ex.Message); }
        finally { _modelTestCancellation = null; }
        // Only a test that ran to the end offers the jump; a cancelled or crashed retest of a model that
        // was already verified did not just pass anything.
        _offerGoPlay = completed && FirstRunSetup.Evaluate(AgentRuntime.Instance.Settings).ReadyToInvite;
        if (_panel == null || !_panel.IsInsideTree()) return;
        // A rebuild redraws every badge and the first-run lines, but it also resets every editor to
        // the saved settings. Edits typed while the test ran (it can take 45 seconds) would be lost,
        // so a form with unsaved changes only has its badges updated in place.
        if (_settingsDirty)
        {
            RefreshModelTestBadges();
        }
        else
        {
            RebuildSettingsForm();
        }

        RefreshModelTestControls();
        SetSaveStatus(result);
    }

    /// <summary>
    /// The badge line under a model card: the recorded verdict, or the nudge to test. The tone is the
    /// colour the line is drawn in -- it used to be an emoji at the front of the text, drawn from a
    /// fallback font at its own size.
    /// </summary>
    private static (string Text, UiFactory.ButtonKind Tone) ModelTestBadge(string modelId)
    {
        var record = AgentRuntime.Instance.ModelTestFor(modelId);
        if (record == null)
        {
            return (Loc.T("未测试"), UiFactory.ButtonKind.Ghost);
        }

        if (!AgentRuntime.Instance.IsModelVerified(modelId))
        {
            return record.Status switch
            {
                "verified" => (Loc.T("已修改，需重新测试"), UiFactory.ButtonKind.Ghost),
                // A failed test must not look like "never tested": the player just pressed the
                // button and needs the reason on the card, not in a line the rebuild erases.
                "failed" => (Loc.T("测试失败（{0}）：{1}", ShortTime(record.TestedAt), record.Error ?? ""), UiFactory.ButtonKind.Danger),
                _ => (Loc.T("未测试"), UiFactory.ButtonKind.Ghost)
            };
        }

        return record.Tools == "supported"
            ? (Loc.T("已验证（{0}）", ShortTime(record.TestedAt)), UiFactory.ButtonKind.Primary)
            : (Loc.T("不支持工具调用，无法自动游玩（{0}）", ShortTime(record.TestedAt)), UiFactory.ButtonKind.Normal);
    }

    private static string ShortTime(string? iso)
    {
        return DateTimeOffset.TryParse(iso, out var at) ? at.ToLocalTime().ToString("HH:mm") : "";
    }

    private Task TestModelFromUiAsync(string modelId) => RunModelTestFromUiAsync(modelId);

    /// <summary>Re-reads every model card's test badge, and the quick card's, in place.</summary>
    private void RefreshModelTestBadges()
    {
        if (_quickTestBadge != null && GodotObject.IsInstanceValid(_quickTestBadge))
        {
            ShowBadge(_quickTestBadge, QuickTestBadge());
        }

        foreach (var (modelId, badge) in _modelTestBadges)
        {
            if (GodotObject.IsInstanceValid(badge))
            {
                ShowBadge(badge, ModelTestBadge(modelId));
            }
        }
    }

    /// <summary>The settings page's state line: reused for the per-model test's progress.</summary>
    private void SetSaveStatus(string text)
    {
        if (_saveStatus != null)
        {
            _saveStatus.Text = text;
        }
    }
}
