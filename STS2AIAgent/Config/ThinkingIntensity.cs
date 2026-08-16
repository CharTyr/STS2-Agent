namespace STS2AIAgent.Config;

internal enum ThinkingIntensity
{
    Off = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal static class ThinkingIntensityMap
{
    public static ThinkingIntensity Parse(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "0" => ThinkingIntensity.Off,
            "low" or "minimal" or "1" => ThinkingIntensity.Low,
            "high" or "max" or "3" => ThinkingIntensity.High,
            _ => ThinkingIntensity.Medium
        };
    }

    public static string ToApiValue(ThinkingIntensity intensity)
    {
        return intensity switch
        {
            ThinkingIntensity.Off => "off",
            ThinkingIntensity.Low => "low",
            ThinkingIntensity.High => "high",
            _ => "medium"
        };
    }

    public static string? ToReasoningEffort(ThinkingIntensity intensity)
    {
        return intensity switch
        {
            ThinkingIntensity.Off => null,
            ThinkingIntensity.Low => "low",
            ThinkingIntensity.High => "high",
            _ => "medium"
        };
    }

    public static string PromptSuffix(ThinkingIntensity intensity)
    {
        return intensity switch
        {
            ThinkingIntensity.Off => "Keep replies short. Do not show a long chain of thought.",
            ThinkingIntensity.Low => "Think briefly before acting. Prefer a short rationale.",
            ThinkingIntensity.High => "Think carefully and step by step before acting. Check overlays, indexes, and available_actions.",
            _ => "Think enough to pick a legal, high-value action. Recheck the latest state before acting."
        };
    }
}
