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

    public float? OverlayLeft { get; set; }

    public float? OverlayTop { get; set; }

    public string McpServerPath { get; set; } = string.Empty;

    public int McpPort { get; set; } = 8765;

    public bool McpEnabled { get; set; }

    public int? MaxSessionTokens { get; set; }

    public int? MaxSessionRequests { get; set; }

    public STS2AIAgent.Agent.SessionBudgetGuard CreateBudgetGuard(int initialTokens = 0, int initialRequests = 0)
    {
        return new STS2AIAgent.Agent.SessionBudgetGuard(MaxSessionTokens, MaxSessionRequests, initialTokens, initialRequests);
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
            SupportsVision = false,
            SupportsTools = true,
            ThinkingMode = "auto",
            ThinkingIntensity = "medium"
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

            if (string.IsNullOrWhiteSpace(model.ThinkingIntensity))
            {
                model.ThinkingIntensity = string.IsNullOrWhiteSpace(ThinkingIntensity)
                    ? "medium"
                    : ThinkingIntensity;
            }
        }

        if (string.IsNullOrWhiteSpace(ThinkingIntensity))
        {
            ThinkingIntensity = "medium";
        }

        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "F8";
        }

        if (OverlayLeft is float left && (float.IsNaN(left) || float.IsInfinity(left)))
        {
            OverlayLeft = null;
        }

        if (OverlayTop is float top && (float.IsNaN(top) || float.IsInfinity(top)))
        {
            OverlayTop = null;
        }

        if (McpPort is < 1 or > 65535)
        {
            McpPort = 8765;
        }

        McpServerPath = McpServerPath?.Trim() ?? string.Empty;

        if (MaxSessionTokens is <= 0)
        {
            MaxSessionTokens = null;
        }

        if (MaxSessionRequests is <= 0)
        {
            MaxSessionRequests = null;
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

    public string ThinkingIntensity { get; set; } = string.Empty;

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Model : DisplayName;

    public ThinkingIntensity GetThinkingIntensity()
    {
        return ThinkingIntensityMap.Parse(ThinkingIntensity);
    }
}

internal sealed record ResolvedModel(LlmEndpoint Endpoint, LlmModelConfig Model);
