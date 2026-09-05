namespace STS2AIAgent.Agent;

using STS2AIAgent.Llm;

internal sealed class SessionBudgetGuard
{
    public int? MaxTokens { get; }

    public int? MaxRequests { get; }

    public int ConsumedTokens { get; private set; }

    public int RequestCount { get; private set; }

    public SessionBudgetGuard(int? maxTokens = null, int? maxRequests = null, int initialTokens = 0, int initialRequests = 0)
    {
        MaxTokens = maxTokens is > 0 ? maxTokens : null;
        MaxRequests = maxRequests is > 0 ? maxRequests : null;
        ConsumedTokens = Math.Max(0, initialTokens);
        RequestCount = Math.Max(0, initialRequests);
    }

    public bool HasLimit => MaxTokens.HasValue || MaxRequests.HasValue;

    public string? CheckBudget(int extraRequests = 0)
    {
        extraRequests = Math.Max(0, extraRequests);
        var requests = RequestCount + extraRequests;
        if (MaxRequests.HasValue && requests >= MaxRequests.Value)
        {
            return $"已达到会话请求次数上限（{requests}/{MaxRequests.Value} 次），已自动停止游玩。";
        }

        if (MaxTokens.HasValue && ConsumedTokens >= MaxTokens.Value)
        {
            return $"已达到会话 Token 预算上限（{ConsumedTokens:N0}/{MaxTokens.Value:N0} tokens），已自动停止游玩。";
        }

        return null;
    }

    public string? Observe(AgentTurnResult result)
    {
        var tokens = result.Usage?.TotalTokens ?? 0;
        var requests = result.RequestsSpent > 0 ? result.RequestsSpent : (result.WaitingForGame ? 0 : 1);
        return Record(tokens, requests);
    }

    public string? Record(int tokensAdded, int requestsAdded)
    {
        ConsumedTokens += tokensAdded;
        RequestCount += requestsAdded;
        return CheckBudget();
    }
}
