using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>Persists completed turn facts separately from live autoplay UI updates.</summary>
internal sealed partial class AgentRuntime
{
    private void RecordTurnReceipt(AgentTurnResult result, bool recordBudget = false)
    {
        AccountTurn(result, recordBudget);
        if (!string.IsNullOrWhiteSpace(result.Acted))
        {
            RecordDecision(
                "agent_loop",
                result.Acted,
                result.Reasoning,
                result.StateFingerprint,
                result.RequestsSpent,
                result.Usage?.TotalTokens);
        }
    }

    private void AccountTurn(AgentTurnResult result, bool recordBudget = false)
    {
        lock (_gate)
        {
            if (result.Usage != null)
            {
                _sessionUsage = LlmUsage.Combine(_sessionUsage, result.Usage) ?? LlmUsage.Empty;
                _sessionUsageKnown = true;
            }

            _sessionRequests += Math.Max(0, result.RequestsSpent);
            if (recordBudget)
            {
                _budgetGuard.Observe(result);
            }
        }
    }

    private void ApplyPlayResult(AgentTurnResult result)
    {
        RecordTurnReceipt(result);
        _waitingForGame = result.WaitingForGame;
        _waitingForPlayer = result.WaitingForPlayer;
        _requestingModel = false;

        if (!string.IsNullOrWhiteSpace(result.Acted)) _lastAction = result.Acted;

        _lastThought = result.Reasoning ?? result.AssistantText ?? _lastThought;
        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            AddHistory("assistant", result.AssistantText);
        }

        if (result.RequiresConfiguration)
        {
            ClassifyStop(result.Error ?? Loc.T("配置错误"), ModelRoleNames.Play);
        }

        SetStatus(result.Error == null
            ? (result.Acted != null ? Loc.T("已执行 {0}", result.Acted) : result.WaitingForGame ? Loc.T("等待游戏可操作") : Loc.T("等待可操作状态"))
            : DiagnosticExport.Redact(result.Error));
    }
}
