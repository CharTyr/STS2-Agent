namespace STS2AIAgent.Agent;

using STS2AIAgent.Localization;

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
        return ClassifyBody(message);
    }

    /// <summary>
    /// Prefers the kind a thrower stated over one derived from the message. Message matching is
    /// the fallback for errors that only exist as text (transport and model failures).
    /// </summary>
    public static string Resolve(string? explicitKind, string? message)
    {
        return string.IsNullOrEmpty(explicitKind) ? Classify(message) : explicitKind;
    }

    private static string ClassifyBody(string message)
    {
        // Budget stops come from SessionBudgetGuard, which states the kind itself. Matching bare
        // words here used to misread ordinary failures: a game error containing 上限 was reported as
        // a budget stop and advised the player to reset their session stats.
        // Only the run boundary's own messages mean the run ended. Matching the bare word "当前局"
        // also caught the retry message "检查当前局面后可手动继续", so three failed decisions were
        // reported as a finished run, with advice to start a new run from the main menu.
        //
        // The boundary's messages are shown to the player, so they arrive in the player's language;
        // Loc.T supplies the translated shape of the same constant and keeps both in sync.
        if (ContainsAny(
                message,
                CurrentRunBoundary.LeftRunMessage,
                Loc.T(CurrentRunBoundary.LeftRunMessage),
                CurrentRunBoundary.RunIdentityChangedMessage,
                Loc.T(CurrentRunBoundary.RunIdentityChangedMessage)))
        {
            return RunEnd;
        }

        // The messages behind these fragments are produced in the player's language, so both shapes
        // have to be recognised. Status codes need no translation.
        if (message.Contains("请检查模型", StringComparison.Ordinal) ||
            message.Contains("check the model", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("402", StringComparison.Ordinal) ||
            message.Contains("403", StringComparison.Ordinal) ||
            message.Contains("404", StringComparison.Ordinal) ||
            message.Contains("422", StringComparison.Ordinal) ||
            message.Contains("认证失败", StringComparison.Ordinal) ||
            message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("配置错误", StringComparison.Ordinal) ||
            message.Contains("configuration error", StringComparison.OrdinalIgnoreCase))
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

    private static bool ContainsAny(string message, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && message.Contains(candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
