namespace STS2AIAgent.Config;

internal readonly record struct FirstRunStatus(bool ReadyToInvite, string Hint)
{
    public bool ProviderConfigReachable => ReadyToInvite || Hint.Length > 0;
}

internal static class FirstRunSetup
{
    public const string SettingsHint =
        "请先在设置中填写 OpenAI 兼容接口地址和模型名称。本地 Ollama / LM Studio 可以留空 API Key。";

    public const string InviteHint =
        "配好游玩模型后，回到主菜单打开「AI 队友」，邀请第二实例加入。本地 1 人 + 1 AI，大厅仍为 4 人位。";

    public static FirstRunStatus Evaluate(AgentSettings settings)
    {
        return Evaluate(settings.TryResolvePlayModel());
    }

    public static FirstRunStatus Evaluate(ResolvedModel? model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.Model.Model))
        {
            return new FirstRunStatus(false, SettingsHint);
        }

        if (!Uri.TryCreate(model.Endpoint.BaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return new FirstRunStatus(false, "模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。");
        }

        return new FirstRunStatus(true, InviteHint);
    }
}
