using STS2AIAgent.Localization;

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
    public static string SettingsHint =>
        Loc.T("请先在设置中填写 OpenAI 兼容接口地址和模型名称。本地 Ollama / LM Studio 可以留空 API Key。");

    public static string UnverifiedHint =>
        Loc.T("配置已填写，但主模型尚未验证。在设置页点该模型卡片上的「测试」做一次真实调用（含工具调用检测）；通过后即可到「游玩」页开始自动游玩。");

    public static string InviteHint =>
        Loc.T("主模型已验证。到「游玩」页点「开始自动游玩」即可单人游玩；想和 AI 队友双打，把游玩页顶部切到「多人」再邀请：本地 1 人 + 1 AI 同一局，大厅仍为 4 人位。");

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
            return new FirstRunStatus(false, Loc.T("模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。"), "missing", conversation, play, vision);
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
                Loc.T("模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。"),
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
