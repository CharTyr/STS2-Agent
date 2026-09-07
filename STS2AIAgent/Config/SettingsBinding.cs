namespace STS2AIAgent.Config;

internal readonly record struct SettingsBindingImpact(bool Blocked, string Message, IReadOnlyList<string> Roles)
{
    public static SettingsBindingImpact None { get; } = new(false, "", Array.Empty<string>());
}

internal static class SettingsBinding
{
    public static SettingsBindingImpact EndpointRemoval(AgentSettings settings, string endpointId)
    {
        var roles = new List<string>();
        var models = settings.Models.Count(model =>
            string.Equals(model.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));
        if (models > 0)
        {
            var modelIds = settings.Models
                .Where(model => string.Equals(model.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase))
                .Select(model => model.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            CollectRoleReferences(settings, modelIds, roles);
        }

        if (models == 0)
        {
            return SettingsBindingImpact.None;
        }

        var detail = $"仍有 {models} 个模型绑定此端点";
        if (roles.Count > 0)
        {
            detail += "，其中 " + string.Join("、", roles) + " 正在使用";
        }
        return new SettingsBindingImpact(
            true,
            "暂不能删除该端点（" + detail + "）。请先在设置中为这些模型选择其他端点并保存；重绑定完成后再删除。",
            roles);
    }

    public static SettingsBindingImpact ModelRemoval(AgentSettings settings, string modelId)
    {
        var roles = new List<string>();
        if (string.Equals(settings.ConversationModelId, modelId, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("对话模型");
        }

        var playId = string.IsNullOrWhiteSpace(settings.PlayModelId) ? settings.ConversationModelId : settings.PlayModelId;
        if (string.Equals(playId, modelId, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("游玩模型");
        }

        if (string.Equals(settings.VisionModelId, modelId, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("视觉模型");
        }

        if (roles.Count == 0)
        {
            return SettingsBindingImpact.None;
        }

        return new SettingsBindingImpact(
            true,
            "暂不能删除该模型（仍绑定 " + string.Join("、", roles) + "）。请先为这些用途选择其他模型并保存；重绑定完成后再删除。",
            roles);
    }

    private static void CollectRoleReferences(
        AgentSettings settings,
        IReadOnlySet<string> modelIds,
        List<string> roles)
    {
        if (modelIds.Contains(settings.ConversationModelId ?? string.Empty))
        {
            roles.Add(ModelRoleProbe.RoleLabel(ModelRoleNames.Conversation));
        }

        var playId = string.IsNullOrWhiteSpace(settings.PlayModelId)
            ? settings.ConversationModelId
            : settings.PlayModelId;
        if (modelIds.Contains(playId ?? string.Empty))
        {
            roles.Add(ModelRoleProbe.RoleLabel(ModelRoleNames.Play));
        }

        if (modelIds.Contains(settings.VisionModelId ?? string.Empty))
        {
            roles.Add(ModelRoleProbe.RoleLabel(ModelRoleNames.Vision));
        }
    }
}
