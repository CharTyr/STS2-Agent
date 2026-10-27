using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Player-facing session views and model probe advice.
    private static void AddFacingEntries(Dictionary<string, string> map)
    {
        map["设置已变更，等待队友确认…"] = "Settings changed; waiting for the teammate…";
        map["配置已变更，请重新测试。"] = "Settings changed; test again.";
        map["配置已填写，但主模型尚未验证。在「设置」页点「测试主模型」做一次真实调用；连通与工具调用通过后，即可开始自动游玩或邀请队友。"] = "Settings are filled in, but the main model is unverified. In Settings, click Test main model for a real request. Once connectivity and tool calling pass, you can start auto-play or invite a teammate.";
        map["队友模式"] = "Teammate mode";
        map["已有测试在进行，请稍候或取消。"] = "A test is running; wait or cancel it.";
        map["该模型不支持工具调用，无法自动游玩。请换模型再测。"] = "This model has no tool calling and cannot auto-play. Pick another and test again.";
        map["工具调用：可用"] = "Tools: supported";
        map["工具调用：不可用"] = "Tools: unsupported";
        map["工具调用可用。"] = "Tool calling works.";
        map["工具调用不可用，请换模型。"] = "No tool calling; try another model.";
        map["发现两份本地模组，请只保留一份完整安装：{0}"] = "Two local mod copies were found. Keep one complete installation: {0}";
        map["本地模组与主窗口版本不同，文件已保留。请更新或移走这份手动安装：{0}"] = "The local mod differs from the host. Files were preserved. Update or move this manual installation: {0}";
        map["模组文件缺失或为空：{0}。请重新安装完整发布包。"] = "A mod file is missing or empty: {0}. Reinstall the complete package.";
        map["模组清单不属于 STS2 AI Agent 或已损坏：{0}"] = "The manifest is invalid or does not belong to STS2 AI Agent: {0}";
        map["保存失败，队友设置未确认。"] = "Save failed; teammate settings unconfirmed.";
        map["队友设置已同步。"] = "Teammate settings synced.";
        map["请填写完整的 HTTP(S) 接口地址。"] = "Enter a full HTTP(S) API URL.";
        map["请填写模型名。"] = "Enter a model name.";
        map["模型名"] = "Model name";
        map["不限（留空或0）"] = "Unlimited (blank or 0)";
        map["快速配置"] = "Quick setup";
        map["服务商"] = "Provider";
        map["请求上限"] = "Request limit";
        map["正在测试…（最多 45 秒）"] = "Testing… (up to 45 s)";
        map["已取消或超时，配置已保留。"] = "Cancelled or timed out; settings kept.";
        map["已取消。"] = "Cancelled.";
        map["测试主模型"] = "Test main model";
        map["取消测试"] = "Cancel test";
        map["去游玩"] = "Go play";
        map["去设置"] = "Open settings";
        map["还没配置模型"] = "No model set up yet";
        map["接口地址无效"] = "Invalid API URL";
        map["模型测试没通过"] = "Model test failed";
        map["模型不支持工具调用"] = "Model has no tool calling";
        map["模型还没测试"] = "Model not tested yet";
        map["AI 自动游玩"] = "AI auto-play";
        map["外部 Agent"] = "External Agent";
        map["手动选队友角色"] = "Pick teammate character";
        map["外部 Agent 模式：队友入队后等待接管，不会自己出牌。"] = "External mode: the teammate waits to be taken over and will not play on its own.";
        map["在主菜单邀请，队友会自动选角并游玩。"] = "Invite from the main menu; the teammate picks a character and plays.";
        map["在主菜单邀请，到第二窗口为队友选角色。"] = "Invite from the main menu, then pick the teammate's character in the second window.";
        map["复制诊断"] = "Copy diagnostics";
        map["保存诊断"] = "Save diagnostics";
        map["诊断已保存：{0}"] = "Diagnostics saved: {0}";
        map["这是本地队友存档。已阻止会损坏存档的原生读档，请在模组多人页点击「继续上次联机对局」。"] = "This is a local teammate save. The unsafe native load was blocked. Use Continue last co-op run on the mod multiplayer page.";
        // Session view headlines, details and next-step hints (Agent/PlayerFacingSession.cs).
        map["队友窗口已退出"] = "Teammate window closed";
        map["关闭残留窗口，回主菜单重新邀请。"] =
            "Close the leftover window, then invite again from the main menu.";
        map["正在组队"] = "Teaming up";
        map["等待队友窗口连接，请勿重复邀请。"] =
            "Waiting for the teammate window. Do not invite again.";
        map["已达到会话预算"] = "Session budget reached";
        map["配置有误，已停止"] = "Stopped: configuration error";
        map["到「设置」修正端点、模型或 Key，测试通过后点「继续游玩」。"] =
            "Fix the endpoint, model or key in “Settings”, test, then press “Resume”.";
        map["对局已结束"] = "Run ended";
        map["已离开当前对局。"] = "You left the current run.";
        map["新开一局后，再继续或重新邀请。"] =
            "Start a new run first, then resume or invite again.";
        map["暂时连不上模型"] = "Cannot reach the model right now";
        map["检查网络或服务后点「继续游玩」。"] =
            "Check the network or service, then press “Resume”.";
        map["自动游玩已停止"] = "Auto-play stopped";
        map["看一眼局面，再点「继续游玩」。"] = "Check the board, then press “Resume”.";
        map["正在暂停"] = "Pausing";
        map["已提交的动作会先完成。"] =
            "The submitted action finishes first.";
        map["正在完成已提交的动作"] = "Finishing the submitted action";
        map["请稍候，完成后显示已暂停。"] =
            "Wait; it will show Paused when done.";
        map["队友已暂停"] = "Teammate paused";
        map["仍可聊天。准备好后点「继续游玩」。"] =
            "You can still chat. Press “Resume” when ready.";
        map["到「设置」修正后重新测试。"] =
            "Fix it in “Settings” and test again.";
        map["到「设置」配置并测试模型。"] =
            "Set up and test a model in “Settings”.";
        map["可以邀请 AI 队友"] = "Ready to invite an AI teammate";
        map["回主菜单邀请 AI 队友。"] =
            "Invite from the main menu.";
        map["正在等你"] = "Waiting for you";
        map["请在你的窗口完成选择。"] =
            "Make your choice in your window.";
        map["正在等游戏"] = "Waiting for the game";
        map["动画结束后继续。"] =
            "Continues after the animation.";
        map["正在请求模型"] = "Asking the model";
        map["可点「暂停队友」，已发出的请求不会被取消。"] =
            "You can pause; a request already sent is not cancelled.";
        map["队友正在行动"] = "Teammate is playing";
        map["可随时暂停。"] = "You can pause any time.";
        map["队友已连接"] = "Teammate connected";
        map["可点「暂停队友」或聊天。"] = "Pause the teammate or chat.";
        map["{0} 次请求"] = "{0} requests";
        map["暂无 Token · {0}"] = "No tokens yet · {0}";
        map["Token 未知 · {0}"] = "Tokens unknown · {0}";
        map["{0} Token · {3}"] =
            "{0} tokens · {3}";
        map["等待当前任务结束"] = "Waiting for the current task";
        map["已暂停自动游玩"] = "Auto-play paused";
        map["点「继续游玩」后继续。"] =
            "Press “Resume” to continue.";
        map["正常等待"] = "Normal wait";
        map["由主窗口控制"] = "Controlled from the main window";
        map["游玩配置验证失败"] = "Play setup check failed";
        map["配置尚未验证"] = "Setup not yet verified";
        map["可以邀请队友"] = "Ready to invite a teammate";
        map["还没有配好模型"] = "Models are not set up yet";

        // Model role connectivity probe: status labels, advice and classified errors
        // (Config/ModelRoleProbe.cs).
        map["未配置视觉模型（可跳过）"] = "No vision model (optional)";
        map["到「设置」测试后再邀请队友。"] =
            "Test it in “Settings” before inviting a teammate.";
        map["{0}已连通，能力未验证。"] =
            "{0} connected; capabilities unverified.";
        map["连通成功"] = "Connected";
        map["连通失败"] = "Connection failed";
        map["未使用"] = "Not used";
        map["尚未验证"] = "Not verified yet";
        map["能力：未使用"] = "Capability: not used";
        map["能力：未验证"] = "Capability: unverified";
        map["{0}：{1}{2} · {3}{4}"] = "{0}: {1}{2} · {3}{4}";
        map["游玩模型"] = "play model";
        map["视觉模型"] = "vision model";
        map["对话模型"] = "chat model";
        map["{0} 返回 {1}，认证失败。"] = "{0} returned {1}: authentication failed.";
        map["请检查该端点的 API Key。"] =
            "Check this endpoint's API key.";
        map["{0} 返回 404，找不到模型 {1}。"] = "{0} returned 404: model {1} not found.";
        map["请核对模型名和 Base URL（通常以 /v1 结尾）。"] =
            "Check the model name and base URL (usually ending in /v1).";
        map["{0} 返回 429，请求过于频繁或额度不足。"] =
            "{0} returned 429: too many requests, or your quota is exhausted.";
        map["稍后再试，或检查配额。"] =
            "Try later or check your quota.";
        map["{0} 返回 {1}，服务暂时不可用。"] = "{0} returned {1}: the service is temporarily unavailable.";
        map["请确认服务已启动，稍后重试。"] =
            "Make sure the service is running, then retry.";
        map["连接 {0} 超时。"] = "Connection to {0} timed out.";
        map["请检查网络、防火墙和 Base URL。"] =
            "Check your network, firewall and base URL.";
        map["无法连接 {0}。"] = "Could not connect to {0}.";
        map["请检查 Base URL 和本地服务是否已启动。"] =
            "Check the base URL and that the local service is running.";
        map["{0}请求 {1} / {2} 失败：{3}"] = "{0} request to {1} / {2} failed: {3}";
        map["请按错误信息核对后重测。"] =
            "Check against the error, then test again.";

        // First-run setup hints (Config/FirstRunSetup.cs).
        map["请先在「设置」填写接口地址和模型名。"] =
            "Enter an API URL and model name in “Settings” first.";
        map["配置已填写，但尚未验证游玩模型。点「测试连接」会向配置的服务发送测试请求；通过后再邀请队友。"] =
            "Your setup is filled in, but the play model is not verified yet. “Test connection” sends a test request to the configured service; invite your teammate after it passes.";
        map["配置已填写，但主模型尚未验证。在设置页点该模型卡片上的「测试」做一次真实调用（含工具调用检测）；通过后即可到「游玩」页开始自动游玩。"] =
            "Your setup is filled in, but the main model is not verified yet. On the Settings page, press “Test” on that model's card for a real call (including a tool-calling probe); once it passes, go to the “Play” page to start auto-play.";
        map["游玩模型已验证。回到主菜单打开「AI 队友」邀请。本地 1 人 + 1 AI 同一局：你打你的角色，AI 自动打另一个。大厅仍为 4 人位。"] =
            "The play model is verified. Go back to the main menu and open the “AI teammate” page to invite. Local play is 1 human + 1 AI in one run: you play your character while the AI plays the other. The lobby still holds 4.";
        map["主模型已验证。到「游玩」页点「开始自动游玩」即可单人游玩；想和 AI 队友双打，把游玩页顶部切到「多人」再邀请：本地 1 人 + 1 AI 同一局，大厅仍为 4 人位。"] =
            "The main model is verified. Press “Start auto-play” on the “Play” page for solo play; for co-op with the AI teammate, switch the top of the Play page to “Multiplayer” and invite: local play is 1 human + 1 AI in one run, and the lobby still holds 4.";
        map["接口地址无效，请填写完整的 HTTP(S) 地址。"] =
            "Invalid API URL; enter a full HTTP(S) address.";

        // Session budget copy (Config/SessionBudgetLimits.cs).
        map["清零计数"] = "Clear counters";
        map["快速判断 {0} 次 · LLM 接手 {1} 次"] = "Fast judgment {0} · LLM took over {1}";
        map["会话预算必须是正整数；留空或 0 表示不限。已保留原来的安全上限。"] =
            "The session budget must be a positive whole number; leave it blank or 0 for unlimited. Your previous safe limit was kept.";
        map["点「清零计数」后再继续游玩；想一次玩更久，到「设置」调高「请求上限」。清零只重置本次会话的计数，不改上限。"] =
            "Press “Clear counters”, then resume play; to play longer at a time, raise “Request limit” in “Settings”. Clearing only resets this session's counters; the limit stays.";
        map["自动游玩还在收尾，暂停后才能点「清零计数」；也可以到「设置」调高「请求上限」。"] =
            "Auto-play is still finishing; pause before pressing “Clear counters”, or raise “Request limit” in “Settings”.";
    }
}
