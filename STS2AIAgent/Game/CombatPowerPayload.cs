namespace STS2AIAgent.Game;

internal sealed class CombatPowerPayload
{
    public int index { get; init; }

    public string power_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int? amount { get; init; }

    // The native UI counter can differ from Amount. For Hardened Shell it is
    // the remaining damage budget this turn, not a count of remaining hits.
    // Null means the native DisplayAmount could not be read; never infer it.
    public int? display_amount { get; init; }

    public bool is_debuff { get; init; }

    public string? description { get; init; }
}

/// <summary>One of the Fool's arrow slots (v0.113.0). Lives here rather than in
/// GameStateService.Payloads.cs because this file is the second payloads source the
/// api-schema gate already reads, and the payloads file is at its line budget.</summary>
internal sealed class CombatArrowPayload
{
    public int index { get; init; }

    /// <summary>retrieved / away / destroyed.</summary>
    public string status { get; init; } = string.Empty;

    public bool available { get; init; }

    /// <summary>v0.113.0: impaled arrows marked to break rather than fly back.</summary>
    public bool destroy_after_impaled { get; init; }
}

/// <summary>The Fool's per-turn melody counters (v0.113.0): which song tags the player has already
/// played this turn. Melody cards combine when several tags were played. Lives here rather than in
/// GameStateService.Payloads.cs because this file is the second payloads source the api-schema gate
/// already reads, and the payloads file is at its line budget.</summary>
internal sealed class CombatMelodyPayload
{
    public bool wah_played { get; init; }

    public bool cho_played { get; init; }

    public bool ki_played { get; init; }

    /// <summary>Distinct tags played (0-3).</summary>
    public int melodies_this_turn { get; init; }

    /// <summary>Melody cards played this turn; a card with several tags still counts once.</summary>
    public int melody_cards_this_turn { get; init; }
}
