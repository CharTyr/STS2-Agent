namespace STS2AIAgent.Config;

internal readonly record struct FirstRunStatus(
    bool ReadyToInvite,
    string Hint,
    string Phase,
    ModelRoleTestRecord Conversation,
    ModelRoleTestRecord Play,
    ModelRoleTestRecord Vision)
{
    public bool ProviderConfigReachable => Play.Status is "verified" or "failed" or "unverified" || Hint.Length > 0;
}

internal static class FirstRunSetup
{
    public const string SettingsHint =
        "请先在设置中填写 OpenAI 兼容接口地址和模型名称。本地 Ollama / LM Studio 可以留空 API Key。";

    public const string UnverifiedHint =
        "配置已填写，但尚未验证游玩模型。点「测试连接」会向配置的服务发送测试请求；通过后再邀请队友。";

    public const string InviteHint =
        "游玩模型已验证。回到主菜单打开「AI 队友」邀请。本地 1 人 + 1 AI 同一局：你打你的角色，AI 自动打另一个。大厅仍为 4 人位。";

    public static FirstRunStatus Evaluate(AgentSettings settings)
    {
        var conversation = ModelRoleProbe.Current(settings, ModelRoleNames.Conversation);
        var play = ModelRoleProbe.Current(settings, ModelRoleNames.Play);
        var vision = ModelRoleProbe.Current(settings, ModelRoleNames.Vision);
        var model = settings.TryResolvePlayModel();
        if (model == null || string.IsNullOrWhiteSpace(model.Model.Model))
        {
            return new FirstRunStatus(false, SettingsHint, "missing", conversation, play, vision);
        }

        if (!Uri.TryCreate(model.Endpoint.BaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return new FirstRunStatus(false, "模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。", "missing", conversation, play, vision);
        }

        if (play.Status == "failed")
        {
            var hint = play.Error + " " + play.NextStep;
            return new FirstRunStatus(false, hint.Trim(), "failed", conversation, play, vision);
        }

        if (play.Status == "verified")
        {
            return new FirstRunStatus(true, InviteHint, "verified", conversation, play, vision);
        }

        return new FirstRunStatus(false, UnverifiedHint, "filled_unverified", conversation, play, vision);
    }

    public static FirstRunStatus Evaluate(ResolvedModel? model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.Model.Model))
        {
            return new FirstRunStatus(
                false,
                SettingsHint,
                "missing",
                ModelRoleProbe.Unverified(ModelRoleNames.Conversation, null),
                ModelRoleProbe.Unverified(ModelRoleNames.Play, null),
                ModelRoleProbe.Unused(ModelRoleNames.Vision));
        }

        if (!Uri.TryCreate(model.Endpoint.BaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return new FirstRunStatus(
                false,
                "模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。",
                "missing",
                ModelRoleProbe.Unverified(ModelRoleNames.Conversation, model),
                ModelRoleProbe.Unverified(ModelRoleNames.Play, model),
                ModelRoleProbe.Unused(ModelRoleNames.Vision));
        }

        var filled = ModelRoleProbe.Unverified(ModelRoleNames.Play, model);
        return new FirstRunStatus(
            false,
            UnverifiedHint,
            "filled_unverified",
            filled,
            filled,
            ModelRoleProbe.Unused(ModelRoleNames.Vision));
    }
}
