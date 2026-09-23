namespace STS2AIAgent.Config;

internal sealed class AgentSettings
{
    public List<LlmEndpoint> Endpoints { get; set; } = new();

    public List<LlmModelConfig> Models { get; set; } = new();

    public string? ConversationModelId { get; set; }

    public string? PlayModelId { get; set; }

    public string? VisionModelId { get; set; }

    public string ThinkingIntensity { get; set; } = "medium";

    /// <summary>
    /// Per-request timeout for LLM completions, in seconds. Null or non-positive uses the client
    /// default (10 minutes). A provider that stalls a stream used to hold a turn for the full ten
    /// minutes with the status line frozen on "requesting the model"; a configurable cap lets a
    /// slow deployment fail visibly instead.
    /// </summary>
    public int? LlmRequestTimeoutSeconds { get; set; }

    /// <summary>Per-request timeout for Jev system-one calls, in seconds. Null or non-positive means 90.</summary>
    public int? JevRequestTimeoutSeconds { get; set; }

    public string Hotkey { get; set; } = "F8";

    /// <summary>
    /// The overlay's colour theme id. Stored as a plain string so Config does not depend on the UI
    /// assembly; the overlay normalises an unknown value to the default when it reads it, which is
    /// what keeps a hand-edited settings file from leaving the panel unthemed.
    /// </summary>
    public string OverlayTheme { get; set; } = "slate";

    public bool AttachStateInChat { get; set; } = true;

    public bool AttachScreenshotInChat { get; set; }

    public bool OverlayVisibleOnStart { get; set; }

    public bool HasSeenFirstRunGuide { get; set; }

    /// <summary>Companion picks the preselected character and readies up by itself. Set false to choose for it via the companion API.</summary>
    public bool CompanionAutoSelectCharacter { get; set; } = true;

    public List<ModelRoleTestRecord> RoleTests { get; set; } = new();

    /// <summary>Per-model verification results, keyed by model id. Written by the per-model test.</summary>
    public List<ModelTestRecord> ModelTests { get; set; } = new();

    public float? OverlayLeft { get; set; }

    public float? OverlayTop { get; set; }

    public string McpServerPath { get; set; } = string.Empty;

    public int McpPort { get; set; } = 8765;

    public bool McpEnabled { get; set; }

    public int? MaxSessionTokens { get; set; }

    public int? MaxSessionRequests { get; set; }

    public bool ProactiveChatEnabled { get; set; }

    public string ProactiveChatTone { get; set; } = STS2AIAgent.Agent.ProactiveChatTones.Default;

    /// <summary>
    /// Which mode the overlay's play page shows: <c>"solo"</c> (single-player AI play) or
    /// <c>"coop"</c> (multiplayer AI teammate). Only one mode is active at a time; the play page
    /// switches its whole content area on this value.
    /// </summary>
    public string OverlayPlayMode { get; set; } = "solo";

    /// <summary>Whether the play-page conversation renders the model's reasoning alongside its replies.</summary>
    public bool ShowThinkingInChat { get; set; }

    /// <summary>
    /// The language the model replies in: <c>"auto"</c> (follow the player), <c>"zh"</c> or
    /// <c>"en"</c>. Injected into the system prompt for both chat and play turns.
    /// </summary>
    public string ReplyLanguage { get; set; } = "auto";

    /// <summary>Dual-layer decision mode for solo play: Jev executes each action, the LLM only plans strategy.</summary>
    public bool DualLayerSoloEnabled { get; set; }

    /// <summary>Dual-layer decision mode for the multiplayer AI teammate.</summary>
    public bool DualLayerCoopEnabled { get; set; }

    /// <summary>TypeSafe API base URL for the Jev execution model.</summary>
    public string JevBaseUrl { get; set; } = "https://api.typesafe.ai";

    /// <summary>TypeSafe API key for the Jev execution model. Secret; never logged or exported.</summary>
    public string JevApiKey { get; set; } = string.Empty;

    /// <summary>Jev model id or alias (for example <c>jev-latest</c>).</summary>
    public string JevModel { get; set; } = "jev-latest";

    /// <summary>
    /// Confidence below which a Jev decision falls back to the LLM for that turn. Clamped to 0..1.
    /// </summary>
    public double JevConfidenceThreshold { get; set; } = 0.35;

    /// <summary>True when the Jev execution model has enough configuration to be usable.</summary>
    public bool HasJevConfigured()
    {
        return !string.IsNullOrWhiteSpace(JevApiKey) && !string.IsNullOrWhiteSpace(JevBaseUrl);
    }

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

    public ResolvedModel? TryResolveConversationModel()
    {
        return TryResolveRoleModel(ConversationModelId);
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
            OverlayVisibleOnStart = false,
            HasSeenFirstRunGuide = false
        };
    }

    public void EnsureValidShape()
    {
        Endpoints ??= new List<LlmEndpoint>();
        Models ??= new List<LlmModelConfig>();
        RoleTests ??= new List<ModelRoleTestRecord>();
        ModelTests ??= new List<ModelTestRecord>();
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

            if (model.ContextWindow is <= 0)
            {
                model.ContextWindow = null;
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

        if (LlmRequestTimeoutSeconds is <= 0)
        {
            LlmRequestTimeoutSeconds = null;
        }

        if (JevRequestTimeoutSeconds is <= 0)
        {
            JevRequestTimeoutSeconds = null;
        }

        ProactiveChatTone = STS2AIAgent.Agent.ProactiveChatTones.Normalize(ProactiveChatTone);

        if (string.IsNullOrWhiteSpace(OverlayPlayMode) ||
            (OverlayPlayMode != "solo" && OverlayPlayMode != "coop"))
        {
            OverlayPlayMode = "solo";
        }

        if (ReplyLanguage is not ("auto" or "zh" or "en"))
        {
            ReplyLanguage = "auto";
        }

        JevBaseUrl = string.IsNullOrWhiteSpace(JevBaseUrl)
            ? "https://api.typesafe.ai"
            : JevBaseUrl.Trim().TrimEnd('/');
        JevApiKey = JevApiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(JevModel))
        {
            JevModel = "jev-latest";
        }

        if (double.IsNaN(JevConfidenceThreshold) || double.IsInfinity(JevConfidenceThreshold))
        {
            JevConfidenceThreshold = 0.35;
        }

        JevConfidenceThreshold = Math.Clamp(JevConfidenceThreshold, 0.0, 1.0);
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

    /// <summary>
    /// This model's input context window, in tokens. Null or a non-positive value means the default
    /// 256,000. It is a model capability, not a spend cap.
    /// </summary>
    public int? ContextWindow { get; set; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Model : DisplayName;

    public ThinkingIntensity GetThinkingIntensity()
    {
        return ThinkingIntensityMap.Parse(ThinkingIntensity);
    }
}

internal sealed record ResolvedModel(LlmEndpoint Endpoint, LlmModelConfig Model);

/// <summary>
/// The per-model verification record: connectivity plus a real tool-calling probe. Keyed by model
/// id with a fingerprint of endpoint/model/key, so editing any of those invalidates the badge.
/// </summary>
internal sealed class ModelTestRecord
{
    public string ModelId { get; set; } = string.Empty;

    /// <summary>unverified | verified | failed</summary>
    public string Status { get; set; } = "unverified";

    /// <summary>unknown | supported | unsupported — the tool-calling probe's answer.</summary>
    public string Tools { get; set; } = "unknown";

    public string? Fingerprint { get; set; }

    public string? Error { get; set; }

    public string? TestedAt { get; set; }
}
