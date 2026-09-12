namespace STS2AIAgent.Game;

/// <summary>
/// Pure decision layer for menu-shaped transitions. An open modal is not proof that a
/// menu transition happened: it is itself the MODAL screen, so the wait must stay
/// unsettled and the caller is told which modal is blocking.
/// </summary>
internal static class MenuTransitionPolicy
{
    /// <summary>A transition away from a menu screen only settled when the screen really changed.</summary>
    public static bool IsMenuExited(bool menuScreenStillCurrent, bool modalOpen, bool resolvedScreenUnknown)
    {
        return !menuScreenStillCurrent && !modalOpen && !resolvedScreenUnknown;
    }

    /// <summary>Embark settles when the lobby reports ready, or when the screen transition actually happened.</summary>
    public static bool IsEmbarkSettled(
        bool multiplayerReady,
        bool menuScreenStillCurrent,
        bool modalOpen,
        bool resolvedScreenUnknown)
    {
        return multiplayerReady || IsMenuExited(menuScreenStillCurrent, modalOpen, resolvedScreenUnknown);
    }

    /// <summary>The character-select screen must be the current screen; an actionable modal does not count.</summary>
    public static bool IsCharacterSelectSettled(bool characterSelectScreenVisible, bool modalOpen)
    {
        return characterSelectScreenVisible && !modalOpen;
    }

    /// <summary>Truthful pending message that names the blocking modal when one is open.</summary>
    public static string DescribeUnsettled(string action, string? blockingModal)
    {
        return string.IsNullOrEmpty(blockingModal)
            ? $"{action}: the transition did not settle before the deadline."
            : $"{action}: blocked by the open {blockingModal}; run confirm_modal or dismiss_modal from the fresh state.";
    }
}
