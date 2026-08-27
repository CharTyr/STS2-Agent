using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class CrystalSphereSettlePolicyTests
{
    public static void RequiresObservedProgressOnSameScreen()
    {
        Assert.False(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: true,
            divinationsBefore: 3,
            divinationsNow: 3,
            isFinished: false,
            canProceed: false));

        Assert.True(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: true,
            divinationsBefore: 3,
            divinationsNow: 2,
            isFinished: false,
            canProceed: false));
    }

    public static void WaitsForProceedAfterFinalDivination()
    {
        Assert.False(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: true,
            divinationsBefore: 1,
            divinationsNow: 0,
            isFinished: true,
            canProceed: false));

        Assert.True(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: true,
            divinationsBefore: 1,
            divinationsNow: 0,
            isFinished: true,
            canProceed: true));

        Assert.True(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: false,
            divinationsBefore: 1,
            divinationsNow: 1,
            isFinished: false,
            canProceed: true));
    }

    public static void AcceptsChildScreenButNotMissingMinigame()
    {
        Assert.True(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: true,
            minigameAvailable: false,
            divinationsBefore: 1,
            divinationsNow: 1,
            isFinished: false,
            canProceed: false));

        Assert.False(CrystalSphereSettlePolicy.IsSettled(
            screenChanged: false,
            minigameAvailable: false,
            divinationsBefore: 1,
            divinationsNow: 1,
            isFinished: false,
            canProceed: false));
    }
}
