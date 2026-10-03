using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class CardGridSelectionPolicyTests
{
    public static void EmptyEnchantCannotConfirmEvenWithOptionalNativePrefs()
    {
        var metadata = Enchant(selectedCount: 0);
        Assert.Equal(1, metadata.MinSelect);
        Assert.Equal(3, metadata.MaxSelect);
        Assert.Equal(0, metadata.SelectedCount);
        Assert.True(metadata.RequiresConfirmation);
        Assert.False(metadata.CanConfirm);
    }

    public static void EnchantPermitsNonemptySubsetsWithoutRequiringMaximum()
    {
        foreach (var selectedCount in new[] { 1, 2, 3 })
        {
            var metadata = Enchant(selectedCount);
            Assert.Equal(1, metadata.MinSelect);
            Assert.Equal(3, metadata.MaxSelect);
            Assert.True(metadata.CanConfirm);
        }

        Assert.False(Enchant(selectedCount: 4).CanConfirm);
    }

    public static void EnchantKeepsStricterNativeMinimumAndImpossibleRange()
    {
        var tooFew = CardGridSelectionPolicy.BuildMetadata(true, 2, 3, 1, true);
        Assert.Equal(2, tooFew.MinSelect);
        Assert.False(tooFew.CanConfirm);
        Assert.True(CardGridSelectionPolicy.BuildMetadata(true, 2, 3, 2, true).CanConfirm);
        Assert.False(CardGridSelectionPolicy.BuildMetadata(true, 0, 0, 0, true).CanConfirm);
    }

    public static void OtherCardGridsKeepEmptyOptionalSelections()
    {
        var metadata = CardGridSelectionPolicy.BuildMetadata(false, 0, 3, 0, true);
        Assert.Equal(0, metadata.MinSelect);
        Assert.True(metadata.CanConfirm);
    }

    public static void NativeMetadataProbeUsesTheSelectionPolicy()
    {
        var source = AgentSourceFixture.ReadStateService();
        var body = string.Concat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool TryGetCardGridSelectionMetadata(").Where(c => !char.IsWhiteSpace(c)));
        Assert.Contains(
            "metadata=CardGridSelectionPolicy.BuildMetadata(currentScreenisNDeckEnchantSelectScreen,prefs.MinSelect,prefs.MaxSelect,selectedCount,prefs.RequireManualConfirmation)",
            body,
            StringComparison.Ordinal);
    }

    private static CardGridSelectionMetadata Enchant(int selectedCount) =>
        CardGridSelectionPolicy.BuildMetadata(true, 0, 3, selectedCount, true);
}
