using STS2AIAgent.Config;

namespace STS2AIAgent.Llm;

internal static class ThinkingRequestBuilder
{
    public static ThinkingRequestFields Build(string modelName, string thinkingMode, ThinkingIntensity intensity)
    {
        var mode = string.IsNullOrWhiteSpace(thinkingMode) ? "auto" : thinkingMode.Trim().ToLowerInvariant();
        var resolved = mode switch
        {
            "reasoning_effort" or "openai" => "reasoning_effort",
            "deepseek" or "thinking" => "deepseek",
            "prompt" or "none" or "off" => "prompt",
            _ => InferMode(modelName)
        };

        if (intensity == ThinkingIntensity.Off)
        {
            return new ThinkingRequestFields(
                ReasoningEffort: null,
                DeepSeekThinking: resolved == "deepseek" ? new Dictionary<string, string> { ["type"] = "disabled" } : null,
                PromptSuffix: ThinkingIntensityMap.PromptSuffix(intensity));
        }

        return resolved switch
        {
            "reasoning_effort" => new ThinkingRequestFields(
                ReasoningEffort: ThinkingIntensityMap.ToReasoningEffort(intensity),
                DeepSeekThinking: null,
                PromptSuffix: ThinkingIntensityMap.PromptSuffix(intensity)),
            "deepseek" => new ThinkingRequestFields(
                ReasoningEffort: null,
                DeepSeekThinking: new Dictionary<string, string> { ["type"] = "enabled" },
                PromptSuffix: ThinkingIntensityMap.PromptSuffix(intensity)),
            _ => new ThinkingRequestFields(
                ReasoningEffort: null,
                DeepSeekThinking: null,
                PromptSuffix: ThinkingIntensityMap.PromptSuffix(intensity))
        };
    }

    public static string InferMode(string? modelName)
    {
        var name = modelName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Contains("deepseek", StringComparison.Ordinal) ||
            name.Contains("r1", StringComparison.Ordinal) && name.Contains("reason", StringComparison.Ordinal))
        {
            return "deepseek";
        }

        if (name.Contains("gpt-5", StringComparison.Ordinal) ||
            name.Contains("o1", StringComparison.Ordinal) ||
            name.Contains("o3", StringComparison.Ordinal) ||
            name.Contains("o4", StringComparison.Ordinal))
        {
            return "reasoning_effort";
        }

        return "prompt";
    }
}

internal sealed record ThinkingRequestFields(
    string? ReasoningEffort,
    Dictionary<string, string>? DeepSeekThinking,
    string PromptSuffix);
