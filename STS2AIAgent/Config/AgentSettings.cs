namespace STS2AIAgent.Config;

internal sealed class AgentSettings
{
    public List<LlmEndpoint> Endpoints { get; set; } = new();

    public List<LlmModelConfig> Models { get; set; } = new();

    public string? ConversationModelId { get; set; }

    public string? PlayModelId { get; set; }

    public string? VisionModelId { get; set; }

    public string ThinkingIntensity { get; set; } = "medium";

    public string Hotkey { get; set; } = "F8";

    public bool AttachStateInChat { get; set; } = true;

    public bool AttachScreenshotInChat { get; set; }

    public bool OverlayVisibleOnStart { get; set; }

    public ThinkingIntensity GetThinkingIntensity()
    {
        return ThinkingIntensityMap.Parse(ThinkingIntensity);
    }

    public LlmModelConfig? FindModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public LlmEndpoint? FindEndpoint(string? endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return null;
        }

        return Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));
    }

    public ResolvedModel ResolveConversationModel()
    {
        return ResolveRoleModel(ConversationModelId, required: true, roleName: "conversation");
    }

    public ResolvedModel? TryResolvePlayModel()
    {
        var playId = string.IsNullOrWhiteSpace(PlayModelId) ? ConversationModelId : PlayModelId;
        return TryResolveRoleModel(playId);
    }

    public ResolvedModel? TryResolveVisionModel()
    {
        return TryResolveRoleModel(VisionModelId);
    }

    public ResolvedModel ResolvePlayModel()
    {
        return TryResolvePlayModel()
            ?? throw new InvalidOperationException("Select a conversation or play model in the agent settings.");
    }

    public static AgentSettings CreateDefault()
    {
        var endpoint = new LlmEndpoint
        {
            Id = "default",
            Name = "OpenAI Compatible",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = string.Empty,
            Enabled = true
        };
        var model = new LlmModelConfig
        {
            Id = "default-model",
            EndpointId = endpoint.Id,
            Model = "gpt-4o",
            DisplayName = "gpt-4o",
            SupportsVision = true,
            SupportsTools = true,
            ThinkingMode = "auto"
        };

        return new AgentSettings
        {
            Endpoints = { endpoint },
            Models = { model },
            ConversationModelId = model.Id,
            Hotkey = "F8",
            AttachStateInChat = true,
            OverlayVisibleOnStart = false
        };
    }

    public void EnsureValidShape()
    {
        Endpoints ??= new List<LlmEndpoint>();
        Models ??= new List<LlmModelConfig>();
        if (Endpoints.Count == 0 && Models.Count == 0)
        {
            var defaults = CreateDefault();
            Endpoints = defaults.Endpoints;
            Models = defaults.Models;
            ConversationModelId ??= defaults.ConversationModelId;
        }

        foreach (var endpoint in Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Id))
            {
                endpoint.Id = Guid.NewGuid().ToString("N")[..8];
            }
        }

        foreach (var model in Models)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
            {
                model.Id = Guid.NewGuid().ToString("N")[..8];
            }
        }

        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "F8";
        }
    }

    private ResolvedModel ResolveRoleModel(string? modelId, bool required, string roleName)
    {
        var resolved = TryResolveRoleModel(modelId);
        if (resolved != null)
        {
            return resolved;
        }

        if (!required)
        {
            throw new InvalidOperationException($"Model for role '{roleName}' is not configured.");
        }

        throw new InvalidOperationException($"Select a {roleName} model in the agent settings.");
    }

    private ResolvedModel? TryResolveRoleModel(string? modelId)
    {
        var model = FindModel(modelId);
        if (model == null)
        {
            return null;
        }

        var endpoint = FindEndpoint(model.EndpointId);
        if (endpoint == null || !endpoint.Enabled)
        {
            return null;
        }

        return new ResolvedModel(endpoint, model);
    }
}

internal sealed class LlmEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "Endpoint";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

internal sealed class LlmModelConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string EndpointId { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o";

    public string DisplayName { get; set; } = string.Empty;

    public bool SupportsVision { get; set; }

    public bool SupportsTools { get; set; } = true;

    public string ThinkingMode { get; set; } = "auto";

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Model : DisplayName;
}

internal sealed record ResolvedModel(LlmEndpoint Endpoint, LlmModelConfig Model);
