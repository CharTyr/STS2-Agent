using STS2AIAgent.Config;

namespace STS2AIAgent.Multiplayer;

internal static class CoopLaunchPolicy
{
    public static string CompanionArguments(string? forceSteam, string? clientId)
    {
        if (!string.Equals(forceSteam, "off", StringComparison.OrdinalIgnoreCase)) return "--windowed";
        var arguments = "--windowed --force-steam off";
        if (ulong.TryParse(clientId, out var id))
        {
            if (id == ulong.MaxValue) throw new InvalidOperationException("Offline clientId leaves no adjacent companion ID.");
            arguments += " --clientId " + (id + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return arguments;
    }

    public static string? GetError(bool isCompanion, bool autoPlayRunning, string screen, ResolvedModel? model)
    {
        if (isCompanion) return "当前窗口已是 AI 队友。请在你的主窗口邀请队友。";
        if (autoPlayRunning) return "请先暂停当前角色的自动游玩，再邀请 AI 队友。";
        if (screen != "MAIN_MENU") return "请先回到主菜单，再邀请 AI 队友组队。";
        if (model == null || string.IsNullOrWhiteSpace(model.Model.Model))
            return "请先在设置中选择 AI 队友使用的游玩模型。";
        if (!Uri.TryCreate(model.Endpoint.BaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            return "模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。";
        return null;
    }
}
