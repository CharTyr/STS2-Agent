using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // Game state payload text shown to the model: summary lines and glossary explanations
    // (Game/GameStateService.cs). The keyword labels double as the glossary keys, so a keyword
    // entry here is what an English client reads for that term.
    private static void AddGameEntries(Dictionary<string, string> map)
    {
        // Summary lines assembled from pieces of board state.
        map["{0} (熔毁)"] = "{0} (Melted)";
        map["空"] = "Empty";
        map["：{0}"] = ": {0}";
        map["{0} (随机)"] = "{0} (Random)";
        map["{0}：{1}"] = "{0}: {1}";
        map["X费"] = "X Energy";
        map["{0}费"] = "{0} Energy";
        map["X星"] = "X Star";
        map["{0}星"] = "{0} Star";
        map["{0} 被动{1}/激发{2}"] = "{0} passive {1}/evoke {2}";
        map["{0} {1}/{2} 格挡{3}"] = "{0} {1}/{2} block {3}";
        map["{0}: 空"] = "{0}: Empty";
        map["{0}: {1}{2}"] = "{0}: {1}{2}";

        // Co-op launch failures raised straight from Game/GameActionService.cs.
        map["找不到多人子菜单。"] = "Could not find the multiplayer submenu.";
        map["找不到 FastHost。"] = "Could not find FastHost.";

        // Glossary keyword labels.
        map["力量"] = "Strength";
        map["敏捷"] = "Dexterity";
        map["易伤"] = "Vulnerable";
        map["虚弱"] = "Weak";
        map["脆弱"] = "Frail";
        map["格挡"] = "Block";
        map["消耗"] = "Exhaust";
        map["保留"] = "Retain";
        map["中毒"] = "Poison";
        map["眩晕"] = "Dazed";
        map["灼伤"] = "Burn";
        map["虚空"] = "Void";
        map["力量流失"] = "Strength Down";
        map["集中"] = "Focus";
        map["球位"] = "Orb Slot";
        map["附魔"] = "Enchantment";
        map["灌注"] = "Imbued";
        map["临时"] = "Temporary";

        // Glossary explanations.
        map["每层力量通常使攻击额外造成 1 点伤害。"] = "Each point of Strength usually adds 1 damage per attack.";
        map["每层敏捷通常使获得的格挡额外增加 1。"] = "Each point of Dexterity usually adds 1 more Block.";
        map["易伤单位会承受更多攻击伤害。"] = "Vulnerable creatures take more attack damage.";
        map["虚弱单位造成的攻击伤害会降低。"] = "Weak creatures deal less attack damage.";
        map["脆弱单位获得的格挡会减少。"] = "Frail creatures gain less Block.";
        map["格挡会优先抵消即将受到的伤害。"] = "Block absorbs incoming damage before HP.";
        map["消耗牌打出后会移出本场战斗。"] = "Exhausted cards are removed for the rest of the combat.";
        map["保留牌在回合结束时不会被弃掉。"] = "Retained cards are not discarded at the end of the turn.";
        map["中毒会在回合结束时造成等量生命损失，然后层数减少。"] = "Poison deals its stacks as HP loss at the end of the turn, then the stacks go down.";
        map["眩晕通常是无法主动打出的状态牌。"] = "Dazed is a status card that cannot be played.";
        map["灼伤通常会在手中或结算时带来额外伤害。"] = "Burn usually brings extra damage while in hand or when it resolves.";
        map["虚空通常会在抽到时消耗能量或妨碍出牌。"] = "Void usually drains energy or blocks your plays when you draw it.";
        map["力量流失会临时降低力量。"] = "Strength Down lowers Strength for a while.";
        map["集中通常会强化充能球的被动与激发效果。"] = "Focus usually strengthens orb passive and evoke effects.";
        map["球位决定你能同时容纳多少个充能球。"] = "Orb Slots decide how many orbs you can hold at once.";
        map["附魔是卡牌附着的额外词条或效果层。"] = "An enchantment is an extra modifier layer attached to a card.";
        map["灌注表示卡牌带有额外附着效果。"] = "Imbued marks a card that carries an extra attached effect.";
        map["临时牌通常会在回合结束或打出后离开牌组流转。"] = "Temporary cards usually leave the deck flow after the turn ends or after they are played.";
    }
}
