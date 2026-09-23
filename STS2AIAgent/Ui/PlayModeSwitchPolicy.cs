namespace STS2AIAgent.Ui;

/// <summary>Pure switch policy: neither visibility nor persistence changes until pause is confirmed.</summary>
internal static class PlayModeSwitchPolicy
{
    public static bool ShouldCommit(string currentMode, string requestedMode, bool pauseConfirmed)
    {
        return pauseConfirmed && (requestedMode is "solo" or "coop")
            && (currentMode is "solo" or "coop") && currentMode != requestedMode;
    }
}
