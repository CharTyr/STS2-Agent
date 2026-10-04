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
}
