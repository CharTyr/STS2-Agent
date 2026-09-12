using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class CardPlayCounterPolicyTests
{
    public static void APlayStillInHandRollsBack()
    {
        Assert.True(CardPlayCounterPolicy.ShouldRollBack(
            playSettled: false,
            combatInProgress: true,
            cardStillInHand: true));
    }

    public static void ASettledPlayKeepsTheCounters()
    {
        Assert.False(CardPlayCounterPolicy.ShouldRollBack(
            playSettled: true,
            combatInProgress: true,
            cardStillInHand: true));
    }

    public static void LeftHandOrEndedCombatKeepsTheCounters()
    {
        Assert.False(CardPlayCounterPolicy.ShouldRollBack(
            playSettled: false,
            combatInProgress: true,
            cardStillInHand: false));

        Assert.False(CardPlayCounterPolicy.ShouldRollBack(
            playSettled: false,
            combatInProgress: false,
            cardStillInHand: true));
    }
}
