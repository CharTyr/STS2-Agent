using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

internal sealed class DiagnosticSnapshot
{
    public required string ModVersion { get; init; }

    public required string Role { get; init; }

    public required string PlayPhase { get; init; }

    public required string Status { get; init; }

    public required string DualStatus { get; init; }

    public required string TeamControlStatus { get; init; }

    public required string? StopKind { get; init; }

    public required string? StopDetail { get; init; }

    public required string ApiPrefix { get; init; }

    public required string? McpUrl { get; init; }

    public required bool McpEnabled { get; init; }

    public required bool UsageKnown { get; init; }

    public required int SessionRequests { get; init; }

    public required int? SessionTokens { get; init; }

    public required IReadOnlyList<string> RecentEvents { get; init; }

    public required IReadOnlyList<string> RecentRequestIds { get; init; }

    public required AgentSettings Settings { get; init; }
}

internal static class DiagnosticExport
{
    private static readonly Regex AuthorizationBearer = new(
        @"(\bAuthorization\s*[""']?\s*[:=]\s*[""']?\s*Bearer\s+)[^\s,;""'}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerToken = new(
        @"(\bBearer\s+)[^\s,;""'}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AuthorizationValue = new(
        @"(\bAuthorization\s*[""']?\s*[:=]\s*[""']?\s*)(?!Bearer\b)[^\s,;}&\]#""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UrlUserInfo = new(
        @"(https?://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UrlSecretQuery = new(
        @"([?&](?:api[\s_-]*key|session[\s_-]*token|access[\s_-]*token|refresh[\s_-]*token|authorization|token|key|secret|password)=)[^&#\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveAssignment = new(
        @"(\b(?:api[\s_-]*key|session[\s_-]*token|companion[\s_-]*session|access[\s_-]*token|refresh[\s_-]*token|client[\s_-]*secret|password|secret|token|key)\s*[""']?\s*[:=]\s*[""']?)[^\s,;}&\]#""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveLabel = new(
        @"(\b(?:api[\s_-]*key|session[\s_-]*token|companion[\s_-]*session)\s+)[^\s,;}&\]#""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SkSecret = new(
        @"\bsk-[A-Za-z0-9][A-Za-z0-9._~-]{7,}",
        RegexOptions.CultureInvariant);

    public const string IncludesChatNotice = "默认不包含对话或队伍聊天正文。";

    public static string Render(DiagnosticSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("STS2 AI Agent 诊断（已脱敏）");
        builder.AppendLine(IncludesChatNotice);
        builder.AppendLine("已排除 API Key、Authorization 头和会话令牌。");
        builder.AppendLine("mod_version=" + Redact(snapshot.ModVersion));
        builder.AppendLine("instance_role=" + Redact(snapshot.Role));
        builder.AppendLine("play_phase=" + Redact(snapshot.PlayPhase));
        builder.AppendLine("status=" + Redact(snapshot.Status));
        builder.AppendLine("dual_status=" + Redact(snapshot.DualStatus));
        builder.AppendLine("team_control=" + Redact(snapshot.TeamControlStatus));
        builder.AppendLine("stop_kind=" + Redact(snapshot.StopKind ?? "-"));
        builder.AppendLine("stop_detail=" + Redact(snapshot.StopDetail ?? "-"));
        builder.AppendLine("api=" + Redact(snapshot.ApiPrefix));
        builder.AppendLine("mcp_enabled=" + snapshot.McpEnabled);
        builder.AppendLine("mcp_url=" + Redact(snapshot.McpUrl ?? "-"));
        builder.AppendLine("usage_known=" + snapshot.UsageKnown);
        builder.AppendLine("session_requests=" + snapshot.SessionRequests);
        builder.AppendLine("session_tokens=" + (snapshot.UsageKnown ? snapshot.SessionTokens?.ToString() ?? "0" : "unknown"));
        builder.AppendLine("request_ids=" + Redact(snapshot.RecentRequestIds.Count == 0 ? "-" : string.Join(",", snapshot.RecentRequestIds)));
        builder.AppendLine("events:");
        foreach (var line in snapshot.RecentEvents)
        {
            builder.AppendLine("  - " + Redact(line));
        }

        builder.AppendLine("settings:");
        builder.AppendLine(RedactedSettingsJson(snapshot.Settings));
        return builder.ToString();
    }

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var redacted = text;
        redacted = AuthorizationBearer.Replace(redacted, "$1***");
        redacted = BearerToken.Replace(redacted, "$1***");
        redacted = AuthorizationValue.Replace(redacted, "$1***");
        redacted = UrlUserInfo.Replace(redacted, "$1***@");
        redacted = UrlSecretQuery.Replace(redacted, "$1***");
        redacted = SensitiveAssignment.Replace(redacted, "$1***");
        redacted = SensitiveLabel.Replace(redacted, "$1***");
        redacted = SkSecret.Replace(redacted, "***");
        return redacted;
    }

    public static string RedactedSettingsJson(AgentSettings settings)
    {
        var payload = new
        {
            conversationModelId = Redact(settings.ConversationModelId),
            playModelId = Redact(settings.PlayModelId),
            visionModelId = Redact(settings.VisionModelId),
            hasSeenFirstRunGuide = settings.HasSeenFirstRunGuide,
            mcpEnabled = settings.McpEnabled,
            maxSessionTokens = settings.MaxSessionTokens,
            maxSessionRequests = settings.MaxSessionRequests,
            endpoints = (settings.Endpoints ?? new List<LlmEndpoint>()).Select(endpoint => new
            {
                Id = Redact(endpoint.Id),
                Name = Redact(endpoint.Name),
                BaseUrl = Redact(endpoint.BaseUrl),
                endpoint.Enabled,
                apiKey = string.IsNullOrWhiteSpace(endpoint.ApiKey) ? "" : "***"
            }),
            models = (settings.Models ?? new List<LlmModelConfig>()).Select(model => new
            {
                Id = Redact(model.Id),
                EndpointId = Redact(model.EndpointId),
                Model = Redact(model.Model),
                DisplayName = Redact(model.DisplayName),
                model.SupportsVision,
                model.SupportsTools
            }),
            roleTests = (settings.RoleTests ?? new List<ModelRoleTestRecord>()).Select(test => new
            {
                Role = Redact(test.Role),
                Status = Redact(test.Status),
                CapabilityStatus = Redact(test.CapabilityStatus),
                EndpointName = Redact(test.EndpointName),
                ModelName = Redact(test.ModelName),
                test.StatusCode,
                error = Redact(test.Error),
                nextStep = Redact(test.NextStep),
                testedAt = Redact(test.TestedAt)
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
