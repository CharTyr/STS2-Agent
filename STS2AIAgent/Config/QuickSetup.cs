using STS2AIAgent.Localization;

namespace STS2AIAgent.Config;

/// <summary>
/// One provider the quick card offers: its OpenAI-compatible base URL, the thinking protocol its
/// endpoint gets, the models to suggest (the first is filled in when the provider is picked), and
/// where to get a key. <see cref="Models"/> and <see cref="KeyUrl"/> may be empty.
/// </summary>
internal sealed record ProviderPreset(string Id, string Name, string BaseUrl, string Protocol, string[] Models, string KeyUrl)
{
    /// <summary>The model filled in when this provider is picked, or empty when there is no sensible default.</summary>
    public string DefaultModel => Models.Length > 0 ? Models[0] : string.Empty;
}

internal static class QuickSetup
{
    /// <summary>
    /// The providers the quick card lists, most-used first.
    /// </summary>
    /// <remarks>
    /// The card used to list DeepSeek, OpenAI and SiliconFlow and nothing else, with an empty model
    /// field and no link for a key, so a player on Zhipu, Kimi or Qwen had to pick "Custom" and type
    /// a base URL they had to look up (the r3 live pass did exactly that for GLM). Models are
    /// suggestions taken from each provider's documentation at the time of writing; the field stays
    /// editable, because providers rename models faster than this list can follow. Local runtimes
    /// suggest nothing: their model name is whatever the player pulled.
    /// </remarks>
    public static readonly ProviderPreset[] Providers =
    {
        new("deepseek", "DeepSeek", "https://api.deepseek.com", "deepseek",
            new[] { "deepseek-chat", "deepseek-reasoner" }, "https://platform.deepseek.com/api_keys"),
        new("zhipu", "智谱 GLM / Zhipu", "https://open.bigmodel.cn/api/paas/v4", "prompt",
            new[] { "glm-4.6", "glm-4.5-air" }, "https://open.bigmodel.cn/usercenter/apikeys"),
        new("moonshot", "Kimi / Moonshot", "https://api.moonshot.cn/v1", "prompt",
            new[] { "kimi-k2-0905-preview", "kimi-k2-turbo-preview" }, "https://platform.moonshot.cn/console/api-keys"),
        new("qwen", "通义千问 / Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", "prompt",
            new[] { "qwen-plus", "qwen-max", "qwen-turbo" }, ""),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "prompt",
            new[] { "deepseek/deepseek-chat" }, "https://openrouter.ai/keys"),
        new("openai", "OpenAI API", "https://api.openai.com/v1", "auto",
            new[] { "gpt-5-mini", "gpt-5" }, "https://platform.openai.com/api-keys"),
        new("siliconflow", "硅基流动 / SiliconFlow", "https://api.siliconflow.cn/v1", "prompt",
            new[] { "deepseek-ai/DeepSeek-V3" }, "https://cloud.siliconflow.cn/account/ak"),
        new("ollama", "Ollama", "http://127.0.0.1:11434/v1", "prompt", Array.Empty<string>(), ""),
        new("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", "prompt", Array.Empty<string>(), ""),
        new("custom", "自定义 / Custom", "", "auto", Array.Empty<string>(), "")
    };

    /// <summary>The index of the provider with this id; the custom entry when there is none.</summary>
    public static int IndexOf(string id)
    {
        var index = Array.FindIndex(Providers, p => string.Equals(p.Id, id, StringComparison.Ordinal));
        return index >= 0 ? index : Providers.Length - 1;
    }

    public static int FindProvider(string url)
    {
        var index = Array.FindIndex(Providers,
            p => p.BaseUrl.Length > 0 && p.BaseUrl.TrimEnd('/').Equals(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : Providers.Length - 1;
    }

    public static AgentSettings Apply(AgentSettings original, int providerIndex, string key, string modelName, string customUrl)
    {
        var provider = Providers[Math.Clamp(providerIndex, 0, Providers.Length - 1)];
        var url = provider.Id == "custom" ? customUrl.Trim() : provider.BaseUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException(Loc.T("请填写完整的 HTTP(S) 接口地址。"));
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException(Loc.T("请填写模型名。"));
        var copy = SettingsClone.Clone(original);
        var model = copy.Models.Count == 1 ? copy.Models[0] : copy.FindModel("quick-start-model");
        if (model == null)
        {
            var endpointId = "quick-start";
            for (var suffix = 2; copy.FindEndpoint(endpointId) != null; suffix++) endpointId = "quick-start-" + suffix;
            copy.Endpoints.Add(new LlmEndpoint { Id = endpointId });
            model = new LlmModelConfig { Id = "quick-start-model", EndpointId = endpointId };
            copy.Models.Add(model);
        }
        var endpoint = copy.FindEndpoint(model.EndpointId);
        if (endpoint == null)
        {
            endpoint = new LlmEndpoint { Id = "quick-start" };
            copy.Endpoints.Add(endpoint);
            model.EndpointId = endpoint.Id;
        }
        var sameEndpoint = endpoint.BaseUrl.TrimEnd('/') == url.TrimEnd('/');
        endpoint.Name = provider.Name;
        endpoint.BaseUrl = url;
        endpoint.ApiKey = key.Trim();
        endpoint.Enabled = true;
        if (!sameEndpoint) endpoint.ThinkingProtocol = provider.Protocol;
        model.Model = modelName.Trim();
        model.DisplayName = model.Model;
        if (!sameEndpoint) model.ThinkingMode = "auto";
        copy.ConversationModelId = model.Id;
        copy.PlayModelId = model.Id;
        ModelRoleProbe.InvalidateMismatched(copy);
        return copy;
    }
}
