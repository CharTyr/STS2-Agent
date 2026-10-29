using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // The 2026-10 experience overhaul: turbo, autopilot, whole-turn plans, combat math, cost meters,
    // self-healing, the in-game HUD and the run report. Kept in one shard so the overhaul's copy can
    // be reviewed (and translated) together.
    private static void AddOverhaulEntries(Dictionary<string, string> map)
    {
        // HUD and long-stage hints.
        map["还没开始一局：AI 会自己选角出发，也可以你先开一局。"] = "No run has started: the AI can pick a character and embark, or you can start a run first.";
        map["等你在自己的窗口里操作。"] = "Waiting for you to act in your own window.";
        map["模型响应较慢，可以暂停后换模型。"] = "The model is slow to respond; you can pause and choose another model.";
        map["游戏窗口可能在后台，请切回游戏并保持画面可见。"] = "The game may be in the background; bring it forward and keep it visible.";
        map["AI 正在游玩 · {0} · {1} 暂停 · {2} 打开面板"] = "AI playing · {0} · {1} pause · {2} open panel";
        map["AI 已暂停 · {0} 继续 · {1} 打开面板"] = "AI paused · {0} resume · {1} open panel";
        // Cost meters (Agent/PlayerFacingSession.cs).
        map["{0} · 缓存命中 {1}%"] = "{0} · cache hit {1}%";

        // Forced-move reasons (Agent/AgentLoop.Autopilot.cs).
        map["确认只有「确定」按钮的提示弹窗。"] = "Confirm a popup that only has an OK button.";
        map["本局已结束，继续到结算。"] = "The run is over; continue to the summary.";
        map["没有能打出的牌，也没有能用的药水，结束回合。"] = "No playable card and no usable potion; end the turn.";
        map["地图上只有一条路可走。"] = "The map offers a single path.";
        map["奖励里没有需要取舍的项目，全部领取并继续。"] = "Nothing in the rewards needs a choice; take them all and continue.";
        map["先领取金币或遗物；选卡留给模型。"] = "Take the gold or relic first; the card pick is left to the model.";
        map["事件只剩一个可选项。"] = "The event has a single option left.";
        map["只有一个可选项。"] = "There is a single option.";
        map["休息点只有一个可用选项。"] = "The rest site has a single available option.";
        map["宝箱里只有一件遗物。"] = "The chest holds a single relic.";
        map["打开宝箱。"] = "Open the chest.";
        map["当前只有一个可执行的操作。"] = "Only one action is possible.";

        // Recovery (Agent/AutoPlayRecovery.cs).
        map["连续 {0} 次连不上模型服务，已停止自动游玩：{1}。你可以：检查网络或服务商状态，稍后点「继续游玩」。"] =
            "Could not reach the model service {0} times in a row; auto-play stopped: {1}. You can check your network or the provider's status, then press “Continue” later.";

        // Provider capabilities (Agent/AgentRuntime.ModelTest.cs).
        map["连通成功，工具调用可用。此服务商的流式工具参数不完整，已改用非流式请求。"] =
            "Connected; tool calling works. This provider truncates streamed tool arguments, so requests now go out non-streaming.";

        // Decision sources and plan steps (Agent/PlayerLabels.cs, Ui/DecisionLogView.cs).
        map["自动处理"] = "Auto";
        map["整回合规划"] = "Turn plan";
        map["外部智能体（MCP）"] = "External agent (MCP)";
        map["整回合规划 {0}/{1}"] = "Turn plan {0}/{1}";

        // The experience settings card (Ui/AgentOverlayHost.Experience.cs).
        map["游玩体验"] = "Play experience";
        map["游戏速度"] = "Game speed";
        map["快速"] = "Fast";
        map["极速"] = "Instant";
        map["检测到另一个加速模组（SpeedX），两者可以同时使用。"] = "Another speed mod (SpeedX) is loaded; the two work together.";
        map["小事自动处理（不问模型）"] = "Handle forced moves without the model";
        map["整回合规划（一次请求打完一回合）"] = "Plan a whole combat turn in one request";
        map["战斗算术（来袭伤害、斩杀、格挡）"] = "Combat math (incoming damage, lethal, block)";
        map["截图"] = "Screenshots";
        map["不截图"] = "Never";
        map["按需"] = "When needed";
        map["每一步"] = "Every step";
        map["游戏画面提示"] = "On-screen hints";
        map["仅横幅"] = "Banner only";
        map["完整"] = "Full";
        map["暂停热键"] = "Pause key";
        map["不用热键"] = "No key";
        map["记录战绩并提供给模型"] = "Keep a run record and show it to the model";
        map["游戏速度：AI 自动游玩时把游戏自带的动画速度调快，暂停或停止后恢复你原来的设置；联机时最多用「快速」。不会改写你的游戏设置文件。\n小事自动处理：只剩一个选择、确认教学弹窗、无牌可出时结束回合这类不用判断的步骤，直接完成，不花模型请求。选牌、路线、商店、事件、药水永远交给模型。\n整回合规划：战斗中模型一次给出整回合的出牌顺序，Mod 每一步都用最新局面核对，局面一变就停下重新问。\n战斗算术：把来袭伤害、需要多少格挡、每张牌对每个敌人的真实伤害和能否斩杀算好交给模型。\n截图「按需」：只在局面看不懂或上一步出错时才附截图，更省更快。\n游戏画面提示：在游戏画面上显示 AI 要出的牌、目标和理由；截图时会自动隐藏。"] =
            "Game speed: while the AI plays, the game's own animation speed is raised; pausing or stopping restores your setting. Co-op uses Fast at most. Your game settings file is never rewritten.\nForced moves: steps with no real choice — a single option, a tutorial popup, ending a turn with nothing playable — are done at once without a model request. Card picks, routes, shops, events and potions always go to the model.\nWhole-turn plans: in combat the model gives the whole turn's card order at once; the mod checks every step against the live game and asks again as soon as something changes.\nCombat math: incoming damage, block needed, each card's real damage on each enemy and lethal are computed for the model.\nScreenshots “When needed”: only when the screen is unclear or the last step failed — cheaper and faster.\nOn-screen hints: show the card, target and reason the AI is about to play on the game screen; hidden in screenshots.";

        // The end-of-run report (Agent/RunSummary.cs, Ui/AgentOverlayHost.RunSummary.cs). Every line is a
        // fact read from the game-over frame or summed from that run's own decision rows; the one
        // session-wide reading says so in its own label.
        map["本轮成绩单"] = "Run report";
        map["胜利"] = "Victory";
        map["失败"] = "Defeat";
        map["结果未知"] = "Result unknown";
        map["未知角色"] = "Unknown character";
        map["未知敌人"] = "unknown enemy";
        map["未记录"] = "not recorded";
        map["层数未知"] = "floor unknown";
        map["{0}（难度 {1}）"] = "{0} (Ascension {1})";
        map["第 {0} 层"] = "Floor {0}";
        map["结果：{0}"] = "Result: {0}";
        map["幕 {0}"] = "act {0}";
        map["BOSS {0}"] = "boss {0}";
        map["角色：{0}"] = "Character: {0}";
        map["本窗口观察用时：{0}"] = "Watched here for: {0}";
        map["{0} 分 {1} 秒"] = "{0}m {1}s";
        map["{0} 秒"] = "{0}s";
        map["倒在：{0}"] = "Killed by: {0}";
        map["倒在 {0}"] = "killed by {0}";
        map["{0}（推断）"] = "{0} (inferred)";
        map["决策：{0} 条（模型 {1} · 自动处理 {2} · 快速判断 {3}，其中整回合规划 {4} 步）"] =
            "Decisions: {0} (model {1} · forced {2} · fast judgment {3}; {4} of them whole-turn plan steps)";
        map["请求：{0}{1} 次 · 每层约 {2} 次"] = "Requests: {0}{1} · about {2} per floor";
        map["请求：{0}{1} 次"] = "Requests: {0}{1}";
        map["Token：{0}{1}"] = "Tokens: {0}{1}";
        map["Token：未知"] = "Tokens: unknown";
        map["本次会话缓存命中：{0}%"] = "Cache hit this session: {0}%";
        map["游戏速度：{0}"] = "Game speed: {0}";
        map["高光：{0}"] = "Highlight: {0}";
        map["复制成绩单"] = "Copy report";
        map["已复制成绩单。"] = "The report is on the clipboard.";
        map["还没有本轮成绩单。"] = "No finished run to report yet.";
        map["还没有本轮成绩单；已记录 {0} 局战绩。"] = "No finished run to report yet; {0} run(s) recorded.";
        map["清空战绩记录"] = "Clear run record";
        map["已清空战绩记录。"] = "The recorded runs were cleared.";
        map["清空战绩记录失败：文件可能被占用。"] =
            "Could not clear the recorded runs: the file may be in use.";
        map["一局结束（游戏显示结算画面）时自动出现。全部是这一局的事实：结果、层数、决策来源、请求与 Token。\n「复制成绩单」把同样的文字放进剪贴板，可以直接发给别人。\n「清空战绩记录」删除跨局保存的战绩文件；关掉「游玩体验」里的战绩开关后，不再写入，也不再提供给模型。"] =
            "Appears when a run ends (the game shows its summary screen). Everything here is a fact about that run: result, floors, where the decisions came from, requests and tokens.\n“Copy report” puts the same text on the clipboard so it can be shared.\n“Clear run record” deletes the cross-run record file; with the switch in “Play experience” off, nothing is written and nothing is shown to the model.";
    }
}
