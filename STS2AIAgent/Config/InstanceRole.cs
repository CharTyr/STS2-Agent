namespace STS2AIAgent.Config;

internal static class InstanceRole
{
    public const string Human = "human";
    public const string Companion = "companion";

    public static string Current
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("STS2_AGENT_ROLE");
            if (string.Equals(raw?.Trim(), Companion, StringComparison.OrdinalIgnoreCase))
            {
                return Companion;
            }

            return Human;
        }
    }

    public static bool IsCompanion => Current == Companion;
}
