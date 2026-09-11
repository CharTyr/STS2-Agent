using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Runtime status, errors, chat and dual-instance messages (Agent/AgentRuntime.cs).
    private static void AddRuntimeEntries(Dictionary<string, string> map)
    {
        // Status line.
        map["就绪"] = "Ready";
        map["自动游玩中"] = "Auto-playing";
        map["已暂停自动游玩"] = "Auto-play paused";
        map["正在暂停，等待当前任务完成…"] = "Pausing; waiting for the current task to finish…";
        map["正在请求模型…"] = "Requesting the model…";
        map["已执行 {0}"] = "Executed {0}";
        map["等待游戏可操作"] = "Waiting for the game";
        map["等待可操作状态"] = "Waiting for an actionable state";
        map["等待你选择地图节点，随后投同一格。"] = "Waiting for you to pick a map node; I will vote for the same one.";
        map["跟随你的地图选择。"] = "Following your map choice.";
        map["确认阻挡操作的教学弹窗。"] = "Confirmed the tutorial popup that was blocking the action.";

        // Single step.
        map["单步决策中"] = "Deciding the single step";
        map["单步已取消"] = "Single step cancelled";
        map["单步失败：{0}"] = "Single step failed: {0}";
        map["自动游玩中，请先暂停再单步"] = "Auto-play is running; pause before single step";

        // Chat and session stats.
        map["(无文本回复)"] = "(no text reply)";
        map["对话完成"] = "Chat finished";
        map["对话出错"] = "Chat error";
        map["对话失败"] = "Chat failed";
        map["对话已取消"] = "Chat cancelled";
        map["请求失败：{0}"] = "Request failed: {0}";
        map["自动游玩进行中。请先暂停，再对话或代打。"] = "Auto-play is running. Pause it before you chat or play on your behalf.";
        map["自动游玩进行中，不能清零本会话统计。请先暂停。暂停/继续不会清零累计。"] = "Auto-play is running, so session stats cannot be reset. Pause first. Pause and Resume do not clear the totals.";
        map["已清零本会话统计。预算上限未改；继续游玩将重新计数。"] = "Session stats cleared. The budget limit is unchanged; resuming counts from zero.";

        // Model test, connection and settings.
        map["正在测试模型…会向配置的服务发送测试请求。"] = "Testing the model… a test request will be sent to the configured service.";
        map["游玩模型测试失败"] = "Play model test failed";
        map["游玩模型连通成功（不等于工具/视觉已验证）"] = "Play model connected (this does not verify tools or vision)";
        map["模型尚未验证"] = "Model not verified yet";
        map["连通失败"] = "Connection failed";
        map["设置保存失败，原配置文件未被覆盖。请检查磁盘空间或文件占用后重试。"] = "Could not save Settings; the existing file was left untouched. Check disk space or file locks and try again.";
        map["配置错误"] = "Configuration error";

        // Dual instance and teammate.
        map["尚未启动双开。"] = "Dual-instance has not started.";
        map["队友控制尚未连接。"] = "Teammate control is not connected yet.";
        map["正在请求队友继续…"] = "Asking your teammate to resume…";
        map["正在等待队友暂停；已提交的动作会先完成。"] = "Waiting for your teammate to pause; the submitted action will finish first.";
        map["请等待组队完成。"] = "Wait for teaming up to finish.";
        map["队友进程已退出。请回到主菜单重新邀请。"] = "The teammate process exited. Go back to the main menu and invite again.";
        map["请先邀请 AI 队友。"] = "Invite an AI teammate first.";
        map["队友已暂停。仍然可以聊天，点击继续后才会自动行动。"] = "Your teammate is paused. You can still chat; it resumes only after you click Resume.";
        map["队友正在自动游玩。"] = "Your teammate is auto-playing.";
        map["队友仍在停止当前任务，请稍后再次确认暂停。"] = "Your teammate is still stopping the current task. Try pausing again in a moment.";
        map["未确认队友控制结果：{0}"] = "Teammate control was not confirmed: {0}";
        map["队友尚未完成组队，请稍后继续。"] = "Your teammate has not finished teaming up. Resume in a moment.";
        map["正在组队，请等待 AI 队友连接完成。"] = "Teaming up; wait for the AI teammate to connect.";
        map["正在组队，请等待连接完成后发送消息。"] = "Teaming up; wait for the connection before sending a message.";
        map["请先邀请 AI 队友。此处消息只发送给本次邀请的队友。"] = "Invite an AI teammate first. Messages here go only to the teammate invited this time.";
        map["消息正在送往队友；若它正在行动，会在本次行动完成后回复。"] = "Sending the message to your teammate; if it is acting, it replies after the current action.";
        map["队友已回复。你的建议会作为后续决策的参考。"] = "Your teammate replied. Your advice will guide its later decisions.";
        map["队伍消息未确认完成：{0}"] = "Team message was not confirmed: {0}";
        map["队友未返回文本回复；建议已记录供后续决策参考。"] = "Your teammate returned no text reply; your advice was recorded for later decisions.";
        map["组队后，可以在这里和 AI 队友商量打法。"] = "After teaming up, you can discuss tactics with your AI teammate here.";
        map["正在检查组队条件…"] = "Checking team-up requirements…";
        map["请等待当前队伍消息完成，再重新组队。"] = "Wait for the current team message to finish, then team up again.";
        map["正在邀请 AI 队友，等待游戏窗口连接…"] = "Inviting your AI teammate; waiting for the game window to connect…";
        map["已取消等待队友连接；若队友窗口已打开，请在该窗口确认状态。"] = "Stopped waiting for the teammate to connect; if its window is open, confirm the status there.";
        map["邀请队友失败：{0}"] = "Could not invite the teammate: {0}";
        map["队伍对话已重置。确认队友连接后，可以商量这次冒险的打法。"] = "Team chat was reset. Once your teammate is connected, you can discuss how to play this run.";
        map["同伴实例：正在加入大厅"] = "Companion instance: joining the lobby";
        map["同伴实例加入大厅失败"] = "Companion instance failed to join the lobby";
        map["自动游玩已停止：{0}"] = "Auto-play stopped: {0}";

        // MCP endpoint.
        map["MCP 已打开。把下面的地址或配置贴进外部客户端。"] = "MCP is on. Paste the address or config below into an external client.";
        map["MCP 已关闭，未对外暴露。"] = "MCP is off; nothing is exposed.";
    }
}
