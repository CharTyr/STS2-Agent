namespace STS2AIAgent.Multiplayer;

internal static class CompanionActPolicy
{
    public static bool Allows(
        string? action,
        bool isCompanion,
        bool actorIsLocal,
        string? requestedPlayerId = null,
        string? localPlayerId = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        if (!isCompanion)
        {
            return true;
        }

        if (!actorIsLocal)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedPlayerId) &&
            !string.IsNullOrWhiteSpace(localPlayerId) &&
            !string.Equals(requestedPlayerId, localPlayerId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
