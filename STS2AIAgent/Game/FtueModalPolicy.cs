namespace STS2AIAgent.Game;

internal static class FtueModalPolicy
{
    public const string CombatRulesTypeName = "NCombatRulesFtue";

    public static bool IsFtueType(string? typeName)
    {
        return !string.IsNullOrWhiteSpace(typeName) &&
               typeName.IndexOf("Ftue", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsCombatRulesFtue(string? typeName)
    {
        return string.Equals(typeName, CombatRulesTypeName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ExposeConfirm(string? modalTypeName, bool hasUsableConfirmButton)
    {
        return hasUsableConfirmButton || IsFtueType(modalTypeName);
    }

    public static bool CloseFtueDirectly(string? modalTypeName, bool hasUsableConfirmButton)
    {
        if (IsCombatRulesFtue(modalTypeName))
        {
            return false;
        }

        return !hasUsableConfirmButton && IsFtueType(modalTypeName);
    }

    public static bool AdvanceWithConfirmButton(string? modalTypeName, bool hasUsableConfirmButton)
    {
        return IsCombatRulesFtue(modalTypeName) && hasUsableConfirmButton;
    }

    public static IReadOnlyList<string> CloseMethodNames(string? modalTypeName)
    {
        if (string.Equals(modalTypeName, "NCanPlayCardsFtue", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "CloseFtueAndEndTurn", "CloseFtue", "MarkFtueAsComplete" };
        }

        if (string.Equals(modalTypeName, "NMerchantFtue", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "CloseFtueAndOpenRug", "CloseFtue", "MarkFtueAsComplete" };
        }

        if (IsCombatRulesFtue(modalTypeName))
        {
            return Array.Empty<string>();
        }

        return new[] { "CloseFtue", "MarkFtueAsComplete" };
    }
}

