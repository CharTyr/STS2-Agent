using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class CombatTurnReadinessPolicyTests
{
    public static void RejectsPreTurnEmptyHandEvenWhenButtonLooksReady()
    {
        Assert.False(CombatTurnReadinessPolicy.IsLocallyReady(0, 0, 0, true));
    }

    public static void KeepsOpeningDrawGuardWhenNoNativeReadyEvidenceExists()
    {
        Assert.False(CombatTurnReadinessPolicy.IsLocallyReady(1, 0, 0, false));
    }

    public static void AcceptsCardsOrRecordedPlayAsTurnEvidence()
    {
        Assert.True(CombatTurnReadinessPolicy.IsLocallyReady(1, 1, 0, false));
        Assert.True(CombatTurnReadinessPolicy.IsLocallyReady(1, 0, 1, false));
    }

    public static void RecoversEmptyHandWhenNativeEndTurnIsReady()
    {
        Assert.True(CombatTurnReadinessPolicy.IsLocallyReady(1, 0, 0, true));
    }
}
