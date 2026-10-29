using STS2AIAgent.Config;

namespace STS2AIAgent.Multiplayer;

/// <summary>Play configuration only. Host overlay, paths, role and MCP flags stay local.</summary>
internal sealed class CompanionPlaySettings
{
    public List<LlmEndpoint> Endpoints { get; init; } = new();
    public List<LlmModelConfig> Models { get; init; } = new();
    public string? ConversationModelId { get; init; }
    public string? PlayModelId { get; init; }
    public string? VisionModelId { get; init; }
    public List<ModelRoleTestRecord> RoleTests { get; init; } = new();
    public List<ModelTestRecord> ModelTests { get; init; } = new();
    public bool NonCombatOnlyEnabled { get; init; }
    public int? MaxSessionTokens { get; init; }
    public int? MaxSessionRequests { get; init; }
    public int? LlmRequestTimeoutSeconds { get; init; }
    public string ReplyLanguage { get; init; } = "auto";
    public bool ProactiveChatEnabled { get; init; }
    public string ProactiveChatTone { get; init; } = "default";

    // Experience switches the teammate plays by. The defaults match AgentSettings so a payload from a
    // sender that predates them keeps the receiver on the shipped defaults.
    public string TurboMode { get; init; } = AgentSettings.TurboInstant;
    public bool AutopilotEnabled { get; init; } = true;
    public bool TurnPlansEnabled { get; init; } = true;
    public bool CombatMathEnabled { get; init; } = true;
    public string VisionMode { get; init; } = AgentSettings.VisionOnDemand;
    public bool RunJournalEnabled { get; init; } = true;
    public int? MaxOutputTokens { get; init; } = AgentSettings.DefaultMaxOutputTokens;

    public bool IsValid => Endpoints is { Count: <= 16 } && Models is { Count: <= 32 }
        && RoleTests is { Count: <= 3 } && ModelTests is { Count: <= 32 }
        && Endpoints.All(e => e != null && e.Id is { Length: > 0 and <= 128 }
            && e.BaseUrl is { Length: > 0 and <= 2048 } && e.ApiKey is { Length: <= 4096 }
            && e.ThinkingProtocol is "auto" or "deepseek" or "reasoning_effort" or "prompt")
        && Models.All(m => m != null && m.Id is { Length: > 0 and <= 128 } && m.Model is { Length: <= 256 }
            && Endpoints.Any(e => string.Equals(e.Id, m.EndpointId, StringComparison.OrdinalIgnoreCase)))
        && RoleTests.All(t => t != null) && ModelTests.All(t => t != null)
        && MaxSessionTokens is null or > 0 && MaxSessionRequests is null or > 0
        && LlmRequestTimeoutSeconds is null or (> 0 and <= 3600)
        && ReplyLanguage is "auto" or "zh" or "en" && ProactiveChatTone is { Length: <= 128 }
        && MaxOutputTokens is null or (> 0 and <= 1_000_000)
        && TurboMode is AgentSettings.TurboOff or AgentSettings.TurboFast or AgentSettings.TurboInstant
        && VisionMode is AgentSettings.VisionOff or AgentSettings.VisionOnDemand or AgentSettings.VisionEveryStep;

    public static CompanionPlaySettings From(AgentSettings settings)
    {
        var copy = SettingsClone.Clone(settings);
        return new CompanionPlaySettings
        {
            Endpoints = copy.Endpoints, Models = copy.Models, ConversationModelId = copy.ConversationModelId,
            PlayModelId = copy.PlayModelId, VisionModelId = copy.VisionModelId, RoleTests = copy.RoleTests,
            ModelTests = copy.ModelTests, NonCombatOnlyEnabled = copy.NonCombatOnlyEnabled,
            MaxSessionTokens = copy.MaxSessionTokens, MaxSessionRequests = copy.MaxSessionRequests,
            LlmRequestTimeoutSeconds = copy.LlmRequestTimeoutSeconds, ReplyLanguage = copy.ReplyLanguage,
            ProactiveChatEnabled = copy.ProactiveChatEnabled, ProactiveChatTone = copy.ProactiveChatTone,
            TurboMode = copy.TurboMode, AutopilotEnabled = copy.AutopilotEnabled,
            TurnPlansEnabled = copy.TurnPlansEnabled, CombatMathEnabled = copy.CombatMathEnabled,
            VisionMode = copy.VisionMode, RunJournalEnabled = copy.RunJournalEnabled,
            MaxOutputTokens = copy.MaxOutputTokens
        };
    }

    public void ApplyTo(AgentSettings settings)
    {
        if (!IsValid) throw new ArgumentException("Invalid companion play settings.");
        // Clone the wire data; mutating a sender after this call cannot change the receiver.
        var copy = SettingsClone.Clone(new AgentSettings
        {
            Endpoints = Endpoints, Models = Models, ConversationModelId = ConversationModelId,
            PlayModelId = PlayModelId, VisionModelId = VisionModelId, RoleTests = RoleTests, ModelTests = ModelTests
        });
        settings.Endpoints = copy.Endpoints;
        settings.Models = copy.Models;
        settings.ConversationModelId = copy.ConversationModelId;
        settings.PlayModelId = copy.PlayModelId;
        settings.VisionModelId = copy.VisionModelId;
        settings.RoleTests = copy.RoleTests;
        settings.ModelTests = copy.ModelTests;
        settings.NonCombatOnlyEnabled = NonCombatOnlyEnabled;
        settings.MaxSessionTokens = MaxSessionTokens;
        settings.MaxSessionRequests = MaxSessionRequests;
        settings.LlmRequestTimeoutSeconds = LlmRequestTimeoutSeconds;
        settings.ReplyLanguage = ReplyLanguage;
        settings.ProactiveChatEnabled = ProactiveChatEnabled;
        settings.ProactiveChatTone = ProactiveChatTone;
        settings.TurboMode = TurboMode;
        settings.AutopilotEnabled = AutopilotEnabled;
        settings.TurnPlansEnabled = TurnPlansEnabled;
        settings.CombatMathEnabled = CombatMathEnabled;
        settings.VisionMode = VisionMode;
        settings.RunJournalEnabled = RunJournalEnabled;
        settings.MaxOutputTokens = MaxOutputTokens;
    }
}
