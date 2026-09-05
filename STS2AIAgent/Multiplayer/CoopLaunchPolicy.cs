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

    public static bool TryGetCompanionArguments(string? forceSteam, string? clientId, out string arguments, out string? error)
    {
        try
        {
            arguments = CompanionArguments(forceSteam, clientId);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            arguments = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    public static string CompanionSettingsPath(string mainSettingsPath, string? explicitCompanionPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitCompanionPath))
        {
            if (!System.IO.Path.IsPathFullyQualified(explicitCompanionPath))
                throw new InvalidOperationException("Companion settings path must be an absolute file path.");
            return System.IO.Path.GetFullPath(explicitCompanionPath);
        }

        if (string.IsNullOrWhiteSpace(mainSettingsPath))
            throw new ArgumentException("Main settings path must not be empty.", nameof(mainSettingsPath));

        var fullMain = System.IO.Path.GetFullPath(mainSettingsPath);
        var dir = System.IO.Path.GetDirectoryName(fullMain) ?? string.Empty;
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fullMain);
        var ext = System.IO.Path.GetExtension(fullMain);
        return System.IO.Path.Combine(dir, $"{nameWithoutExt}.companion{ext}");
    }

    public static void SeedCompanionSettings(string mainSettingsPath, string companionSettingsPath)
    {
        if (string.IsNullOrWhiteSpace(mainSettingsPath) || string.IsNullOrWhiteSpace(companionSettingsPath))
            return;

        if (string.Equals(System.IO.Path.GetFullPath(mainSettingsPath), System.IO.Path.GetFullPath(companionSettingsPath), StringComparison.OrdinalIgnoreCase))
            return;

        var dir = System.IO.Path.GetDirectoryName(companionSettingsPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        if (System.IO.File.Exists(mainSettingsPath) && !System.IO.File.Exists(companionSettingsPath))
            System.IO.File.Copy(mainSettingsPath, companionSettingsPath, overwrite: false);
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
