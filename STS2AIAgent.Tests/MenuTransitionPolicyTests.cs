using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class MenuTransitionPolicyTests
{
    public static void ABlockingModalIsNotAMenuExit()
    {
        Assert.False(MenuTransitionPolicy.IsMenuExited(
            menuScreenStillCurrent: false,
            modalOpen: true,
            resolvedScreenUnknown: false));

        Assert.True(MenuTransitionPolicy.IsMenuExited(
            menuScreenStillCurrent: false,
            modalOpen: false,
            resolvedScreenUnknown: false));
    }

    public static void UnchangedOrUnknownScreenIsNotAMenuExit()
    {
        Assert.False(MenuTransitionPolicy.IsMenuExited(
            menuScreenStillCurrent: true,
            modalOpen: false,
            resolvedScreenUnknown: false));

        Assert.False(MenuTransitionPolicy.IsMenuExited(
            menuScreenStillCurrent: false,
            modalOpen: false,
            resolvedScreenUnknown: true));
    }

    public static void AModalDoesNotSettleASingleplayerEmbark()
    {
        Assert.False(MenuTransitionPolicy.IsEmbarkSettled(
            multiplayerReady: false,
            menuScreenStillCurrent: true,
            modalOpen: true,
            resolvedScreenUnknown: false));

        Assert.True(MenuTransitionPolicy.IsEmbarkSettled(
            multiplayerReady: false,
            menuScreenStillCurrent: false,
            modalOpen: false,
            resolvedScreenUnknown: false));
    }

    public static void LobbyReadyStaysSettled()
    {
        Assert.True(MenuTransitionPolicy.IsEmbarkSettled(
            multiplayerReady: true,
            menuScreenStillCurrent: true,
            modalOpen: true,
            resolvedScreenUnknown: true));
    }

    public static void CharacterSelectNeedsTheScreenItself()
    {
        Assert.True(MenuTransitionPolicy.IsCharacterSelectSettled(
            characterSelectScreenVisible: true,
            modalOpen: false));

        Assert.False(MenuTransitionPolicy.IsCharacterSelectSettled(
            characterSelectScreenVisible: false,
            modalOpen: false));

        Assert.False(MenuTransitionPolicy.IsCharacterSelectSettled(
            characterSelectScreenVisible: true,
            modalOpen: true));
    }

    public static void UnsettledMessageNamesTheBlockingModal()
    {
        var named = MenuTransitionPolicy.DescribeUnsettled("continue_run", "NErrorPopup");
        Assert.Contains("continue_run", named);
        Assert.Contains("NErrorPopup", named);

        var generic = MenuTransitionPolicy.DescribeUnsettled("embark", null);
        Assert.False(generic.Contains("modal", StringComparison.OrdinalIgnoreCase));
    }
}
