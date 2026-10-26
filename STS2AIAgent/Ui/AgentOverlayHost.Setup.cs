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
    private Button? _cancelModelTest;
    private Button? _footerModelTest;
    private Button? _addEndpointButton;
    private Button? _addModelButton;

    private Control BuildQuickSetupCard(AgentSettings settings)
    {
        var resolved = settings.TryResolvePlayModel();
        var providerIndex = QuickSetup.FindProvider(resolved?.Endpoint.BaseUrl ?? "");
        _quickProvider = UiFactory.Combo();
        foreach (var p in QuickSetup.Providers) _quickProvider.AddItem(p.Name);
        _quickProvider.Select(providerIndex);
        _quickKey = UiFactory.Line(resolved?.Endpoint.ApiKey ?? "", "API Key", secret: true);
        _quickModel = UiFactory.Line(resolved?.Model.Model ?? "", Loc.T("服务商或本地模型名称"));
        _quickUrl = UiFactory.Line(resolved?.Endpoint.BaseUrl ?? "", "https://example.com/v1");
        _quickUrl.Visible = QuickSetup.Providers[providerIndex].Id == "custom";
        _quickBudget = UiFactory.Line(settings.MaxSessionRequests?.ToString() ?? "", Loc.T("留空或 0 表示不限"));
        void Dirty() { if (!_rebuildingSettings) { _quickDirty = true; MarkSettingsDirty(); } }
        _quickKey.TextChanged += _ => Dirty();
        _quickModel.TextChanged += _ => Dirty();
        _quickUrl.TextChanged += _ => Dirty();
        _quickBudget.TextChanged += _ => Dirty();
        _quickProvider.ItemSelected += index =>
        {
            _quickUrl.Visible = QuickSetup.Providers[(int)index].Id == "custom";
            _quickKey.Text = "";
            _quickModel.Text = "";
            Dirty();
        };
        _quickDirty = false;
        return UiFactory.Card(Loc.T("快速配置"),
            UiFactory.Wrapped(Loc.T("选择服务商，填写 API Key 和模型名称，然后检测。对话和自动游玩会共用这个模型；本地服务可留空 Key。")),
            Labeled(Loc.T("服务商"), _quickProvider), _quickUrl,
            Labeled("API Key", _quickKey), Labeled(Loc.T("模型"), _quickModel),
            Labeled(Loc.T("自动游玩请求上限"), _quickBudget),
            UiFactory.Wrapped(settings.MaxSessionRequests.HasValue
                ? Loc.T("新安装默认试用 30 次请求，可调整。检测也会调用你的服务；请求数和 Token 不等于账单金额。")
                : Loc.T("当前自动游玩不限请求次数。可填写上限；请求数和 Token 不等于账单金额。"), UiFactory.FontCaption));
    }

    private AgentSettings HarvestQuickSetup(AgentSettings settings)
    {
        if (_showAdvancedValue || !_quickDirty || _quickProvider == null) return settings;
        var copy = QuickSetup.Apply(settings, _quickProvider.Selected, _quickKey?.Text ?? "", _quickModel?.Text ?? "", _quickUrl?.Text ?? "");
        var budget = SessionBudgetLimits.Parse(_quickBudget?.Text);
        if (!budget.Accepted) throw new ArgumentException(budget.Error);
        copy.MaxSessionRequests = budget.Value;
        return copy;
    }

    private void RefreshModelTestControls()
    {
        foreach (var button in _modelTestButtons)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = _modelTestCancellation != null;
        if (_footerModelTest != null) _footerModelTest.Disabled = _modelTestCancellation != null;
        if (_cancelModelTest != null) _cancelModelTest.Disabled = _modelTestCancellation == null;
    }

    private async Task RunModelTestFromUiAsync(string? modelId)
    {
        if (_modelTestCancellation != null) return;
        SaveSettingsFromUi();
        if (!_lastSettingsSaveSucceeded) return;
        modelId ??= AgentRuntime.Instance.Settings.TryResolvePlayModel()?.Model.Id;
        if (modelId == null) { SetSaveStatus(FirstRunSetup.SettingsHint); return; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _modelTestCancellation = cancellation;
        RefreshModelTestControls();
        SetSaveStatus(Loc.T("正在检测连通与工具能力，最多 45 秒，通常 2 个请求。可点取消。"));
        string result;
        try
        {
            result = await AgentRuntime.Instance.TestModelAsync(modelId, cancellation.Token);
            await SaveSettingsAndSyncJevAsync();
        }
        catch (OperationCanceledException)
        {
            result = cancellation.IsCancellationRequested ? Loc.T("检测已取消或超时，配置已保留，可重试。") : Loc.T("检测已取消。");
        }
        catch (Exception ex) { result = DiagnosticExport.Redact(ex.Message); }
        finally { _modelTestCancellation = null; }
        if (_panel == null || !_panel.IsInsideTree()) return;
        RebuildSettingsForm();
        RefreshModelTestControls();
        SetSaveStatus(result);
    }
}
