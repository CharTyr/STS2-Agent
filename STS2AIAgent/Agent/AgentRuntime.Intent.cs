using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    private LiveIntent? _currentIntent;
    private string _intentPlanPreview = "";
    public LiveIntent? CurrentIntent { get { lock (_gate) return _currentIntent; } }
    public string IntentPlanPreview { get { lock (_gate) return _intentPlanPreview; } }

    private void ReportIntent(LiveIntent intent)
    {
        lock (_gate)
        {
            _currentIntent = intent;
            if (intent.PlanPreview.Length > 0) _intentPlanPreview = intent.PlanPreview;
        }
        RaiseChanged();
    }

    private void ClearIntent()
    {
        lock (_gate) { _currentIntent = null; _intentPlanPreview = ""; }
    }

    public string StallHintText(string? screen)
    {
        var phase = _status == PlayPhases.RequestingModel || _status == PlayPhases.AskingJev ? StallPhase.RequestingModel
            : _status == PlayPhases.ReadingState ? StallPhase.ReadingState
            : _status == PlayPhases.WaitingForGame ? StallPhase.WaitingForGame : StallPhase.Other;
        var elapsed = _phaseStartedAt == default ? TimeSpan.Zero : DateTimeOffset.UtcNow - _phaseStartedAt;
        return StallHintPolicy.Decide(phase, elapsed, screen, PlayRunning) switch
        {
            StallHint.MainMenuIdle => Loc.T("还没开始一局：AI 会自己选角出发，也可以你先开一局。"),
            StallHint.WaitingForPlayer => Loc.T("等你在自己的窗口里操作。"),
            StallHint.SlowModel => Loc.T("模型响应较慢，可以暂停后换模型。"),
            StallHint.GameNotResponding => Loc.T("游戏窗口可能在后台，请切回游戏并保持画面可见。"),
            _ => ""
        };
    }
}
