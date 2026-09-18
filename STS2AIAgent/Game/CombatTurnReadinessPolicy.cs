namespace STS2AIAgent.Game;

/// <summary>
/// Resolves the narrow empty-hand ambiguity at the start/end of a player turn.
/// The mod-maintained play counter is useful evidence, but it can be reset by a
/// round-number transition after native card effects have already emptied the
/// hand.  In that case the enabled native end-turn button is authoritative.
/// </summary>
internal static class CombatTurnReadinessPolicy
{
    internal static bool IsLocallyReady(
        int turnNumber,
        int handCount,
        int cardsPlayedThisTurn,
        bool nativeEndTurnReady)
    {
        if (turnNumber <= 0)
        {
            return false;
        }

        return handCount > 0 || cardsPlayedThisTurn > 0 || nativeEndTurnReady;
    }
}
