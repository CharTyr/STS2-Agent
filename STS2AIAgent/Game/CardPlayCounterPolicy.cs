namespace STS2AIAgent.Game;

/// <summary>
/// Pure decision layer for the mid-turn card counters. The counters are incremented
/// optimistically before the play settles, so a play that never left the hand has to
/// be rolled back to keep the counters matching the table.
/// </summary>
internal static class CardPlayCounterPolicy
{
    public static bool ShouldRollBack(bool playSettled, bool combatInProgress, bool cardStillInHand)
    {
        return !playSettled && combatInProgress && cardStillInHand;
    }
}
