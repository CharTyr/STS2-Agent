using System.Globalization;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Config;

internal readonly record struct SessionBudgetParse(bool Accepted, int? Value, string? Error);

internal static class SessionBudgetLimits
{
    /// <summary>
    /// The button that zeroes this session's counters so play can go on. "重置统计" read like wiping
    /// data; what the player wants at a budget stop is to keep playing.
    /// </summary>
    public const string ResetStatsActionLabel = "清零计数";
    public const string InvalidInputMessage = "会话预算必须是正整数；留空或 0 表示不限。已保留原来的安全上限。";

    public static SessionBudgetParse Parse(string? text)
    {
        var raw = text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return new SessionBudgetParse(true, null, null);
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return new SessionBudgetParse(false, null, Loc.T(InvalidInputMessage));
        }

        if (parsed < 0)
        {
            return new SessionBudgetParse(false, null, Loc.T(InvalidInputMessage));
        }

        return parsed == 0
            ? new SessionBudgetParse(true, null, null)
            : new SessionBudgetParse(true, parsed, null);
    }

    public static bool TryApply(string? text, int? currentSafeValue, out int? value, out string? error)
    {
        var parsed = Parse(text);
        if (!parsed.Accepted)
        {
            value = currentSafeValue;
            error = parsed.Error;
            return false;
        }

        value = parsed.Value;
        error = null;
        return true;
    }

    public static bool CanResetSessionStats(bool playRunning, string? playPhase)
    {
        if (playRunning)
        {
            return false;
        }

        return !string.Equals(playPhase, "running", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(playPhase, "stopping", StringComparison.OrdinalIgnoreCase);
    }

    public static string BudgetRecoveryNextAction(bool canReset)
    {
        return canReset
            ? Loc.T("点「清零计数」后再继续游玩；想一次玩更久，到「设置」调高「请求上限」。清零只重置本次会话的计数，不改上限。")
            : Loc.T("自动游玩还在收尾，暂停后才能点「清零计数」；也可以到「设置」调高「请求上限」。");
    }

    public static bool TryHarvestBudget(AgentSettings target, string? tokensText, string? requestsText, out string? error)
    {
        var tokens = target.MaxSessionTokens;
        var requests = target.MaxSessionRequests;
        string? tokenError = null;
        string? requestError = null;
        var okTokens = tokensText is null || TryApply(tokensText, target.MaxSessionTokens, out tokens, out tokenError);
        var okRequests = requestsText is null || TryApply(requestsText, target.MaxSessionRequests, out requests, out requestError);
        if (!okTokens || !okRequests)
        {
            error = string.Join(" ", new[] { tokenError, requestError }.Where(item => !string.IsNullOrWhiteSpace(item)));
            return false;
        }

        target.MaxSessionTokens = tokens;
        target.MaxSessionRequests = requests;
        error = null;
        return true;
    }

    public static AgentSettings Harvest(AgentSettings current, string? tokensText, string? requestsText, out string? error)
    {
        var copy = new AgentSettings
        {
            MaxSessionTokens = current.MaxSessionTokens,
            MaxSessionRequests = current.MaxSessionRequests
        };
        TryHarvestBudget(copy, tokensText, requestsText, out error);
        return copy;
    }
}
