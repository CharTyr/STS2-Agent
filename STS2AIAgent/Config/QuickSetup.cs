using STS2AIAgent.Localization;

namespace STS2AIAgent.Config;

internal sealed record ProviderPreset(string Id, string Name, string BaseUrl, string Protocol);

internal static class QuickSetup
{
    public static readonly ProviderPreset[] Providers =
    {
        new("deepseek", "DeepSeek", "https://api.deepseek.com", "deepseek"),
        new("openai", "OpenAI API", "https://api.openai.com/v1", "auto"),
        new("siliconflow", "硅基流动 / SiliconFlow", "https://api.siliconflow.cn/v1", "prompt"),
        new("ollama", "Ollama", "http://127.0.0.1:11434/v1", "prompt"),
        new("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", "prompt"),
        new("custom", "自定义 / Custom", "", "auto")
    };

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
