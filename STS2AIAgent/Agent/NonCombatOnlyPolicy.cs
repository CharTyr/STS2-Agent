using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// Detects the ownership boundary for non-combat-only sessions. It accepts both compact state and
/// the action snapshot shape used immediately before dispatch. Unknown input fails closed so a
/// transient or malformed frame cannot leak an action into combat.
/// </summary>
internal static class NonCombatOnlyPolicy
{
    public static bool IsCombat(string? screen, bool inCombat) =>
        inCombat || string.Equals(screen, "COMBAT", StringComparison.OrdinalIgnoreCase);

    public static bool IsCombatSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return true;
            var state = root.TryGetProperty("state", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            var screen = state.TryGetProperty("screen", out var screenValue) && screenValue.ValueKind == JsonValueKind.String
                ? screenValue.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(screen)) return true;
            if (state.TryGetProperty("in_combat", out var malformedCombat)
                && malformedCombat.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return true;
            }

            var inCombat = state.TryGetProperty("in_combat", out var inCombatValue)
                && inCombatValue.ValueKind == JsonValueKind.True;
            return IsCombat(screen, inCombat);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}

/// <summary>The game entered combat after a decision started but before its action was dispatched.</summary>
internal sealed class NonCombatOnlyYieldException : Exception
{
    public AgentTurnResult? Receipt { get; set; }
}
