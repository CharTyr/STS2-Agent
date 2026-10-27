using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Overlay text: headers, tabs, buttons, labels, hints (Ui/AgentOverlayHost.cs).
    private static void AddUiEntries(Dictionary<string, string> map)
    {
        // Header, tabs, chat page.
        map["思考协议"] = "Thinking protocol";
        map["隐藏"] = "Hide";
        map["● 游玩中"] = "● Playing";
        map["○ 待机"] = "○ Idle";
        map["可以问：这手牌怎么打、遗物值不值得拿。"] =
            "Try: how to play this hand, is a relic worth taking.";
        map["对话默认只读；要它动手，用上方的「开始自动游玩」或「单步」，或者明确说「帮我打」。"] =
            "Chat is read-only; to make it act, use “Start auto-play” or “Single step” above, or say “play for me” in a message.";
        map["屏幕 {0} · 动作 {1}"] = "Screen {0} · Action {1}";
        map["展开记录"] = "Show log";
        map["收起记录"] = "Hide log";
        map["思考"] = "Reasoning";
        map["思考中"] = "Thinking";
        map["动作"] = "Action";
        map["已折叠更早的 {0} 条"] = "{0} earlier hidden";
        map["本次会话"] = "This session";
        map["本局"] = "This run";
        map["跳到「{0}」"] = "Jump to “{0}”";
        map["对话"] = "Chat";
        map["设置"] = "Settings";
        map["游玩"] = "Play";
        map["AI 队友"] = "AI teammate";
        map["MCP"] = "MCP";
        map["附带局面"] = "Attach state";
        map["附带截图"] = "Attach screenshot";
        map["发送"] = "Send";
        map["清空"] = "Clear";
        map["在下方输入后点发送。"] = "Type below, then press Send.";

        // Play page: the solo/multiplayer mode switch, the dual-layer toggles, and the Jev panel.
        map["单人"] = "Solo";
        map["多人"] = "Multiplayer";
        map["显示思考"] = "Show reasoning";
        map["回复语言"] = "Reply language";
        map["自动"] = "Auto";
        map["输入消息…"] =
            "Type a message…";
        map["双层决策（Jev）"] = "Dual-layer (Jev)";
        map["战斗中不自动操作"] = "No auto-play in combat";
        map["战斗"] = "Combat";
        map["未配置 Jev，请先在「设置」填写。"] =
            "Jev is not set up. Fill it in under “Settings”.";
        map["Jev：{0}"] =
            "Jev: {0}";
        map["Jev"] = "Jev";
        map["最近选择"] = "Last choice";
        map["概率"] = "Odds";
        map["危险度"] = "Danger score";
        map["耗时"] = "Latency";
        map["正在暂停…"] = "Pausing…";
        map["暂停未确认，模式未切换。"] = "Pause not confirmed; mode unchanged.";
        map["已切换，另一模式不会自动开始。"] = "Switched; the other mode will not start on its own.";
        map["保存失败，模式未切换。"] = "Save failed; mode unchanged.";
        map["暂停未确认，模式未切换。"] = "Pause not confirmed; mode unchanged.";
        map["保存失败，队友设置未确认。"] = "Save failed; teammate settings unconfirmed.";
        map["队友设置未确认，请检查连接。"] = "Teammate settings unconfirmed; check the connection.";
        map["队友未连接，下次邀请时生效。"] = "Teammate offline; applies on the next invite.";
        map["队友 Jev 开关尚未同步。"] = "Teammate Jev switch not synced yet.";
        map["队友 Jev 状态不可用，请检查连接。"] =
            "Teammate Jev status unavailable; check the connection.";

        // Settings page: the Jev execution-model section.
        map["置信度阈值（0-1）"] = "Confidence threshold (0-1)";
        map["测试 Jev 连接"] = "Test Jev connection";
        map["正在测试…"] = "Testing…";
        map["你"] = "You";
        map["助手"] = "Assistant";

        // Settings page buttons.
        map["添加端点"] = "Add endpoint";
        map["添加模型"] = "Add model";
        map["保存设置"] = "Save settings";
        map["测试连接"] = "Test connection";

        // Section headings and the overlay theme selector.
        map["外观"] = "Appearance";
        map["主题"] = "Theme";
        map["暮色"] = "Dusk";
        map["午夜"] = "Midnight";
        map["羊皮纸"] = "Parchment";
        map["高对比"] = "High contrast";
        map["猩红"] = "Crimson";
        map["翡翠"] = "Emerald";
        map["紫晶"] = "Amethyst";
        map["象牙"] = "Ivory";
        map["自动游玩"] = "Auto-play";
        map["本步详情"] = "Last step";
        map["组队状态"] = "Party";
        map["邀请与控制"] = "Invite and control";
        map["队友控制"] = "Teammate control";
        map["MCP 服务"] = "MCP service";
        map["客户端配置"] = "Client configuration";
        map["用量"] = "Usage";
        map["决策记录"] = "Decisions";

        // Play page.
        map["状态：-"] = "Status: -";
        map["最近动作：-"] = "Last action: -";
        map["思考：-"] = "Thinking: -";
        map["Token 消耗：-"] = "Token usage: -";
        map["开始自动游玩"] = "Start auto-play";
        map["暂停"] = "Pause";
        map["单步"] = "Single step";
        map["状态：{0}"] = "Status: {0}";
        map["最近动作：{0}"] = "Last action: {0}";
        map["思考：{0}"] = "Thinking: {0}";
        map["自动游玩走 compact 状态和工具，与 MCP 相同，不需要视觉即可打完全部流程。对话默认只读：要动手就用「开始自动游玩」或「单步」，或者在消息里明确说「帮我打」。"] =
            "Auto-play uses the same compact state and tools as MCP, so vision is optional — it can play through the entire run without it. Chat is read-only: use “Start auto-play” or “Single step” to act, or ask it explicitly to play for you.";

        // AI teammate page.
        map["和 AI 一起爬塔"] = "Climb the tower with AI";
        map["下一步：-"] = "Next step: -";
        map["下一步：{0}"] = "Next step: {0}";
        map["请从主菜单邀请。第二窗口打开后，AI 会自己选角、点开局事件并进图。你继续在这个窗口操作自己的角色；轮到它时，它会自动出牌。"] =
            "Invite from the main menu. Once the second window opens, the AI picks its character, gets through the opening event, and enters the map on its own. Keep playing your character in this window; the AI plays its cards when its turn comes.";
        map["导出诊断"] = "Export diagnostics";
        map["邀请 AI 队友"] = "Invite AI teammate";
        map["邀请中…"] = "Inviting…";
        map["队友未加入"] = "No teammate";
        map["禁用自动选角"] = "Disable automatic character pick";
        map["请从主菜单邀请。第二窗口打开后，会停在选角界面让 AI 自己决定选角，也可以你切过去给它选好、点出发；之后它自己点开局事件并进图。"] =
            "Invite from the main menu. The second window waits on the character screen for the AI to decide its character; you can also switch over, pick for it, and press Embark. It then gets through the opening event and enters the map on its own.";
        map["继续联机存档"] = "Continue co-op save";
        map["正在读档…"] = "Loading save…";
        map["请从主菜单邀请。第二窗口打开后，AI 会加入并进图，然后停在原地等待外部接管，不会自己出牌；你继续在这个窗口操作自己的角色。"] =
            "Invite from the main menu. Once the second window opens, the AI joins and enters the run, then stops and waits for an external client to take over — it will not play on its own; keep playing your character in this window.";
        map["游玩模型未配置或未验证：仍然可以邀请，队友会加入并进图，然后停在原地等待外部接管，不会自己出牌。想让它自己打，先在设置里配好模型并通过「测试连接」。"] =
            "No verified play model: you can still invite. The teammate joins and enters the run, then stops and waits for an external client to take over; it will not play on its own. To let it play on its own, configure a model in Settings and pass “Test connection” first.";
        map["暂停队友"] = "Pause teammate";
        map["继续游玩"] = "Resume";
        map["队伍交流"] = "Team chat";
        map["对队友说点什么…"] = "Say something to your teammate…";
        map["发送"] = "Send";
        map["队友：{0}"] = "Teammate: {0}";
        map["队友：未组队"] = "Teammate: not teamed up";
        map["队友：读取中…"] = "Teammate: reading…";
        map["未知"] = "unknown";
        map["{0}：{1} HP"] = "{0}: {1} HP";
        map["，{0} 格挡"] = ", {0} block";
        map["（已倒下）"] = " (down)";
        map["，{0} 能量"] = ", {0} energy";
        map["手牌 {0} 张"] = "{0} cards in hand";
        map["队友"] = "Teammate";
        map["等待回复…"] = "Waiting…";
        // The focus-fire constraint the companion's loop receives when the teammate announced a target.
        map["队友本回合在打 enemy_index {0}。除非那个敌人已经必死或只剩最后一击，不要把伤害再倾泻在它身上；优先选另一个目标，避免两人重复集火把伤害溢出掉。"] =
            "Your teammate is attacking enemy_index {0} this turn. Unless that enemy is already certain to die or is one hit from it, do not pour more damage into it; pick another target so the two of you do not waste damage on the same kill.";
        map["诊断已复制（不含 Key 和对话）。"] =
            "Diagnostics copied (no keys or chat).";

        // Decision log tab: what the agent did, why, and what this session has spent.
        map["决策日志"] = "Decision log";
        map["理由：{0}"] = "Reason: {0}";
        map["来源：{0}"] = "Source: {0}";
        map["Jev 未提交，LLM 接手"] = "Jev gave no action; LLM took over";
        map["Token {0}"] = "Tokens {0}";
        map["Token 未知"] = "Tokens unknown";
        map["暂无决策记录。"] =
            "No decisions yet.";
        // A session can outlive a run, so the tab shows both totals.
        map["本局：-"] = "This run: -";
        map["尚未进入对局"] = "No run yet";
        map["暂无决策"] = "No decisions yet";
        map["{1} 次决策 · {2} Token"] = "{1} decisions · {2} tokens";
        map["{1} 次决策 · Token 未知"] = "{1} decisions · tokens unknown";

        // Connect page.
        map["MCP"] = "MCP";
        map["启用 MCP"] = "Enable MCP";
        map["复制地址"] = "Copy URL";
        map["复制配置"] = "Copy config";
        map["地址：{0}"] = "URL: {0}";

        // Settings form, endpoint and model cards.
        map["未保存"] = "Unsaved";
        map["已保存"] = "Saved";
        map["已保存（预算未改：{0}）"] = "Saved (budget unchanged: {0})";
        map["保存失败，原配置未改动。"] = "Save failed; nothing was changed.";
        map["快速开始"] = "Get started";
        map["添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试连接 → 保存。通过后再去「AI 队友」从主菜单邀请。默认网址和模型名不算已经可用。"] =
            "Add an endpoint → add a model and bind it → assign chat/play roles → test the connection → save. Once it passes, go to “AI teammate” and invite from the main menu. The default URL and model name do not count as working.";
        map["测试连接会向配置的服务发送测试请求。对话通过不等于游玩已通过。本地服务可以留空 API Key。"] =
            "Testing sends a real request to the configured service. A passing chat test does not mean the play model is verified. For local services you can leave the API key blank.";
        map["端点"] = "Endpoints";
        map["模型"] = "Models";
        map["角色绑定"] = "Role assignment";
        map["模型绑定"] = "Model binding";
        map["主对话模型"] = "Main chat model";
        map["主模型"] = "Main model";
        map["游玩模型（可空=主对话）"] = "Play model (blank = chat model)";
        map["视觉模型（可选）"] = "Vision model (optional)";
        map["测试"] = "Test";
        map["正在测试…"] = "Testing…";
        map["⚪ 未配置"] = "⚪ Not set up";
        map["⚪ 未测试"] =
            "⚪ Not tested";
        map["⚪ 已修改，需重新测试"] = "⚪ Changed; test again";
        map["⚠️ 测试失败（{0}）：{1}"] = "⚠️ Test failed ({0}): {1}";
        map["✅ 已验证（{0}）"] = "✅ Verified ({0})";
        map["⚠️ 不支持工具调用，无法自动游玩（{0}）"] =
            "⚠️ No tool calling; cannot auto-play ({0})";
        map["默认 600 秒"] = "Default 600 seconds";
        map["默认 90 秒"] = "Default 90 seconds";
        map["请求超时（秒）"] = "Request timeout (s)";
        map["显示高级选项"] = "Show advanced options";
        map["显示/隐藏热键"] = "Show/hide hotkey";
        map["不限（留空或0）"] = "Unlimited (blank or 0)";
        map["Token 上限"] = "Token limit";
        map["请求上限"] = "Request limit";
        map["AI 主动发言"] = "AI speaks up";
        map["发言风格"] = "Tone";
        map["重置窗口位置"] = "Reset window position";
        map["配置文件：{0}"] = "Config file: {0}";
        map["名称"] = "Name";
        map["启用"] = "Enabled";
        map["删除"] = "Delete";
        map["显示名"] = "Display name";
        map["模型名"] = "Model name";
        map["(未绑定端点)"] = "(No endpoint bound)";
        map["(端点不可用：{0})"] = "(Endpoint unavailable: {0})";
        map["视觉"] = "Vision";
        map["工具调用"] = "Tool calling";
        map["思考方式"] = "Thinking mode";
        map["思考强度"] = "Thinking effort";
        map["上下文窗口"] = "Context window";
        map["默认 256000"] = "Default 256000";
        map["新端点"] = "New endpoint";
        map["新模型"] = "New model";
        map["(默认/无)"] = "(Default/none)";
        map["(未选择)"] = "(Not selected)";
        map["(绑定不可用：{0})"] = "(Binding unavailable: {0})";
        map[" 备份：{0}"] = " Backup: {0}";

        // Help panels: the usage notes behind each card's "?" button (Ui/HelpCard.cs).
        map["说明"] =
            "Help";
        map["模型状态"] =
            "Model status";
        map["高级"] =
            "Advanced";
        map["「开始自动游玩」让 AI 持续决策并操作，「单步」只走一步。\n用的是与 MCP 相同的精简状态和工具，不需要视觉。"] =
            "“Start auto-play” lets the AI keep deciding and acting; “Single step” takes just one step.\nIt uses the same compact state and tools as MCP, so vision is not required.";
        map["对话默认只读，提问不会出牌。想让 AI 代打一手，直接说「帮我打」。\n自动游玩时发的消息只影响之后的决策，不会同时出牌。"] =
            "Chat is read-only: asking a question never plays a card. To have the AI play one move, say “play for me”.\nMessages sent during auto-play only shape later decisions; they never play at the same time.";
        map["开启后，战斗中 AI 不请求模型也不操作，战斗结束后自动恢复。\n适合手动打战斗，或交给外部战斗控制器。"] =
            "While on, the AI makes no model requests and takes no actions in combat, and resumes when combat ends.\nUse it to fight by hand or to hand combat to an external controller.";
        map["开启「双层决策」后，由 Jev 逐步选择动作，LLM 只负责战略规划。\nJev 的地址和 Key 在「设置」里填写；置信度低于阈值时由 LLM 接手。"] =
            "With dual-layer on, Jev picks each action and the LLM only plans strategy.\nFill in Jev's URL and key under “Settings”; below the confidence threshold the LLM takes over.";
        map["最新的在最前。每条包含动作、理由、来源和该步消耗的 Token。"] =
            "Newest first. Each entry shows the action, reason, source and the tokens that step used.";
        map["你打自己的角色，AI 打另一个角色，同一座塔往上爬。\n在主菜单邀请；第二窗口打开后，AI 会自动选角、过开局事件并进图。\n大厅仍是 4 人位，还可再邀 2 名在线玩家。\n队友窗口连不上时，检查游戏日志和 Steam 双开限制。"] =
            "You play your character, the AI plays another, and you climb the same tower.\nInvite from the main menu; once the second window opens, the AI picks a character, clears the opening event and enters the map.\nThe lobby still holds 4, so you can invite 2 more online players.\nIf the teammate window will not connect, check the game log and Steam's multi-instance limits.";
        map["聊天不会替你出牌，也不会恢复已暂停的队友。\n你的建议会作为队友下一次决策的参考。"] =
            "Chat never plays cards for you or resumes a paused teammate.\nYour advice feeds the teammate's next decision.";
        map["「测试」会真实调用该模型：先测连通，再测工具调用。不支持工具调用的模型无法自动游玩。\n「测试主模型」测的是当前主模型。"] =
            "“Test” makes real calls to the model: connectivity first, then tool calling. A model without tool calling cannot auto-play.\n“Test main model” tests the current main model.";
        map["四步：① 在「端点」填地址和 Key（本地服务可留空 Key）② 在「模型」添加模型并绑定端点 ③ 点模型卡片上的「测试」，通过会显示 ✅ ④ 在「模型绑定」选主模型并保存。\n然后到「游玩」页开始自动游玩。双人游玩：把游玩页顶部切到「多人」再邀请 AI 队友。"] =
            "Four steps: ① fill in the URL and key under “Endpoints” (local services can leave the key blank) ② add a model under “Models” and bind the endpoint ③ press “Test” on the model card; a pass shows ✅ ④ pick the main model under “Model binding” and save.\nThen start auto-play on the “Play” page. For co-op, switch the top of the Play page to “Multiplayer” and invite the AI teammate.";
        map["选择服务商，填 API Key 和模型名，然后点「测试主模型」。\n测试会真实调用模型：先测连通，再测工具调用；不支持工具调用的模型无法自动游玩。\n对话和自动游玩共用这个模型；本地服务（Ollama / LM Studio）可留空 Key。\n请求上限：新安装默认试用 30 次，可调整。测试也会调用你的服务；请求数和 Token 不等于账单金额。"] =
            "Choose a provider, enter the API key and model name, then press “Test main model”.\nThe test makes real calls: connectivity first, then tool calling; a model without tool calling cannot auto-play.\nChat and auto-play share this model; local services (Ollama / LM Studio) can leave the key blank.\nRequest limit: new installs default to a 30-request trial; adjust as needed. Tests also call your service, and request/token counts are not a bill.";
        map["视觉：不勾选模型的「视觉」、也不配视觉模型时，仍可用精简状态和工具打完全部流程。\n预算：达到上限会自动停止游玩并提示；填错会保留原上限，不会变成不限。\n请求超时：服务商长时间没有响应时，按此时长报错。\nAI 主动发言：默认关闭；开启后只在战斗开始和结束各说一句，每次自动游玩最多 6 句，间隔至少 75 秒，不会代打，也受预算限制。\n窗口位置：拖动标题栏移动，位置会保存。"] =
            "Vision: without the model's “Vision” box or a vision model, auto-play still finishes the whole run on the compact state and tools.\nBudget: auto-play stops with a notice at the limit; a bad entry keeps the previous limit instead of becoming unlimited.\nRequest timeout: how long to wait before reporting a provider that stopped answering.\nAI speaks up: off by default; when on it speaks once at combat start and once at combat end, at most 6 lines per auto-play run, at least 75 seconds apart. It never plays for you and respects the budget.\nWindow position: drag the title bar to move it; the position is saved.";
        map["双层决策下，由 Jev（TypeSafe System One 模型）逐步操作游戏，LLM 只做战略规划。\n在游玩页按模式开启。置信度低于阈值时，由 LLM 接手。"] =
            "In dual-layer mode Jev (the TypeSafe System One model) takes each action and the LLM only plans strategy.\nEnable it per mode on the Play page. Below the confidence threshold the LLM takes over.";
        map["游戏内自动游玩和一起玩都不需要 MCP，只有 Claude Code / Cursor / Codex 等外部客户端才需要。\n打开后选你的客户端再复制配置：Claude Code 在终端运行这条命令，Cursor 贴进 mcp.json，Codex 贴进 ~/.codex/config.toml。服务只监听本机 127.0.0.1。\n本机 HTTP API 始终可用（GET /health /state，POST /action）；MCP 打开后才会在同一端口提供 /mcp。地址以本页复制为准。\nPython sidecar 仅支持 stdio / layered / full。"] =
            "In-game auto-play and playing together do not need MCP; only external clients such as Claude Code / Cursor / Codex do.\nOnce it is on, pick your client and copy its config: run the command in a terminal for Claude Code, paste into mcp.json for Cursor, or into ~/.codex/config.toml for Codex. The service listens only on 127.0.0.1.\nThe local HTTP API is always available (GET /health /state, POST /action); /mcp appears on the same port only once MCP is on. Copy the address from this page.\nThe Python sidecar supports only stdio / layered / full.";

        // Hide-button tooltip and the local API line in Settings → MCP.
        map["热键 {0}"] = "Hotkey {0}";
        map["本机 API：{0}"] = "Local API: {0}";
    }
}
