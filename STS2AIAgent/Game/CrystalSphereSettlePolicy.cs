namespace STS2AIAgent.Game;

internal static class CrystalSphereSettlePolicy
{
    public static bool IsSettled(
        bool screenChanged,
        bool minigameAvailable,
        int divinationsBefore,
        int divinationsNow,
        bool isFinished,
        bool canProceed)
    {
        if (screenChanged || canProceed)
        {
            // Rewards can push a child screen. On the final divination the
            // minigame entity may disappear before the proceed button is read.
            return true;
        }

        if (!minigameAvailable)
        {
            // A transient reflection or node-read failure is not proof that the
            // action settled; keep waiting so callers receive pending on timeout.
            return false;
        }

        if (isFinished)
        {
            return false;
        }

        return divinationsNow < divinationsBefore;
    }
}
