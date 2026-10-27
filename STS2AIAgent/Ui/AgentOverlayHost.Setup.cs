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
        _quickKey = UiFactory.Line(resolved?.Endpoint.ApiKey ?? "", "API Key", secret: true);
        _quickModel = UiFactory.Line(resolved?.Model.Model ?? "", Loc.T("模型名"));
        _quickUrl = UiFactory.Line(resolved?.Endpoint.BaseUrl ?? "", "https://example.com/v1");
        _quickUrl.Visible = QuickSetup.Providers[providerIndex].Id == "custom";
        _quickBudget = UiFactory.Line(settings.MaxSessionRequests?.ToString() ?? "", Loc.T("不限（留空或0）"));
        void Dirty() { if (!_rebuildingSettings) { _quickDirty = true; MarkSettingsDirty(); } }
        _quickKey.TextChanged += _ => Dirty();
        _quickModel.TextChanged += _ => Dirty();
        _quickUrl.TextChanged += _ => Dirty();
        _quickBudget.TextChanged += _ => MarkSettingsDirty();
        _quickProvider.ItemSelected += index =>
        {
            _quickUrl.Visible = QuickSetup.Providers[(int)index].Id == "custom";
            _quickKey.Text = "";
            _quickModel.Text = "";
            Dirty();
        };
        _quickDirty = false;
        _quickTestBadge = UiFactory.Wrapped("", 12, muted: false);
        ShowBadge(_quickTestBadge, QuickTestBadge());
        var rows = new List<Control>
        {
            Labeled(Loc.T("服务商"), _quickProvider), _quickUrl,
            Labeled("API Key", _quickKey), Labeled(Loc.T("模型"), _quickModel),
            Labeled(Loc.T("请求上限"), _quickBudget),
            _quickTestBadge
        };
        if (nextStep != null) rows.Add(nextStep);
        return HelpCard.Build(
            "settings.quick",
            Loc.T("快速配置"),
            Loc.T("选择服务商，填 API Key 和模型名，然后点「测试主模型」。\n测试会真实调用模型：先测连通，再测工具调用；不支持工具调用的模型无法自动游玩。\n对话和自动游玩共用这个模型；本地服务（Ollama / LM Studio）可留空 Key。\n请求上限：新安装默认试用 30 次，可调整。测试也会调用你的服务；请求数和 Token 不等于账单金额。"),
            rows.ToArray());
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
