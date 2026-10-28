namespace STS2AIAgent.Config;

/// <summary>
/// The switches of the 2026-10-28 experience overhaul: game speed while the AI plays, forced moves
/// without a model call, whole-turn plans, combat arithmetic in the state, screenshots on demand,
/// the in-game intent layer, the pause hotkey and the run journal.
/// </summary>
/// <remarks>
/// Every switch defaults to on (the owner's decision): a file written by an older version has none
/// of these keys and therefore gets the new behaviour, and each one can be turned off in settings,
/// which restores the v0.17.0 behaviour for that part. A new switch here must also be copied by
/// <see cref="SettingsClone"/>, harvested by the settings form, and normalised below.
/// </remarks>
internal sealed partial class AgentSettings
{
    public const string TurboOff = "off";
    public const string TurboFast = "fast";
    public const string TurboInstant = "instant";

    public const string VisionOff = "off";
    public const string VisionOnDemand = "on_demand";
    public const string VisionEveryStep = "every_step";

    public const string HudOff = "off";
    public const string HudMinimal = "minimal";
    public const string HudFull = "full";

    /// <summary>
    /// Game speed while auto-play runs: <c>off</c>, <c>fast</c> (the game's own Fast Mode) or
    /// <c>instant</c> (the game's Instant mode). A multiplayer run is capped at <c>fast</c>.
    /// The player's own setting is restored when auto-play stops.
    /// </summary>
    public string TurboMode { get; set; } = TurboInstant;

    /// <summary>
    /// The game's speed setting as the player had it while our speed is applied
    /// (<c>Normal</c>/<c>Fast</c>/<c>Instant</c>/<c>None</c>), null otherwise. It is persisted so a
    /// crash while the AI played can still give the player back their setting on the next launch.
    /// Instance-local: never sent to a teammate.
    /// </summary>
    public string? TurboRestoreFastMode { get; set; }

    /// <summary>Moves with exactly one sensible answer are made without asking the model.</summary>
    public bool AutopilotEnabled { get; set; } = true;

    /// <summary>The model may plan the rest of a combat turn in one request.</summary>
    public bool TurnPlansEnabled { get; set; } = true;

    /// <summary>The compact state carries the combat arithmetic (incoming damage, per-target card damage).</summary>
    public bool CombatMathEnabled { get; set; } = true;

    /// <summary>When a play decision attaches a screenshot: <c>off</c>, <c>on_demand</c> or <c>every_step</c>.</summary>
    public string VisionMode { get; set; } = VisionOnDemand;

    /// <summary>The in-game intent layer drawn over the game: <c>off</c>, <c>minimal</c> (banner only) or <c>full</c>.</summary>
    public string HudMode { get; set; } = HudFull;

    /// <summary>The key that pauses or resumes auto-play. Empty means no key.</summary>
    public string PauseHotkey { get; set; } = "F7";

    /// <summary>Finished runs are recorded as facts and a short digest is given to the model.</summary>
    public bool RunJournalEnabled { get; set; } = true;

    private void NormalizeExperienceSettings()
    {
        TurboMode = NormalizeChoice(TurboMode, TurboInstant, TurboOff, TurboFast, TurboInstant);
        VisionMode = NormalizeChoice(VisionMode, VisionOnDemand, VisionOff, VisionOnDemand, VisionEveryStep);
        HudMode = NormalizeChoice(HudMode, HudFull, HudOff, HudMinimal, HudFull);
        PauseHotkey = PauseHotkey?.Trim() ?? string.Empty;
        TurboRestoreFastMode = TurboRestoreFastMode?.Trim() switch
        {
            "Normal" or "Fast" or "Instant" or "None" => TurboRestoreFastMode.Trim(),
            _ => null
        };
    }

    private static string NormalizeChoice(string? value, string fallback, params string[] allowed)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed != null && allowed.Contains(trimmed, StringComparer.Ordinal) ? trimmed : fallback;
    }
}
