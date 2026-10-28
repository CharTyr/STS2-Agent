using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // The 2026-10 experience overhaul: turbo, autopilot, whole-turn plans, combat math, cost meters,
    // self-healing, the in-game HUD and the run report. Kept in one shard so the overhaul's copy can
    // be reviewed (and translated) together.
    private static void AddOverhaulEntries(Dictionary<string, string> map)
    {
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
    }
}
