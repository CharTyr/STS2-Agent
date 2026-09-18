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

    /// <summary>
    /// The combat-rules FTUE lays its text out over several pages: a confirm click advances one page
    /// and leaves the modal open, so the caller has to keep confirming until the last page closes it.
    /// </summary>
    public static bool IsMultiPageFtue(string? typeName)
    {
        return IsCombatRulesFtue(typeName);
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

    public static bool ForceCloseIfStuck(string? modalTypeName)
    {
        return IsFtueType(modalTypeName) && !IsCombatRulesFtue(modalTypeName);
    }

    public static bool AdvanceWithConfirmButton(string? modalTypeName, bool hasUsableConfirmButton)
    {
        return IsCombatRulesFtue(modalTypeName) && hasUsableConfirmButton;
    }

    /// <summary>Parameterless close methods to try, in order, on a stuck FTUE modal.</summary>
    /// <remarks>
    /// Only <c>NFtue.CloseFtue()</c> -- the protected base method every FTUE inherits -- can match.
    /// The list used to lead with <c>CloseFtueAndEndTurn</c>, <c>CloseFtueAndOpenRug</c> and
    /// <c>MarkFtueAsComplete</c>. The first two take an <c>NButton</c> and the caller looks for a
    /// parameterless method, so they never matched (and the first was mapped to NCanPlayCardsFtue
    /// while the game declares it on NCannotPlayCardFtue); <c>MarkFtueAsComplete</c> lives on
    /// <c>SaveManager</c>, not on any modal. Checked against the installed sts2.dll on 2026-09-18.
    /// The one-argument <c>CloseFtue(NButton)</c> overloads are tried before this list, and
    /// <c>NModalContainer.Clear()</c> after it.
    /// </remarks>
    public static IReadOnlyList<string> CloseMethodNames(string? modalTypeName)
    {
        return IsCombatRulesFtue(modalTypeName) ? Array.Empty<string>() : new[] { "CloseFtue" };
    }
}

