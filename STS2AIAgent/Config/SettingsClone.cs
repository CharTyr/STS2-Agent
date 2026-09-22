namespace STS2AIAgent.Config;

/// <summary>
/// Deep copy of the settings model. The overlay edits a clone and harvests control values into
/// it, so a field that is present on <see cref="AgentSettings"/> but missing here is silently
/// dropped the next time the form is rebuilt or saved. Keeping the copy in Config (rather than
/// in the Godot overlay) puts it inside the executable test project, which is what lets the
/// "new field survives the editor" property be checked instead of assumed.
/// </summary>
internal static class SettingsClone
{
    public static AgentSettings Clone(AgentSettings source)
    {
        source.EnsureValidShape();
        return new AgentSettings
        {
            Endpoints = source.Endpoints.Select(endpoint => new LlmEndpoint
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                BaseUrl = endpoint.BaseUrl,
                ApiKey = endpoint.ApiKey,
                Enabled = endpoint.Enabled
            }).ToList(),
            Models = source.Models.Select(model => new LlmModelConfig
            {
                Id = model.Id,
                EndpointId = model.EndpointId,
                Model = model.Model,
                DisplayName = model.DisplayName,
                SupportsVision = model.SupportsVision,
                SupportsTools = model.SupportsTools,
                ThinkingMode = model.ThinkingMode,
                ThinkingIntensity = model.ThinkingIntensity,
                ContextWindow = model.ContextWindow
            }).ToList(),
            ConversationModelId = source.ConversationModelId,
            PlayModelId = source.PlayModelId,
            VisionModelId = source.VisionModelId,
            ThinkingIntensity = source.ThinkingIntensity,
            Hotkey = source.Hotkey,
            AttachStateInChat = source.AttachStateInChat,
            AttachScreenshotInChat = source.AttachScreenshotInChat,
            OverlayVisibleOnStart = source.OverlayVisibleOnStart,
            HasSeenFirstRunGuide = source.HasSeenFirstRunGuide,
            CompanionAutoSelectCharacter = source.CompanionAutoSelectCharacter,
            OverlayTheme = source.OverlayTheme,
            OverlayLeft = source.OverlayLeft,
            OverlayTop = source.OverlayTop,
            McpServerPath = source.McpServerPath,
            McpPort = source.McpPort,
            McpEnabled = source.McpEnabled,
            MaxSessionTokens = source.MaxSessionTokens,
            MaxSessionRequests = source.MaxSessionRequests,
            LlmRequestTimeoutSeconds = source.LlmRequestTimeoutSeconds,
            JevRequestTimeoutSeconds = source.JevRequestTimeoutSeconds,
            ProactiveChatEnabled = source.ProactiveChatEnabled,
            ProactiveChatTone = source.ProactiveChatTone,
            OverlayPlayMode = source.OverlayPlayMode,
            ShowThinkingInChat = source.ShowThinkingInChat,
            ReplyLanguage = source.ReplyLanguage,
            DualLayerSoloEnabled = source.DualLayerSoloEnabled,
            DualLayerCoopEnabled = source.DualLayerCoopEnabled,
            JevBaseUrl = source.JevBaseUrl,
            JevApiKey = source.JevApiKey,
            JevModel = source.JevModel,
            JevConfidenceThreshold = source.JevConfidenceThreshold,
            RoleTests = source.RoleTests.Select(test => new ModelRoleTestRecord
            {
                Role = test.Role,
                Status = test.Status,
                CapabilityStatus = test.CapabilityStatus,
                EndpointId = test.EndpointId,
                EndpointName = test.EndpointName,
                ModelId = test.ModelId,
                ModelName = test.ModelName,
                Fingerprint = test.Fingerprint,
                StatusCode = test.StatusCode,
                Error = test.Error,
                NextStep = test.NextStep,
                TestedAt = test.TestedAt
            }).ToList(),
            ModelTests = source.ModelTests.Select(test => new ModelTestRecord
            {
                ModelId = test.ModelId,
                Status = test.Status,
                Tools = test.Tools,
                Fingerprint = test.Fingerprint,
                Error = test.Error,
                TestedAt = test.TestedAt
            }).ToList()
        };
    }
}

