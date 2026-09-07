using System.Security.Cryptography;
using System.Text;
using STS2AIAgent.Llm;

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

    /// <summary>unverified | unused. Connectivity success never implies tools/vision.</summary>
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
        var raw = (model.Endpoint.BaseUrl ?? "") + "\n" + (model.Model.Model ?? "") + "\n" + (model.Endpoint.ApiKey ?? "");
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
            NextStep = role == ModelRoleNames.Vision ? "未配置视觉模型，可跳过。" : null
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
            NextStep = "在设置中测试该用途，确认服务可用后再邀请队友。"
        };
    }

    public static ModelRoleTestRecord FromSuccess(string role, ResolvedModel model)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = "verified",
            CapabilityStatus = "unverified",
            EndpointId = model.Endpoint.Id,
            EndpointName = model.Endpoint.Name,
            ModelId = model.Model.Id,
            ModelName = model.Model.Model,
            Fingerprint = Fingerprint(model),
            TestedAt = DateTimeOffset.UtcNow.ToString("o"),
            NextStep = RoleLabel(role) + "连通成功。工具/视觉能力仍为未验证。"
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

        if (stored != null && stored.Fingerprint == Fingerprint(resolved))
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

            if (resolved == null || stored.Fingerprint != Fingerprint(resolved))
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
            "verified" => "连通成功",
            "failed" => "连通失败",
            "unused" => "未使用",
            _ => "尚未验证"
        };
        var capability = record.CapabilityStatus == "unused" ? "能力：未使用" : "能力：未验证";
        var target = string.IsNullOrWhiteSpace(record.ModelName)
            ? ""
            : $" · {record.EndpointName ?? record.EndpointId} / {record.ModelName}";
        var error = string.IsNullOrWhiteSpace(record.Error) ? "" : " — " + record.Error;
        return $"{role}：{connectivity}{target}。{capability}{error}";
    }

    public static string FailureKind(int? statusCode, string? message)
    {
        if (statusCode is >= 400 and < 500 && statusCode is not 408 and not 429)
        {
            return "config";
        }

        if (statusCode == 408 || statusCode == 429 || statusCode >= 500)
        {
            return "network";
        }

        var text = message ?? string.Empty;
        if (ContainsAny(text,
                "unauthorized",
                "forbidden",
                "authentication",
                "credential",
                "api key",
                "apikey",
                "模型名",
                "模型不存在",
                "端点配置",
                "配置错误",
                "not configured",
                "invalid model",
                "unsupported model"))
        {
            return "config";
        }

        return "network";
    }

    public static string RoleLabel(string role) => role.ToLowerInvariant() switch
    {
        ModelRoleNames.Play => "游玩模型",
        ModelRoleNames.Vision => "视觉模型",
        _ => "对话模型"
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
        var endpoint = string.IsNullOrWhiteSpace(model.Endpoint.Name) ? model.Endpoint.BaseUrl : model.Endpoint.Name;
        var modelName = model.Model.Model;
        if (statusCode is 401 or 403)
        {
            return (
                $"{endpoint} 返回 {statusCode}，认证失败。",
                "检查该端点的 API Key。本地 Ollama / LM Studio 可以留空 Key；云端服务需要有效密钥。");
        }

        if (statusCode == 404)
        {
            return (
                $"{endpoint} 返回 404，找不到模型 {modelName}。",
                "核对模型名是否与服务商目录一致，以及 Base URL 是否指向 /v1。");
        }

        if (statusCode == 429)
        {
            return (
                $"{endpoint} 返回 429，请求过于频繁或额度不足。",
                "稍后再测，或检查服务商配额。不要连续重试。");
        }

        if (statusCode >= 500)
        {
            return (
                $"{endpoint} 返回 {statusCode}，服务暂时不可用。",
                "确认服务已启动后重试。这是临时错误，不是模型名填错。");
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase))
        {
            return (
                $"连接 {endpoint} 超时。",
                "检查网络、防火墙以及 Base URL 是否可从本机访问。");
        }

        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Name or service", StringComparison.OrdinalIgnoreCase))
        {
            return (
                $"无法连接 {endpoint}。",
                "检查 Base URL、本机网络，以及本地服务是否已启动。");
        }

        return (
            $"{RoleLabel(role)}请求 {endpoint} / {modelName} 失败：{Trim(message, 180)}",
            "根据错误核对端点、模型名和网络后，再对该用途单独测试。");
    }

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
