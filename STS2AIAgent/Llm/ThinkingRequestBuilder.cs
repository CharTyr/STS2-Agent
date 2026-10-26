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
        // Infer only recognized model families, not arbitrary substrings in a proxy alias.
        // The endpoint protocol is the explicit authority for renamed/proxied models.
        name = name[(name.LastIndexOf('/') + 1)..];
        if (name == "deepseek" || name.StartsWith("deepseek-", StringComparison.Ordinal))
        {
            return "deepseek";
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?:gpt-5|step-5|o1|o3|o4)(?:[-.:]|$)"))
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
