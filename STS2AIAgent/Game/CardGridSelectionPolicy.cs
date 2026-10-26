namespace STS2AIAgent.Game;

internal static class CardGridSelectionPolicy
{
    public static CardGridSelectionMetadata BuildMetadata(
        bool isEnchantSelection,
        int nativeMinSelect,
        int maxSelect,
        int selectedCount,
        bool requiresConfirmation)
    {
        // NDeckEnchantSelectScreen.ConfirmSelection ignores an empty selection,
        // even when its native prefs and enabled preview button allow zero.
        // Its final count check still permits any nonempty subset in the native range.
        var minSelect = isEnchantSelection ? Math.Max(1, nativeMinSelect) : nativeMinSelect;
        return new CardGridSelectionMetadata(
            minSelect,
            maxSelect,
            selectedCount,
            requiresConfirmation,
            selectedCount >= minSelect && selectedCount <= maxSelect);
    }
}

internal readonly record struct CardGridSelectionMetadata(
    int MinSelect,
    int MaxSelect,
    int SelectedCount,
    bool RequiresConfirmation,
    bool CanConfirm);
