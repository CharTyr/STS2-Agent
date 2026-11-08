namespace STS2AIAgent.Agent;

/// <summary>
/// When a play request carries a screenshot (W6). A screenshot costs roughly 1–2k tokens and a capture
/// on the game thread; a vision-capable model used to get one on every single step even though the
/// compact state already describes the screen. "On demand" (the default) attaches one only when the
/// structured state may not be enough.
/// </summary>
internal static class VisionPolicy
{
    public const string Off = "off";
    public const string OnDemand = "on_demand";
    public const string EveryStep = "every_step";

    public static string NormalizeMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            Off => Off,
            EveryStep => EveryStep,
            "every-step" or "everystep" => EveryStep,
            _ => OnDemand
        };
    }

    public static bool ShouldAttach(string? mode, string? screen, bool lastTurnFailed, bool lastTurnTookNoAction, int consecutiveUnsettled) =>
        Reason(mode, screen, lastTurnFailed, lastTurnTookNoAction, consecutiveUnsettled) is not ("off" or "not_needed");

    /// <summary>The first rule that applies, as a stable id (for logs and tests).</summary>
    public static string Reason(string? mode, string? screen, bool lastTurnFailed, bool lastTurnTookNoAction, int consecutiveUnsettled)
    {
        switch (NormalizeMode(mode))
        {
            case Off:
                return "off";
            case EveryStep:
                return "every_step";
        }

        if (string.IsNullOrWhiteSpace(screen) || string.Equals(screen.Trim(), "UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return "unknown_screen";
        }

        if (lastTurnFailed)
        {
            return "last_turn_failed";
        }

        if (lastTurnTookNoAction)
        {
            return "no_action";
        }

        return consecutiveUnsettled >= 2 ? "unsettled" : "not_needed";
    }
}
