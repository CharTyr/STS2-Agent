namespace STS2AIAgent.Config;

/// <summary>
/// Three-tier one-click play-style presets so players do not have to hand-tune eight separate
/// experience switches. Applies only play-experience parameters and never modifies endpoints,
/// models, API keys, hotkeys, reply language or player preference notes.
/// </summary>
internal static class PlayStylePresetPolicy
{
    public const string Economy = "economy";
    public const string Balanced = "balanced";
    public const string Push = "push";

    public static readonly string[] AllPresets = { Economy, Balanced, Push };

    public static bool Apply(AgentSettings settings, string preset)
    {
        if (settings == null || string.IsNullOrWhiteSpace(preset))
        {
            return false;
        }

        switch (preset.Trim().ToLowerInvariant())
        {
            case Economy:
                settings.TurboMode = AgentSettings.TurboInstant;
                settings.AutopilotEnabled = true;
                settings.TurnPlansEnabled = true;
                settings.CombatMathEnabled = true;
                settings.VisionMode = AgentSettings.VisionOff;
                settings.RunJournalEnabled = true;
                settings.SpectatorHighlightMs = 0;
                settings.MaxOutputTokens = 4096;
                settings.EnsureValidShape();
                return true;

            case Balanced:
                settings.TurboMode = AgentSettings.TurboInstant;
                settings.AutopilotEnabled = true;
                settings.TurnPlansEnabled = true;
                settings.CombatMathEnabled = true;
                settings.VisionMode = AgentSettings.VisionOnDemand;
                settings.RunJournalEnabled = true;
                settings.SpectatorHighlightMs = 0;
                settings.MaxOutputTokens = AgentSettings.DefaultMaxOutputTokens;
                settings.EnsureValidShape();
                return true;

            case Push:
                settings.TurboMode = AgentSettings.TurboFast;
                settings.AutopilotEnabled = true;
                settings.TurnPlansEnabled = true;
                settings.CombatMathEnabled = true;
                settings.VisionMode = AgentSettings.VisionOnDemand;
                settings.HudMode = AgentSettings.HudFull;
                settings.RunJournalEnabled = true;
                settings.SpectatorHighlightMs = 400;
                settings.MaxOutputTokens = AgentSettings.DefaultMaxOutputTokens;
                settings.EnsureValidShape();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Returns which preset the settings currently match, or null if customized.</summary>
    public static string? Detect(AgentSettings settings)
    {
        if (settings == null)
        {
            return null;
        }

        if (!settings.AutopilotEnabled || !settings.TurnPlansEnabled
            || !settings.CombatMathEnabled || !settings.RunJournalEnabled)
        {
            return null;
        }

        if (settings.TurboMode == AgentSettings.TurboInstant
            && settings.VisionMode == AgentSettings.VisionOff
            && settings.SpectatorHighlightMs == 0
            && settings.MaxOutputTokens == 4096)
        {
            return Economy;
        }

        if (settings.TurboMode == AgentSettings.TurboInstant
            && settings.VisionMode == AgentSettings.VisionOnDemand
            && settings.SpectatorHighlightMs == 0
            && settings.MaxOutputTokens == AgentSettings.DefaultMaxOutputTokens)
        {
            return Balanced;
        }

        if (settings.TurboMode == AgentSettings.TurboFast
            && settings.VisionMode == AgentSettings.VisionOnDemand
            && settings.HudMode == AgentSettings.HudFull
            && settings.SpectatorHighlightMs == 400
            && settings.MaxOutputTokens == AgentSettings.DefaultMaxOutputTokens)
        {
            return Push;
        }

        return null;
    }
}
