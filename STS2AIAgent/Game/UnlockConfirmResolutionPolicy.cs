namespace STS2AIAgent.Game;

internal static class UnlockConfirmResolutionPolicy
{
    public static T? SelectUsable<T>(
        T? preferred,
        IEnumerable<T> fallbacks,
        Func<T, bool> isUsable)
        where T : class
    {
        if (preferred != null && isUsable(preferred))
        {
            return preferred;
        }

        return fallbacks.FirstOrDefault(isUsable);
    }

    public static string BuildProbeSignature(
        string screenType,
        ulong screenInstanceId,
        string source,
        string buttonType,
        string buttonPath,
        bool visible,
        bool enabled)
    {
        return $"{screenType}|{screenInstanceId}|{source}|{buttonType}|{buttonPath}|{visible}|{enabled}";
    }
}
