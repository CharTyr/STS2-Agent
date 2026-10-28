namespace STS2AIAgent.Agent;

internal enum StallHint
{
    None,
    MainMenuIdle,
    WaitingForPlayer,
    SlowModel,
    GameNotResponding
}

internal enum StallPhase
{
    Other,
    ReadingState,
    WaitingForGame,
    RequestingModel,
    WaitingForPlayer
}

/// <summary>
/// A second line for the status when one stage of a turn lasts long (W8.3). The status already names
/// the stage ("waiting for the game", "requesting the model"); it did not say what the player could do
/// about it, so a slow model and a game left in the background looked the same: stuck.
/// </summary>
internal static class StallHintPolicy
{
    public static readonly TimeSpan MainMenuThreshold = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan PlayerThreshold = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ModelThreshold = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GameThreshold = TimeSpan.FromSeconds(25);

    public static StallHint Decide(StallPhase phase, TimeSpan elapsed, string? screen, bool autoPlayRunning)
    {
        if (!autoPlayRunning)
        {
            return StallHint.None;
        }

        if (string.Equals(screen?.Trim(), "MAIN_MENU", StringComparison.OrdinalIgnoreCase) && elapsed >= MainMenuThreshold)
        {
            return StallHint.MainMenuIdle;
        }

        return phase switch
        {
            StallPhase.WaitingForPlayer when elapsed >= PlayerThreshold => StallHint.WaitingForPlayer,
            StallPhase.RequestingModel when elapsed >= ModelThreshold => StallHint.SlowModel,
            StallPhase.WaitingForGame or StallPhase.ReadingState when elapsed >= GameThreshold => StallHint.GameNotResponding,
            _ => StallHint.None
        };
    }
}
