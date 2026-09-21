namespace STS2AIAgent.Agent;

using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

internal sealed class SessionBudgetGuard
{
    private readonly object _gate = new();
    private int? _maxTokens;
    private int? _maxRequests;
    private int _consumedTokens;
    private int _requestCount;

    public int? MaxTokens
    {
        get { lock (_gate) return _maxTokens; }
    }

    public int? MaxRequests
    {
        get { lock (_gate) return _maxRequests; }
    }

    public int ConsumedTokens
    {
        get { lock (_gate) return _consumedTokens; }
    }

    public int RequestCount
    {
        get { lock (_gate) return _requestCount; }
    }

    public SessionBudgetGuard(int? maxTokens = null, int? maxRequests = null, int initialTokens = 0, int initialRequests = 0)
    {
        _maxTokens = maxTokens is > 0 ? maxTokens : null;
        _maxRequests = maxRequests is > 0 ? maxRequests : null;
        _consumedTokens = Math.Max(0, initialTokens);
        _requestCount = Math.Max(0, initialRequests);
    }

    public bool HasLimit
    {
        get
        {
            lock (_gate)
            {
                return _maxTokens.HasValue || _maxRequests.HasValue;
            }
        }
    }

    public void UpdateLimits(int? maxTokens, int? maxRequests)
    {
        lock (_gate)
        {
            _maxTokens = maxTokens is > 0 ? maxTokens : null;
            _maxRequests = maxRequests is > 0 ? maxRequests : null;
        }
    }

    public string? CheckBudget(int extraRequests = 0, int extraTokens = 0)
    {
        lock (_gate)
        {
            return CheckBudgetLocked(extraRequests, extraTokens);
        }
    }

    public string? Observe(AgentTurnResult result)
    {
        var tokens = result.Usage?.TotalTokens ?? 0;
        var requests = Math.Max(0, result.RequestsSpent);
        return Record(tokens, requests);
    }

    public string? Record(int tokensAdded, int requestsAdded)
    {
        lock (_gate)
        {
            _consumedTokens += tokensAdded;
            _requestCount += requestsAdded;
            return CheckBudgetLocked();
        }
    }

    private string? CheckBudgetLocked(int extraRequests = 0, int extraTokens = 0)
    {
        extraRequests = Math.Max(0, extraRequests);
        // Pending usage belongs to the current turn and is checked, not recorded here.
        var requests = (long)_requestCount + extraRequests;
        var tokens = (long)_consumedTokens + Math.Max(0, extraTokens);
        if (_maxRequests.HasValue && requests >= _maxRequests.Value)
        {
            return Loc.T("已达到会话请求次数上限（{0}/{1} 次），已自动停止游玩。", requests, _maxRequests.Value);
        }

        if (_maxTokens.HasValue && tokens >= _maxTokens.Value)
        {
            return Loc.T("已达到会话 Token 预算上限（{0:N0}/{1:N0} tokens），已自动停止游玩。", tokens, _maxTokens.Value);
        }

        return null;
    }
}
