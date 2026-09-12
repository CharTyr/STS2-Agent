using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Settings, multiplayer, diagnostics, MCP launcher and budget copy.
    private static void AddSupportEntries(Dictionary<string, string> map)
    {
        // Config/SettingsBinding.cs
        map["、"] = ", ";
        map["仍有 {0} 个模型绑定此端点"] = "{0} models still use this endpoint";
        map["，其中 {0} 正在使用"] = ", and these roles are in use: {0}";
        map["暂不能删除该端点（{0}）。请先在设置中为这些模型选择其他端点并保存；重绑定完成后再删除。"] =
            "Can't delete this endpoint yet ({0}). Choose another endpoint for these models in Settings and save; delete it after rebinding.";
        map["对话模型"] = "chat model";
        map["游玩模型"] = "play model";
        map["视觉模型"] = "vision model";
        map["暂不能删除该模型（仍绑定 {0}）。请先为这些用途选择其他模型并保存；重绑定完成后再删除。"] =
            "Can't delete this model yet (still used by {0}). Choose another model for these roles in Settings and save; delete it after rebinding.";

        // Config/SettingsStore.cs
        map["保存失败，原配置文件未被覆盖。"] = "Saving failed. The existing settings file was not overwritten.";
        map["配置读取失败，已备份原文件并恢复上次成功保存的配置。"] =
            "Reading settings failed. The original file was backed up and your last saved settings were restored.";
        map["配置读取失败，已备份原文件并改用默认配置。可用备份恢复。"] =
            "Reading settings failed. The original file was backed up and defaults are now in use; you can restore from the backup.";
        map["配置读取失败，且未能备份原文件，未覆盖现有配置。"] =
            "Reading settings failed, and the original file could not be backed up. Existing settings were left untouched.";

        // Multiplayer/CompanionConnection.cs
        map["请输入 1–2000 个字符的队伍消息。"] = "Enter a team message of 1–2000 characters.";
        map["队友没有返回文本。"] = "Your teammate returned no text.";
        map["AI 队友连接已改变，请重新组队。"] = "The AI teammate connection changed. Invite your teammate again.";
        map["队友未能确认请求，请查看队友窗口中的状态。请求不会自动重发。"] =
            "Your teammate did not confirm the request. Check the status in the teammate window; the request is not retried automatically.";

        // Multiplayer/CoopLaunchPolicy.cs
        map["当前窗口已是 AI 队友。请在你的主窗口邀请队友。"] =
            "This window is already the AI teammate. Invite your teammate from the main window.";
        map["请先暂停当前角色的自动游玩，再邀请 AI 队友。"] =
            "Pause auto-play for the current character before inviting an AI teammate.";
        map["请先回到主菜单，再邀请 AI 队友组队。"] = "Return to the main menu before inviting an AI teammate.";
        map["模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。"] =
            "The model endpoint address is invalid. Enter a full HTTP or HTTPS address in Settings.";

        // Multiplayer/DualInstanceCoordinator.cs
        map["创建 4 人大厅失败：{0}"] = "Could not create the 4-player lobby: {0}";
        map["{0}。本机已创建 4 人大厅，请选角色后 Ready 开局。你打自己的角色；AI 会自动加入、点开局并打另一个角色。"] =
            "{0}. A 4-player local lobby is ready. Pick your character and press Ready to start; you play your own character while the AI joins, starts the run, and plays the other one.";
        map["找不到 FastHost 命令行参数表。"] = "Could not find the FastHost command-line argument table.";
        map["FastHost 命令行参数表类型无法写入：{0}"] =
            "Cannot write to the FastHost command-line argument table type: {0}";
        map["写入 -fastmp 后 HasArg 仍为 false。type={0}"] = "HasArg is still false after writing -fastmp. type={0}";

        // Multiplayer/LocalDualInstanceLauncher.cs
        map["正在邀请 AI 队友，请等待连接结果。"] = "Inviting the AI teammate. Wait for the connection result.";
        map["AI 队友窗口已经在运行。请查看该窗口；若要重新组队，请先正常关闭它。"] =
            "The AI teammate window is already running. Check that window; to invite again, close it normally first.";
        map["找不到游戏可执行文件。"] = "Could not find the game executable.";
        map["无法计算队友启动参数：{0}"] = "Could not compute the teammate launch arguments: {0}";
        map["无法配置队友设置文件路径：{0}"] = "Could not set up the teammate settings file path: {0}";
        map["启动第二实例失败。Steam 可能阻止了双开：{0}"] =
            "Could not start the second instance. Steam may be blocking a second copy: {0}";
        map["AI 队友进程已退出（退出码 {0}）。请检查游戏日志与 Steam 双开限制后重试。"] =
            "The AI teammate process exited (exit code {0}). Check the game log and Steam's multi-instance limits, then try again.";
        map["AI 队友进程仍在运行（PID {0}），但未能确认连接。请检查队友窗口和游戏日志，不要重复启动。"] =
            "The AI teammate process is still running (PID {0}), but the connection could not be confirmed. Check the teammate window and the game log, and do not start it again.";
        map["第二实例已就绪：PID {0}，API {1}"] = "Second instance ready: PID {0}, API {1}";

        // Multiplayer/TeamConversation.cs
        map["队伍消息需要包含 1–{0} 个字符。"] = "A team message must be 1–{0} characters long.";

        // Agent/McpProcessLauncher.cs
        map["mcp_server 目录无效。需要包含 pyproject.toml 和 src/sts2_mcp/server.py。"] =
            "The mcp_server folder is invalid. It must contain pyproject.toml and src/sts2_mcp/server.py.";
        map["未找到 uv。请先安装 https://docs.astral.sh/uv/ 并确保 uv 在 PATH 中。"] =
            "uv was not found. Install it from https://docs.astral.sh/uv/ and make sure uv is on your PATH.";
        map["找不到空闲 MCP 端口：{0}"] = "Could not find a free MCP port: {0}";
        map["启动 MCP 失败：{0}"] = "Could not start MCP: {0}";
        map["MCP 进程已启动但 http://127.0.0.1:{0}/healthz 未就绪。请确认已 uv sync。"] =
            "The MCP process started, but http://127.0.0.1:{0}/healthz is not ready. Make sure you have run uv sync.";
        map["MCP 启动失败：{0}"] = "MCP failed to start: {0}";
        map["MCP 已启动：http://127.0.0.1:{0}/mcp"] = "MCP started: http://127.0.0.1:{0}/mcp";

        // Agent/DiagnosticExport.cs
        map["STS2 AI Agent 诊断（已脱敏）"] = "STS2 AI Agent diagnostics (redacted)";
        map["已排除 API Key、Authorization 头和会话令牌。"] =
            "API keys, Authorization headers, and session tokens are excluded.";
        map["默认不包含对话或队伍聊天正文。"] = "Chat and team chat messages are not included by default.";

        // Agent/SessionBudgetGuard.cs
        map["已达到会话请求次数上限（{0}/{1} 次），已自动停止游玩。"] =
            "Session request limit reached ({0}/{1}); auto-play stopped.";
        map["已达到会话 Token 预算上限（{0:N0}/{1:N0} tokens），已自动停止游玩。"] =
            "Session token budget reached ({0:N0}/{1:N0} tokens); auto-play stopped.";

        // Agent/AutoPlayRecovery.cs
        map["请检查模型、端点或凭据后再继续：{0}"] =
            "Check the model, endpoint, or credentials before continuing: {0}";
        map["模型未给出可执行动作"] = "The model did not return a usable action";
        map["连续 3 次决策未成功，已停止自动游玩。检查当前局面后可手动继续：{0}"] =
            "3 decisions in a row failed, so auto-play stopped. Review the current situation, then resume manually: {0}";
        map["连续 {0} 次重复同一个动作且状态没有变化，已停止自动游玩。检查当前局面后可手动继续：{1}"] =
            "{0} identical actions in a row left the state unchanged, so auto-play stopped. Review the current situation, then resume manually: {1}";
        map["连续 {0} 次动作已执行但界面一直没有稳定，已停止自动游玩。检查当前局面后可手动继续：{1}"] =
            "{0} actions in a row executed but the game never settled, so auto-play stopped. Review the current situation, then resume manually: {1}";

        // Agent/CurrentRunBoundary.cs (constants stay Chinese; wrapped at the throw site)
        map["当前局已离开，自动游玩已停止。开始另一局需要手动继续。"] =
            "You have left the current run, so auto-play stopped. Resume manually to start another run.";
        map["检测到对局标识变化，已停止自动游玩。请确认当前局后再继续。"] =
            "The run identity changed, so auto-play stopped. Confirm the current run before resuming.";

        // Agent/ProactiveChatPolicy.cs
        map["轻松搭档"] = "Easygoing partner";
        map["沉稳参谋"] = "Steady advisor";
        map["简短简报"] = "Short briefing";
    }
}
