using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Overlay text: headers, tabs, buttons, labels, hints (Ui/AgentOverlayHost.cs).
    private static void AddUiEntries(Dictionary<string, string> map)
    {
        // Header, tabs, chat page.
        map["拖动移动"] = "Drag to move";
        map["隐藏"] = "Hide";
        map["● 自动游玩中"] = "● Auto-playing";
        map["○ 待机"] = "○ Idle";
        map["和 AI 聊聊这局"] = "Ask the AI about this run";
        map["问它这手牌怎么打、这个遗物值不值得买、刚才那步为什么那么出。"] =
            "Ask how to play this hand, whether that relic is worth buying, or why it made the move it just made.";
        map["对话默认只读；要它真的动手，勾选下方的「允许代打」。"] =
            "Chat is read-only by default; tick “Let the AI play for you” below to let it actually act.";
        map["正在自动决策并执行动作。"] = "Deciding and acting on its own.";
        map["等待开始。"] = "Waiting to start.";
        map["还没有思考记录。开始自动游玩或单步一次后会显示。"] =
            "No reasoning recorded yet. Start auto-play, or take a single step, and it will show up here.";
        map["屏幕"] = "Screen";
        map["最近动作"] = "Last action";
        map["思考"] = "Reasoning";
        map["本次会话"] = "This session";
        map["本局"] = "This run";
        map["跳到「{0}」"] = "Jump to “{0}”";
        map["对话"] = "Chat";
        map["设置"] = "Settings";
        map["游玩"] = "Play";
        map["AI 队友"] = "AI teammate";
        map["接入"] = "Connect";
        map["附带当前状态"] = "Attach current state";
        map["附带截图（视觉）"] = "Attach screenshot (vision)";
        map["允许代打"] = "Let the AI play for you";
        map["发送"] = "Send";
        map["清空"] = "Clear";
        map["在下方输入后点发送。"] = "Type below, then press Send.";
        map["你"] = "You";
        map["助手"] = "Assistant";

        // Settings page buttons.
        map["添加端点"] = "Add endpoint";
        map["添加模型"] = "Add model";
        map["保存设置"] = "Save settings";
        map["测试连接"] = "Test connection";

        // Section headings and the overlay theme selector.
        map["外观"] = "Appearance";
        map["界面主题"] = "Overlay theme";
        map["只改这个窗口的配色，保存后立即生效。"] = "Changes this window's colours only; saving applies it straight away.";
        map["暮色"] = "Dusk";
        map["午夜"] = "Midnight";
        map["羊皮纸"] = "Parchment";
        map["高对比"] = "High contrast";
        map["猩红"] = "Crimson";
        map["翡翠"] = "Emerald";
        map["紫晶"] = "Amethyst";
        map["象牙"] = "Ivory";
        map["当前回合"] = "This turn";
        map["本步详情"] = "Last step";
        map["组队状态"] = "Party";
        map["邀请与控制"] = "Invite and control";
        map["队友控制"] = "Teammate control";
        map["服务开关"] = "Service";
        map["客户端配置"] = "Client configuration";
        map["用量"] = "Usage";
        map["决策记录"] = "Decisions";

        // Play page.
        map["状态：-"] = "Status: -";
        map["屏幕：-"] = "Screen: -";
        map["最近动作：-"] = "Last action: -";
        map["思考：-"] = "Thinking: -";
        map["Token 消耗：-"] = "Token usage: -";
        map["开始自动游玩"] = "Start auto-play";
        map["暂停自动游玩"] = "Pause auto-play";
        map["单步"] = "Single step";
        map["状态：{0}"] = "Status: {0}";
        map["屏幕：{0}"] = "Screen: {0}";
        map["最近动作：{0}"] = "Last action: {0}";
        map["思考：{0}"] = "Thinking: {0}";
        map["自动游玩走 compact 状态和工具，与 MCP 相同，不需要视觉即可打完全部流程。对话默认只读；勾选「允许代打」或明确说「帮我打」才会执行动作。"] =
            "Auto-play uses the same compact state and tools as MCP, so vision is optional — it can play through the entire run without it. Chat is read-only by default; actions run only if you check “Let the AI play for you” or explicitly ask it to play for you.";

        // AI teammate page.
        map["和 AI 一起爬塔"] = "Climb the tower with AI";
        map["可以一起玩：你打自己的角色，AI 打另一个角色，同一座塔往上爬。大厅仍是 4 人位，还可以再邀 2 名在线玩家。"] =
            "Play together: you play your character, the AI plays another, and you climb the same tower. The lobby still holds 4, so you can invite 2 more online players.";
        map["下一步：-"] = "Next step: -";
        map["下一步：{0}"] = "Next step: {0}";
        map["请从主菜单邀请。第二窗口打开后，AI 会自己选角、点开局事件并进图。你继续在这个窗口操作自己的角色；轮到它时，它会自动出牌。"] =
            "Invite from the main menu. Once the second window opens, the AI picks its character, gets through the opening event, and enters the map on its own. Keep playing your character in this window; the AI plays its cards when its turn comes.";
        map["导出诊断"] = "Export diagnostics";
        map["邀请 AI 队友"] = "Invite AI teammate";
        map["正在邀请队友…"] = "Inviting teammate…";
        map["队友尚未加入。"] = "No teammate has joined yet.";
        map["禁用自动选角"] = "Disable automatic character pick";
        map["请从主菜单邀请。第二窗口打开后，会停在选角界面让 AI 自己决定选角，也可以你切过去给它选好、点出发；之后它自己点开局事件并进图。"] =
            "Invite from the main menu. The second window waits on the character screen for the AI to decide its character; you can also switch over, pick for it, and press Embark. It then gets through the opening event and enters the map on its own.";
        map["继续上次联机对局"] = "Continue the saved co-op run";
        map["正在读档接回队友…"] = "Loading the save and bringing the teammate back…";
        map["主菜单上有联机存档时可用。"] = "Available from the main menu when a co-op save exists.";
        map["请从主菜单邀请。第二窗口打开后，AI 会加入并进图，然后停在原地等待外部接管，不会自己出牌；你继续在这个窗口操作自己的角色。"] =
            "Invite from the main menu. Once the second window opens, the AI joins and enters the run, then stops and waits for an external client to take over — it will not play on its own; keep playing your character in this window.";
        map["游玩模型未配置或未验证：仍然可以邀请，队友会加入并进图，然后停在原地等待外部接管，不会自己出牌。想让它自己打，先在设置里配好模型并通过「测试连接」。"] =
            "No verified play model: you can still invite. The teammate joins and enters the run, then stops and waits for an external client to take over; it will not play on its own. To let it play on its own, configure a model in Settings and pass “Test connection” first.";
        map["暂停队友"] = "Pause teammate";
        map["继续游玩"] = "Resume";
        map["队伍交流"] = "Team chat";
        map["一起集火哪个敌人？这条路线你怎么看？"] = "Which enemy should we focus? What do you think of this route?";
        map["和队友说"] = "Message teammate";
        map["队友实况：{0}"] = "Teammate now: {0}";
        map["队友实况：组队后显示。"] = "Teammate now: shown once you team up.";
        map["队友实况：读取中…"] = "Teammate now: reading…";
        map["未知"] = "unknown";
        map["{0}：{1} HP"] = "{0}: {1} HP";
        map["，{0} 格挡"] = ", {0} block";
        map["（已倒下）"] = " (down)";
        map["，{0} 能量"] = ", {0} energy";
        map["手牌 {0} 张"] = "{0} cards in hand";
        map["队友"] = "Teammate";
        map["等待队友回复…"] = "Waiting for teammate…";
        // The focus-fire constraint the companion's loop receives when the teammate announced a target.
        map["队友本回合在打 enemy_index {0}。除非那个敌人已经必死或只剩最后一击，不要把伤害再倾泻在它身上；优先选另一个目标，避免两人重复集火把伤害溢出掉。"] =
            "Your teammate is attacking enemy_index {0} this turn. Unless that enemy is already certain to die or is one hit from it, do not pour more damage into it; pick another target so the two of you do not waste damage on the same kill.";
        map["聊天不会替你出牌，也不会恢复已暂停的队友。建议会供队友下一次决策参考。"] =
            "Chat will not play cards for you or resume a paused teammate. Your advice feeds the teammate's next decision.";
        map["如果队友窗口未能连接，请检查游戏日志和 Steam 双开限制。"] =
            "If the teammate window fails to connect, check the game log and Steam's multi-instance limits.";
        map["诊断已复制到剪贴板（不含 API Key 和对话正文）。"] =
            "Diagnostics copied to the clipboard (no API keys or chat content).";

        // Decision log tab: what the agent did, why, and what this session has spent.
        map["决策日志"] = "Decision log";
        map["最新在前：动作、理由、来源，以及该步消耗的 Token。"] =
            "Newest first: the action, its reason, the source, and the tokens that step spent.";
        map["理由：{0}"] = "Reason: {0}";
        map["来源：{0}"] = "Source: {0}";
        map["本次 Token：{0}"] = "Tokens this step: {0}";
        map["本次 Token：未知"] = "Tokens this step: unknown";
        map["还没有决策记录。自动游玩或外部客户端执行动作后会出现在这里。"] =
            "No decisions recorded yet. They appear here once auto-play or an external client runs an action.";
        // A session can outlive a run, so the tab shows both totals.
        map["本局：-"] = "This run: -";
        map["本局：尚未识别到对局。"] = "This run: no run identified yet.";
        map["本局（{0}）：暂无决策记录。"] = "This run ({0}): no decisions recorded yet.";
        map["本局（{0}）：{1} 次决策，{2} tokens。"] = "This run ({0}): {1} decisions, {2} tokens.";
        map["本局（{0}）：{1} 次决策，Token 未知。"] = "This run ({0}): {1} decisions, token spend unknown.";

        // Connect page.
        map["MCP 接入"] = "MCP Connect";
        map["选择：一起玩只用游戏内窗口，不必打开 MCP。外部客户端用本页开关。Python sidecar 仅 stdio / layered / full。"] =
            "Which do you need? Playing together only needs the in-game window, so MCP can stay off. Use the switch on this page for external clients. The Python sidecar supports only stdio / layered / full.";
        map["游戏内自动打不需要打开。只有 Cursor / Claude / Codex 等外部客户端才需要。"] =
            "In-game auto-play does not need this. Only external clients such as Cursor / Claude / Codex do.";
        map["打开 MCP 服务"] = "Turn on MCP";
        map["复制地址"] = "Copy URL";
        map["复制配置"] = "Copy config";
        map["把配置贴进外部客户端的 MCP 设置。服务只监听本机 127.0.0.1。"] =
            "Paste the config into your external client's MCP settings. The service listens only on 127.0.0.1.";
        map["本机 HTTP API 始终可用：GET /health /state ，POST /action。MCP 打开后才会在同一端口暴露 /mcp。地址以本页复制为准，不要写死 8080 或 8765。"] =
            "The local HTTP API is always available: GET /health and /state, POST /action. /mcp appears on the same port only after MCP is turned on. Copy the address from this page instead of hardcoding 8080 or 8765.";
        map["地址：{0}"] = "URL: {0}";

        // Settings form, endpoint and model cards.
        map["未保存"] = "Unsaved";
        map["已保存"] = "Saved";
        map["已保存（预算未改：{0}）"] = "Saved (budget unchanged: {0})";
        map["保存失败，原配置未被覆盖。"] = "Save failed; your previous config was left untouched.";
        map["首次配置"] = "First-time setup";
        map["添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试连接 → 保存。通过后再去「AI 队友」从主菜单邀请。默认网址和模型名不算已经可用。"] =
            "Add an endpoint → add a model and bind it → assign chat/play roles → test the connection → save. Once it passes, go to “AI teammate” and invite from the main menu. The default URL and model name do not count as working.";
        map["测试连接会向配置的服务发送测试请求。对话通过不等于游玩已通过。本地服务可以留空 API Key。"] =
            "Testing sends a real request to the configured service. A passing chat test does not mean the play model is verified. For local services you can leave the API key blank.";
        map["端点"] = "Endpoints";
        map["模型"] = "Models";
        map["角色绑定"] = "Role assignment";
        map["主对话模型"] = "Main chat model";
        map["游玩模型（可空=主对话）"] = "Play model (blank = chat model)";
        map["外挂视觉模型（可空）"] = "External vision model (optional)";
        map["显示高级选项"] = "Show advanced options";
        map["视觉可选。不勾选「视觉」、不配外挂视觉时，仍用 compact 状态与工具打完全部内容。"] =
            "Vision is optional. Without checking “Vision” or setting an external vision model, auto-play still plays through all content using the compact state and tools.";
        map["开关热键"] = "Toggle hotkey";
        map["不限（留空或0）"] = "Unlimited (blank or 0)";
        map["会话 Token 上限"] = "Session token budget";
        map["会话请求上限"] = "Session request budget";
        map["非法预算输入会保留原来的安全上限，不会静默变成不限。"] =
            "An invalid budget keeps the previous safe limit instead of silently turning into unlimited.";
        map["预算护栏：达到上限时优雅停止自动游玩并提示，避免意外耗尽额度。"] =
            "Budget guard: auto-play stops gracefully with a notice at the limit, so you avoid burning through your quota by accident.";
        map["主动发言（AI 队友偶尔主动说一句）"] = "Proactive chat (AI teammate occasionally speaks up)";
        map["交流风格"] = "Chat tone";
        map["默认关闭。开启后仅在战斗开始与结束时各说一句，每次开始自动游玩最多 6 句，两句之间至少间隔 75 秒（暂停或继续自动游玩不会缩短这个间隔）；不会代打，也遵守预算上限。"] =
            "Off by default. When on, it speaks once at combat start and once at combat end, at most 6 lines per auto-play run, with at least 75 seconds between lines (pausing or resuming does not shorten that gap). It never plays for you and still respects the budget.";
        map["重置窗口位置"] = "Reset window position";
        map["拖动标题栏可移动窗口，位置会保存。"] = "Drag the title bar to move the window; its position is saved.";
        map["配置文件：{0}"] = "Config file: {0}";
        map["名称"] = "Name";
        map["启用"] = "Enabled";
        map["删除"] = "Delete";
        map["显示名"] = "Display name";
        map["模型名"] = "Model name";
        map["(未绑定端点)"] = "(No endpoint bound)";
        map["(当前端点不可用：{0})"] = "(Endpoint unavailable: {0})";
        map["视觉"] = "Vision";
        map["工具调用"] = "Tool calling";
        map["思考方式"] = "Thinking mode";
        map["思考强度"] = "Thinking effort";
        map["新端点"] = "New endpoint";
        map["新模型"] = "New model";
        map["(默认/无)"] = "(Default/none)";
        map["(未选择)"] = "(Not selected)";
        map["(当前绑定不可用：{0})"] = "(Binding unavailable: {0})";
        map[" 备份：{0}"] = " Backup: {0}";

        // Header status line: URL · role · hotkey.
        map["{0}  ·  {1}  ·  热键 {2}"] = "{0}  ·  {1}  ·  Hotkey {2}";
    }
}
