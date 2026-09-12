namespace STS2AIAgent.Game;

/// <summary>
/// How a pending <c>resolve_rewards</c> card choice should be consumed.
/// </summary>
internal enum RewardChoiceKind
{
    Auto,
    Skip,
    Pick
}

/// <summary>
/// Result of resolving a pending card reward choice against the live option list.
/// <see cref="Index"/> is only meaningful when <see cref="IsValid"/> is true
/// (and is unused for <see cref="RewardChoiceKind.Skip"/>).
/// </summary>
internal readonly record struct RewardChoiceResolution(
    RewardChoiceKind Kind,
    int Index,
    bool IsValid,
    string? Reason);

/// <summary>
/// Pure decision layer for the pending card reward choice set by <c>resolve_rewards</c>.
/// An explicit index that does not exist must fail instead of silently picking the
/// first option, while "no choice given" keeps the documented first-card behavior.
/// </summary>
internal static class RewardChoicePolicy
{
    /// <summary>Pending value for an explicit skip request (<c>option_index: -1</c>).</summary>
    public const int SkipChoice = -2;

    /// <summary>Pending value for "no choice given": pick the first card reward.</summary>
    public const int AutoChoice = -1;

    public static RewardChoiceResolution Resolve(int pending, int optionCount)
    {
        if (pending == SkipChoice)
        {
            return new RewardChoiceResolution(RewardChoiceKind.Skip, -1, true, null);
        }

        if (pending == AutoChoice)
        {
            return optionCount > 0
                ? new RewardChoiceResolution(RewardChoiceKind.Auto, 0, true, null)
                : new RewardChoiceResolution(RewardChoiceKind.Auto, -1, false, "no card reward options");
        }

        return pending >= 0 && pending < optionCount
            ? new RewardChoiceResolution(RewardChoiceKind.Pick, pending, true, null)
            : new RewardChoiceResolution(RewardChoiceKind.Pick, pending, false, "option_index is out of range");
    }
}
