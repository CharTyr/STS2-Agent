namespace STS2AIAgent.Game;

/// <summary>Keep a failed upgrade preview distinct from a fully upgraded card.</summary>
internal static class CardUpgradePreviewPolicy
{
    public static object? Build(Func<bool> isFullyUpgraded, Func<object> generate)
    {
        try
        {
            return isFullyUpgraded() ? null : generate();
        }
        catch
        {
            // Do not expose exception messages or pretend that a failed lookup means max level.
            return new { status = "unavailable", reason = "upgrade_preview_failed" };
        }
    }
}
