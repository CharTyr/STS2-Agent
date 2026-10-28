namespace STS2AIAgent.Agent;

/// <summary>The game's own speed levels (<c>FastModeType</c>), slowest first; None counts as slowest.</summary>
internal enum TurboLevel
{
    None = 0,
    Normal = 1,
    Fast = 2,
    Instant = 3
}

/// <summary>What to do with the game speed: <see cref="Target"/> null leaves it unchanged.</summary>
internal sealed record TurboDecision(TurboLevel? Target, string Reason);

/// <summary>
/// Decides the game speed while the AI auto-plays (W1). Pure, so the rules are tested offline; the
/// game-side half is <c>Game/TurboController.cs</c>.
/// </summary>
/// <remarks>
/// The rules, in order: the setting can be off; a multiplayer run is capped at Fast, because Instant
/// skips the pacing the human teammates read; a player who already plays at least as fast keeps their
/// setting (we never slow anyone down); and a restore only happens when the value is still the one we
/// set -- a player who changed the speed while the AI played keeps their change.
/// </remarks>
internal static class TurboPolicy
{
    public const string Off = "off";
    public const string FastMode = "fast";
    public const string InstantMode = "instant";

    public static string NormalizeMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            Off => Off,
            FastMode => FastMode,
            InstantMode => InstantMode,
            _ => InstantMode
        };
    }

    public static TurboDecision Decide(string? mode, bool isMultiplayer, TurboLevel current)
    {
        var normalized = NormalizeMode(mode);
        if (normalized == Off)
        {
            return new TurboDecision(null, "off");
        }

        var target = normalized == FastMode ? TurboLevel.Fast : TurboLevel.Instant;
        var capped = false;
        if (isMultiplayer && target > TurboLevel.Fast)
        {
            target = TurboLevel.Fast;
            capped = true;
        }

        if (current >= target)
        {
            return new TurboDecision(null, "already_fast");
        }

        return new TurboDecision(target, capped ? "multiplayer_cap" : "engage");
    }

    public static bool ShouldRestore(TurboLevel current, TurboLevel applied) => current == applied;

    public static string Format(TurboLevel level) => level switch
    {
        TurboLevel.Normal => "Normal",
        TurboLevel.Fast => "Fast",
        TurboLevel.Instant => "Instant",
        _ => "None"
    };

    public static TurboLevel? Parse(string? stored)
    {
        return stored?.Trim().ToLowerInvariant() switch
        {
            "none" => TurboLevel.None,
            "normal" => TurboLevel.Normal,
            "fast" => TurboLevel.Fast,
            "instant" => TurboLevel.Instant,
            _ => null
        };
    }
}
