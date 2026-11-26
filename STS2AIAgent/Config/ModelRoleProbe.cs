using System.Security.Cryptography;
using System.Text;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Config;

internal static class ModelRoleNames
{
    public const string Conversation = "conversation";
    public const string Play = "play";
    public const string Vision = "vision";
}

internal sealed class ModelRoleTestRecord
{
    public string Role { get; set; } = ModelRoleNames.Conversation;

    /// <summary>unverified | verified | failed | unused</summary>
    public string Status { get; set; } = "unverified";

    /// <summary>unverified | supported | unsupported | unused. Connectivity alone never implies tools.</summary>
    public string CapabilityStatus { get; set; } = "unverified";

    public string? EndpointId { get; set; }

    public string? EndpointName { get; set; }

    public string? ModelId { get; set; }

    public string? ModelName { get; set; }

    public string? Fingerprint { get; set; }

    public int? StatusCode { get; set; }

    public string? Error { get; set; }

    public string? NextStep { get; set; }

    public string? TestedAt { get; set; }
}

internal readonly record struct ModelRoleProbeResult(
    string Role,
    ModelRoleTestRecord Record,
    bool SkippedBecauseFresh);

internal static class ModelRoleProbe
{
    public static string Fingerprint(ResolvedModel model)
    {
        var raw = (model.Endpoint.BaseUrl ?? "") + "\n" + (model.Model.Model ?? "") + "\n" + (model.Endpoint.ApiKey ?? "") + "\n" + model.Endpoint.ThinkingProtocol + "\n" + model.Model.ThinkingMode + "\n" + model.Model.ThinkingIntensity + "\nprobe-v2";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16];
    }

    public static ModelRoleTestRecord Unused(string role)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = "unused",
            CapabilityStatus = "unused",
            NextStep = role == ModelRoleNames.Vision ? Loc.T("未配置视觉模型（可跳过）") : null
        };
    }

    public static ModelRoleTestRecord Unverified(string role, ResolvedModel? model)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = "unverified",
            CapabilityStatus = "unverified",
            EndpointId = model?.Endpoint.Id,
            EndpointName = model?.Endpoint.Name,
            ModelId = model?.Model.Id,
            ModelName = model?.Model.Model,
            Fingerprint = model == null ? null : Fingerprint(model),
            NextStep = Loc.T("到「设置」测试后再邀请队友。")
        };
    }

    public static ModelRoleTestRecord FromSuccess(string role, ResolvedModel model, bool? tools = null)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = "verified",
            CapabilityStatus = role == ModelRoleNames.Vision || tools == null ? "unverified" : tools.Value ? "supported" : "unsupported",
            EndpointId = model.Endpoint.Id,
            EndpointName = model.Endpoint.Name,
            ModelId = model.Model.Id,
            ModelName = model.Model.Model,
            Fingerprint = Fingerprint(model),
            TestedAt = DateTimeOffset.UtcNow.ToString("o"),
            NextStep = tools == true && role != ModelRoleNames.Vision
                ? Loc.T("工具调用可用。")
                : tools == false && role != ModelRoleNames.Vision
                    ? Loc.T("工具调用不可用，请换模型。")
                    : Loc.T("{0}已连通，能力未验证。", RoleLabel(role))
        };
    }

    public static ModelRoleTestRecord FromException(string role, ResolvedModel model, Exception ex)
    {
        var statusCode = (ex as LlmException)?.StatusCode;
        var classified = Classify(statusCode, ex.Message, model, role);
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = "failed",
            CapabilityStatus = "unverified",
            EndpointId = model.Endpoint.Id,
            EndpointName = model.Endpoint.Name,
            ModelId = model.Model.Id,
            ModelName = model.Model.Model,
            Fingerprint = Fingerprint(model),
            StatusCode = statusCode,
            Error = classified.Error,
            NextStep = classified.NextStep,
            TestedAt = DateTimeOffset.UtcNow.ToString("o")
        };
    }

    private static bool FingerprintsMatch(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static ModelRoleTestRecord Current(AgentSettings settings, string role)
    {
        var resolved = Resolve(settings, role);
        var stored = Find(settings, role);
        if (role == ModelRoleNames.Vision && resolved == null)
        {
            return Unused(role);
        }

        if (resolved == null)
        {
            return Unverified(role, null);
        }

        if (stored != null && FingerprintsMatch(stored.Fingerprint, Fingerprint(resolved)))
        {
            return stored;
        }

        return Unverified(role, resolved);
    }

    public static bool IsVerified(AgentSettings settings, string role)
    {
        var record = Current(settings, role);
        return record.Status == "verified";
    }

    public static ModelRoleTestRecord? Find(AgentSettings settings, string role)
    {
        return settings.RoleTests?.FirstOrDefault(item =>
            string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));
    }

    public static void Upsert(AgentSettings settings, ModelRoleTestRecord record)
    {
        settings.RoleTests ??= new List<ModelRoleTestRecord>();
        settings.RoleTests.RemoveAll(item =>
            string.Equals(item.Role, record.Role, StringComparison.OrdinalIgnoreCase));
        settings.RoleTests.Add(record);
    }

    public static void InvalidateMismatched(AgentSettings settings)
    {
        foreach (var role in new[] { ModelRoleNames.Conversation, ModelRoleNames.Play, ModelRoleNames.Vision })
        {
            var resolved = Resolve(settings, role);
            var stored = Find(settings, role);
            if (stored == null)
            {
                continue;
            }

            if (role == ModelRoleNames.Vision && resolved == null)
            {
                Upsert(settings, Unused(role));
                continue;
            }

            if (resolved == null || !FingerprintsMatch(stored.Fingerprint, Fingerprint(resolved)))
            {
                Upsert(settings, Unverified(role, resolved));
            }
        }
    }

    public static string FormatLine(ModelRoleTestRecord record)
    {
        var role = RoleLabel(record.Role);
        var connectivity = record.Status switch
        {
            "verified" => Loc.T("连通成功"),
            "failed" => Loc.T("连通失败"),
            "unused" => Loc.T("未使用"),
            _ => Loc.T("尚未验证")
        };
        var capability = record.CapabilityStatus switch
        {
            "unused" => Loc.T("能力：未使用"),
            "supported" => Loc.T("工具调用：可用"),
            "unsupported" => Loc.T("工具调用：不可用"),
            _ => Loc.T("能力：未验证")
        };
        var target = string.IsNullOrWhiteSpace(record.ModelName)
            ? ""
            : $" · {record.EndpointName ?? record.EndpointId} / {record.ModelName}";
        var error = string.IsNullOrWhiteSpace(record.Error) ? "" : " — " + record.Error;
        return Loc.T("{0}：{1}{2} · {3}{4}", role, connectivity, target, capability, error);
    }

    public static string FailureKind(int? statusCode, string? message)
    {
        var text = message ?? string.Empty;
        if (statusCode == 402 || IsQuotaExhaustedMessage(text))
        {
            return "config";
        }

        if (statusCode is >= 400 and < 500 && statusCode is not 408 and not 429)
        {
            return "config";
        }

        if (statusCode == 408 || statusCode == 429 || statusCode >= 500)
        {
            return "network";
        }

        if (ContainsAny(text,
                "unauthorized",
                "forbidden",
                "authentication",
                "credential",
                "api key",
                "apikey",
                "api_key",
                "api-key",
                "模型名",
                "model name",
                "模型不存在",
                "model not found",
                "model missing",
                "no such model",
                "model does not exist",
                "does not exist",
                "unknown model",
                "not found",
                "端点配置",
                "endpoint config",
                "invalid endpoint",
                "配置错误",
                "config error",
                "configuration error",
                "invalid config",
                "misconfigur",
                "not configured",
                "invalid model",
                "unsupported model"))
        {
            return "config";
        }

        if (ContainsAny(text,
                "timeout",
                "timed out",
                "rate limit",
                "too many requests",
                "temporarily unavailable",
                "service unavailable",
                "connection refused",
                "connection reset",
                "unreachable"))
        {
            return "network";
        }

        return "network";
    }

    public static string RoleLabel(string role) => role.ToLowerInvariant() switch
    {
        ModelRoleNames.Play => Loc.T("游玩模型"),
        ModelRoleNames.Vision => Loc.T("视觉模型"),
        _ => Loc.T("对话模型")
    };

    public static ResolvedModel? Resolve(AgentSettings settings, string role)
    {
        return role switch
        {
            ModelRoleNames.Play => settings.TryResolvePlayModel(),
            ModelRoleNames.Vision => settings.TryResolveVisionModel(),
            _ => settings.TryResolveConversationModel()
        };
    }

    public static (string Error, string NextStep) Classify(
        int? statusCode,
        string message,
        ResolvedModel model,
        string role = ModelRoleNames.Play)
    {
        var rawEndpoint = string.IsNullOrWhiteSpace(model.Endpoint.Name) ? model.Endpoint.BaseUrl : model.Endpoint.Name;
        var endpoint = Agent.DiagnosticExport.Redact(rawEndpoint);
        var modelName = model.Model.Model;
        var lower = Agent.DiagnosticExport.Redact(message ?? string.Empty);

        if (statusCode == 402 || IsQuotaExhaustedMessage(lower))
        {
            return (
                Loc.T("{0} 账户余额或额度不足。", endpoint),
                Loc.T("请前往服务商后台充值、检查配额，或切换其他可用端点。"));
        }

        if (statusCode == 403 && ContainsAny(lower, "unsupported_country", "country, region, or territory", "not available in your country", "not available in your region", "geo_blocked", "geoblocked"))
        {
            return (
                Loc.T("{0} 拒绝了当前地区或代理出口（403）。", endpoint),
                Loc.T("请切换支持的代理地区，或改用支持直连的服务商端点。"));
        }

        if (statusCode is 400 or 422 && ContainsAny(lower, "max_tokens", "max_completion_tokens", "context_length", "maximum context"))
        {
            return (
                Loc.T("{0} / {1} 拒绝了长度参数（{2}）。", endpoint, modelName, statusCode),
                Loc.T("可尝试切换为「省钱速通」预设以降低单次输出上限，或检查该服务商是否兼容标准 OpenAI 接口。"));
        }

        if (statusCode is 400 or 422 && ContainsAny(lower, "tool_choice", "does not support tools", "tools is not supported", "not support tool", "function_call", "function call", "tool calling", "tool_use"))
        {
            return (
                Loc.T("{0} / {1} 不支持当前工具调用格式（{2}）。", endpoint, modelName, statusCode),
                Loc.T("建议更换支持 OpenAI Tool Calling 的模型（如 gpt-4o / deepseek-chat / claude / gemini）。"));
        }

        if (statusCode is 401 or 403)
        {
            return (
                Loc.T("{0} 返回 {1}，认证失败。", endpoint, statusCode),
                Loc.T("请检查该端点的 API Key 是否填写正确且未过期。"));
        }

        if (statusCode == 404)
        {
            return (
                Loc.T("{0} 返回 404，找不到模型 {1}。", endpoint, modelName),
                Loc.T("请核对模型名和 Base URL（通常以 /v1 结尾）。"));
        }

        if (statusCode == 429)
        {
            return (
                Loc.T("{0} 返回 429，请求过于频繁或额度不足。", endpoint),
                Loc.T("稍后再试，或检查配额。"));
        }

        if (statusCode >= 500)
        {
            return (
                Loc.T("{0} 返回 {1}，服务暂时不可用。", endpoint, statusCode),
                Loc.T("请确认服务已启动，稍后重试。"));
        }

        if (statusCode == null && ContainsAny(lower, "ssl", "tls", "certificate", "handshake", "proxy"))
        {
            return (
                Loc.T("连接 {0} 时发生 TLS/代理握手错误。", endpoint),
                Loc.T("请检查系统代理、VPN 节点与 HTTPS 证书设置。"));
        }

        if (statusCode is null or 408 && (lower.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase)))
        {
            return (
                Loc.T("连接 {0} 超时。", endpoint),
                Loc.T("请检查网络、防火墙和 Base URL。"));
        }

        if (statusCode == null && (lower.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("Name or service", StringComparison.OrdinalIgnoreCase)))
        {
            return (
                Loc.T("无法连接 {0}。", endpoint),
                Loc.T("请检查 Base URL 和本地服务是否已启动。"));
        }

        return (
            Loc.T("{0}请求 {1} / {2} 失败：{3}", RoleLabel(role), endpoint, modelName, Trim(lower, 180)),
            Loc.T("请按错误信息核对后重测。"));
    }

    private static bool IsQuotaExhaustedMessage(string text) =>
        ContainsAny(text, "insufficient_quota", "insufficient balance", "余额不足", "arrearage", "exceeded your current quota", "billing hard limit");

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string Trim(string text, int max)
    {
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
