namespace STS2AIAgent.Agent;

/// <summary>
/// Maps an auto-play stop message onto the kind reported by /health and the overlay. Pure logic:
/// no Godot, game or IO types, so the C# core test executable covers it without launching the game.
/// </summary>
internal static class StopKindPolicy
{
    public const string Budget = "budget";
    public const string RunEnd = "run_end";
    public const string Configuration = "config";
    public const string Network = "network";
    public const string Failed = "failed";

    public static string Classify(string? message)
    {
        message ??= string.Empty;
        if (message.Contains("预算", StringComparison.Ordinal) || message.Contains("上限", StringComparison.Ordinal))
        {
            return Budget;
        }

        // Only the run boundary's own messages mean the run ended. Matching the bare word "当前局"
        // also caught the retry message "检查当前局面后可手动继续", so three failed decisions were
        // reported as a finished run, with advice to start a new run from the main menu.
        if (message.Contains(CurrentRunBoundary.LeftRunMessage, StringComparison.Ordinal) ||
            message.Contains(CurrentRunBoundary.RunIdentityChangedMessage, StringComparison.Ordinal))
        {
            return RunEnd;
        }

        if (message.Contains("请检查模型", StringComparison.Ordinal) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("402", StringComparison.Ordinal) ||
            message.Contains("403", StringComparison.Ordinal) ||
            message.Contains("404", StringComparison.Ordinal) ||
            message.Contains("422", StringComparison.Ordinal) ||
            message.Contains("认证失败", StringComparison.Ordinal) ||
            message.Contains("配置错误", StringComparison.Ordinal))
        {
            return Configuration;
        }

        if (message.Contains("408", StringComparison.Ordinal) ||
            message.Contains("429", StringComparison.Ordinal) ||
            message.Contains("超时", StringComparison.Ordinal) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Name or service", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return Network;
        }

        return Failed;
    }
}
