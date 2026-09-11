using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Player-facing session views and model probe advice.
    private static void AddFacingEntries(Dictionary<string, string> map)
    {
        // Session view headlines, details and next-step hints (Agent/PlayerFacingSession.cs).
        map["队友窗口已退出"] = "Teammate window closed";
        map["关闭残留窗口后，回到主菜单再点「邀请 AI 队友」。"] =
            "Close the leftover window, then go back to the main menu and click “Invite AI teammate” again.";
        map["正在组队"] = "Pairing up";
        map["等待第二窗口连接。请勿重复点击邀请。"] =
            "Waiting for the second window to connect. Do not click Invite again.";
        map["已达到会话预算"] = "Session budget reached";
        map["配置有误，已停止"] = "Stopped: configuration error";
        map["打开「设置」修正端点、模型名或 Key，测试通过后再点「继续游玩」。不会自动重试。"] =
            "Open “Settings” and fix the endpoint, model name, or key, then test again before you click “Resume”. It will not retry on its own.";
        map["对局已结束"] = "Run ended";
        map["已离开当前对局，不会自动开新局。"] = "The run has ended; a new one will not start automatically.";
        map["若要再打一局，先回到主菜单自行开局，再继续或重新邀请。"] =
            "To play another run, go back to the main menu and start one yourself, then resume or invite again.";
        map["暂时连不上模型"] = "Cannot reach the model right now";
        map["检查网络或服务后，点「继续游玩」恢复。配置类错误不会无限重试。"] =
            "Check your network or the service, then click “Resume” to continue. Configuration errors are not retried endlessly.";
        map["自动游玩已停止"] = "Auto-play stopped";
        map["查看当前局面后点「继续游玩」。"] = "Review the current situation, then click “Resume”.";
        map["正在暂停"] = "Pausing";
        map["已提交的动作会先完成，不会再派发新的游戏动作。"] =
            "The action already submitted will finish first; no new game actions will be sent.";
        map["正在完成已提交的动作"] = "Finishing the submitted action";
        map["请稍候。这不是已取消；完成后会显示已暂停。"] =
            "Please wait. This is not a cancellation; it will show as paused once finished.";
        map["队友已暂停"] = "Teammate paused";
        map["仍可聊天。确认配置与队友仍在后，点「继续游玩」。"] =
            "You can still chat. Once you confirm the setup and teammate are fine, click “Resume”.";
        map["打开「设置」，按失败用途修正后再测试。"] =
            "Open “Settings”, fix the role that failed, then test again.";
        map["打开「设置」：添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试 → 邀请队友。"] =
            "Open “Settings”: add an endpoint → add and bind a model → assign the chat/play roles → test → invite your teammate.";
        map["可以邀请 AI 队友"] = "Ready to invite an AI teammate";
        map["回到主菜单，点「邀请 AI 队友」。"] =
            "Go back to the main menu and click “Invite AI teammate”.";
        map["正在等你"] = "Waiting for you";
        map["在你的窗口完成选择。这是正常等待，不是故障。"] =
            "Make your choice in your window. This is a normal wait, not a problem.";
        map["正在等游戏"] = "Waiting for the game";
        map["动画或转场结束后会继续。这是正常等待。"] =
            "It continues after the animation or transition ends. This is a normal wait.";
        map["正在请求模型"] = "Asking the model";
        map["可点「暂停队友」。暂停不会取消已经发出的模型请求，但不会再派发新动作。"] =
            "You can click “Pause teammate”. Pausing does not cancel a model request already in flight, but no new actions will be sent.";
        map["队友正在行动"] = "Teammate is playing";
        map["可随时暂停。你只操作自己的角色。"] = "You can pause at any time. You only control your own character.";
        map["队友已连接"] = "Teammate connected";
        map["需要时点「暂停队友」或继续聊天。"] = "Click “Pause teammate” when you need to, or keep chatting.";
        map["请求：{0} 次"] = "Requests: {0}";
        map["Token 消耗：尚无（未收到 usage） | {0}"] = "Token usage: none yet (no usage returned) | {0}";
        map["Token 消耗：未知（服务未返回 usage） | {0}"] = "Token usage: unknown (service returned no usage) | {0}";
        map["Token 消耗：{0} (Prompt: {1}, Completion: {2}) | {3}"] =
            "Token usage: {0} (prompt: {1}, completion: {2}) | {3}";
        map["等待当前任务结束。"] = "Waiting for the current task to finish.";
        map["已暂停自动游玩"] = "Auto-play paused";
        map["主窗口点「继续游玩」后才会再行动。"] =
            "It resumes only after you click “Resume” in the main window.";
        map["正常等待。"] = "Normal wait.";
        map["由主窗口控制暂停与继续。"] = "Pause and resume are controlled from the main window.";
        map["游玩配置验证失败"] = "Play setup check failed";
        map["配置尚未验证"] = "Setup not yet verified";
        map["可以邀请队友"] = "Ready to invite a teammate";
        map["还没有配好模型"] = "Models are not set up yet";

        // Model role connectivity probe: status labels, advice and classified errors
        // (Config/ModelRoleProbe.cs).
        map["未配置视觉模型，可跳过。"] = "No vision model configured; you can skip this.";
        map["在设置中测试该用途，确认服务可用后再邀请队友。"] =
            "Test this role in Settings, then invite your teammate once it works.";
        map["{0}连通成功。工具/视觉能力仍为未验证。"] =
            "{0} connected. Tool and vision capabilities are still unverified.";
        map["连通成功"] = "Connected";
        map["连通失败"] = "Connection failed";
        map["未使用"] = "Not used";
        map["尚未验证"] = "Not verified yet";
        map["能力：未使用"] = "Capability: not used";
        map["能力：未验证"] = "Capability: unverified";
        map["{0}：{1}{2}。{3}{4}"] = "{0}: {1}{2}. {3}{4}";
        map["游玩模型"] = "play model";
        map["视觉模型"] = "vision model";
        map["对话模型"] = "chat model";
        map["{0} 返回 {1}，认证失败。"] = "{0} returned {1}: authentication failed.";
        map["检查该端点的 API Key。本地 Ollama / LM Studio 可以留空 Key；云端服务需要有效密钥。"] =
            "Check this endpoint's API key. Local Ollama / LM Studio can leave the key blank; cloud services need a valid key.";
        map["{0} 返回 404，找不到模型 {1}。"] = "{0} returned 404: model {1} not found.";
        map["核对模型名是否与服务商目录一致，以及 Base URL 是否指向 /v1。"] =
            "Check that the model name matches the provider's catalog and that the base URL points to /v1.";
        map["{0} 返回 429，请求过于频繁或额度不足。"] =
            "{0} returned 429: too many requests, or the quota is exhausted.";
        map["稍后再测，或检查服务商配额。不要连续重试。"] =
            "Test again later or check your provider quota. Do not retry in a loop.";
        map["{0} 返回 {1}，服务暂时不可用。"] = "{0} returned {1}: the service is temporarily unavailable.";
        map["确认服务已启动后重试。这是临时错误，不是模型名填错。"] =
            "Make sure the service is running, then retry. This is a temporary error, not a wrong model name.";
        map["连接 {0} 超时。"] = "Connection to {0} timed out.";
        map["检查网络、防火墙以及 Base URL 是否可从本机访问。"] =
            "Check your network, your firewall, and whether the base URL is reachable from this machine.";
        map["无法连接 {0}。"] = "Could not connect to {0}.";
        map["检查 Base URL、本机网络，以及本地服务是否已启动。"] =
            "Check the base URL, your network, and whether the local service has started.";
        map["{0}请求 {1} / {2} 失败：{3}"] = "{0} request to {1} / {2} failed: {3}";
        map["根据错误核对端点、模型名和网络后，再对该用途单独测试。"] =
            "Use the error to check the endpoint, model name, and network, then test this role again.";

        // First-run setup hints (Config/FirstRunSetup.cs).
        map["请先在设置中填写 OpenAI 兼容接口地址和模型名称。本地 Ollama / LM Studio 可以留空 API Key。"] =
            "Start by filling in an OpenAI-compatible endpoint URL and model name in Settings. Local Ollama / LM Studio can leave the API Key blank.";
        map["配置已填写，但尚未验证游玩模型。点「测试连接」会向配置的服务发送测试请求；通过后再邀请队友。"] =
            "Your setup is filled in, but the play model is not verified yet. “Test connection” sends a test request to the configured service; invite your teammate after it passes.";
        map["游玩模型已验证。回到主菜单打开「AI 队友」邀请。本地 1 人 + 1 AI 同一局：你打你的角色，AI 自动打另一个。大厅仍为 4 人位。"] =
            "The play model is verified. Go back to the main menu and open “AI teammate” to invite. Local 1 human + 1 AI in one run: you play your character while the AI plays the other. The lobby still holds 4.";
        map["模型端点地址无效，请在设置中填写完整的 HTTP 或 HTTPS 地址。"] =
            "The model endpoint address is invalid. Enter a full HTTP or HTTPS address in Settings.";

        // Session budget copy (Config/SessionBudgetLimits.cs).
        map["重置本会话统计"] = "Reset session stats";
        map["会话预算必须是正整数；留空或 0 表示不限。已保留原来的安全上限。"] =
            "The session budget must be a positive whole number; leave it blank or 0 for unlimited. Your previous safe limit was kept.";
        map["打开「设置」→ 显示高级选项以提高上限，或点「重置本会话统计」后再点「继续游玩」。重置只清零本会话计数，不会改预算上限；普通暂停/继续不会清零。"] =
            "Open “Settings” → Show advanced options to raise the limit, or click “Reset session stats” and then “Resume”. Resetting only clears this session's counters; it does not raise the budget. Normal pause/resume does not clear them.";
        map["自动游玩进行中不能清零统计。请先点「暂停队友」，再在「设置」→ 显示高级选项提高上限，或暂停后点「重置本会话统计」。暂停/继续不会清零累计。"] =
            "You cannot clear stats while auto-play is running. Click “Pause teammate” first, then raise the limit under “Settings” → Show advanced options, or click “Reset session stats” after pausing. Pause/resume does not clear the totals.";
    }
}
