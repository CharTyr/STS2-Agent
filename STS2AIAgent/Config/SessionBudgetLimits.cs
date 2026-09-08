using System.Globalization;

namespace STS2AIAgent.Config;

internal readonly record struct SessionBudgetParse(bool Accepted, int? Value, string? Error);

internal static class SessionBudgetLimits
{
    public const string ResetStatsActionLabel = "重置本会话统计";
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
            return new SessionBudgetParse(false, null, InvalidInputMessage);
        }

        if (parsed < 0)
        {
            return new SessionBudgetParse(false, null, InvalidInputMessage);
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
            ? "打开「设置」→ 显示高级选项以提高上限，或点「重置本会话统计」后再点「继续游玩」。重置只清零本会话计数，不会改预算上限；普通暂停/继续不会清零。"
            : "自动游玩进行中不能清零统计。请先点「暂停队友」，再在「设置」→ 显示高级选项提高上限，或暂停后点「重置本会话统计」。暂停/继续不会清零累计。";
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
