using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The words the overlay shows for the API's screen and action identifiers.
/// </summary>
/// <remarks>
/// The solo card used to read "屏幕 MAIN_MENU · 动作 play_card": the wire names the HTTP API and the
/// model use, put in front of a player. These are for display only; the API, the decision log's
/// stored entries and the model keep the identifiers. An identifier without a label is shown as it is,
/// so a new screen or action never displays as blank.
/// </remarks>
internal static class PlayerLabels
{
    public static string Screen(string? screen)
    {
        var key = screen?.Trim() ?? string.Empty;
        var label = key switch
        {
            "MAIN_MENU" => "主菜单",
            "CHARACTER_SELECT" => "选择角色",
            "MULTIPLAYER_LOBBY" => "联机大厅",
            "MULTIPLAYER_LOAD" => "联机读档",
            "BUNDLE_SELECTION" => "选择开局卡包",
            "CAPSTONE_SELECTION" => "Capstone 选项",
            "PAUSE_MENU" => "暂停菜单",
            "SETTINGS" => "游戏设置",
            "COMPENDIUM" => "百科大全",
            "RELIC_COLLECTION" => "遗物收集",
            "POTION_LAB" => "药水研究所",
            "BESTIARY" => "怪物图鉴",
            "STATS" => "角色数据",
            "RUN_HISTORY" => "历史记录",
            "MAP" => "地图",
            "COMBAT" => "战斗",
            "EVENT" => "事件",
            "CRYSTAL_SPHERE" => "水晶球",
            "SHOP" => "商店",
            "FAKE_MERCHANT" => "商店",
            "REST" => "休息点",
            "REWARD" => "奖励",
            "CHEST" => "宝箱",
            "CARD_SELECTION" => "选牌",
            "CARDS_VIEW" => "看牌",
            "CARD_LIBRARY" => "牌库",
            "CARD_PILE" => "牌堆",
            "CARD_INSPECT" => "查看卡牌",
            "RELIC_INSPECT" => "查看遗物",
            "FEEDBACK" => "反馈页",
            "MODAL" => "弹窗",
            "GAME_OVER" => "游戏结束",
            "UNLOCK" => "解锁",
            "TIMELINE" => "时间线",
            "PATCH_NOTES" => "补丁说明",
            "UNKNOWN" => "未知界面",
            _ => null
        };
        return label == null ? (key.Length == 0 ? "-" : key) : Loc.T(label);
    }

    /// <summary>Who made a recorded decision: the fast-judgment layer, the LLM loop, or an outside client.</summary>
    public static string Source(string? source)
    {
        var key = source?.Trim() ?? string.Empty;
        var label = key switch
        {
            "jev" => "快速判断",
            "agent_loop" => "LLM",
            "autopilot" => "自动处理",
            "plan" => "整回合规划",
            "http_api" => "外部客户端（HTTP / MCP）",
            "native_mcp" => "外部智能体（MCP）",
            _ => null
        };
        return label == null ? (key.Length == 0 ? "-" : key) : Loc.T(label);
    }

    public static string Action(string? action)
    {
        var key = action?.Trim() ?? string.Empty;
        var label = key switch
        {
            "play_card" => "出牌",
            "end_turn" => "结束回合",
            "use_potion" => "使用药水",
            "discard_potion" => "丢弃药水",
            "choose_map_node" => "选择路线",
            "claim_reward" => "领取奖励",
            "choose_reward_card" => "选卡",
            "skip_reward_cards" => "跳过选卡",
            "collect_rewards_and_proceed" => "领取奖励并继续",
            "resolve_rewards" => "处理奖励",
            "proceed" => "继续",
            "choose_event_option" => "选择事件选项",
            "choose_rest_option" => "休息点选择",
            "open_shop_inventory" => "打开商店",
            "close_shop_inventory" => "关闭商店",
            "buy_card" => "买卡",
            "buy_relic" => "买遗物",
            "buy_potion" => "买药水",
            "remove_card_at_shop" => "商店删牌",
            "open_chest" => "开宝箱",
            "choose_treasure_relic" => "选择宝箱遗物",
            "select_deck_card" => "选择牌",
            "confirm_selection" => "确认选择",
            "close_cards_view" => "关闭看牌",
            "confirm_modal" => "确认弹窗",
            "dismiss_modal" => "关闭弹窗",
            "choose_bundle" => "选择卡包",
            "confirm_bundle" => "确认卡包",
            "choose_capstone_option" => "选择 Capstone",
            "crystal_set_tool" => "水晶球：选工具",
            "crystal_clear_cell" => "水晶球：清除格子",
            "continue_run" => "继续存档",
            "open_character_select" => "进入选角",
            "select_character" => "选择角色",
            "increase_ascension" => "提高进阶",
            "decrease_ascension" => "降低进阶",
            "embark" => "出发",
            "continue_game_over" => "结算继续",
            "dismiss_game_over_wait" => "跳过结算等待",
            "return_to_main_menu" => "回到主菜单",
            "confirm_unlock" => "确认解锁",
            "open_timeline" => "打开时间线",
            "choose_timeline_epoch" => "选择时间线纪元",
            "confirm_timeline_overlay" => "确认时间线",
            "close_main_menu_submenu" => "返回上一级菜单",
            "save_and_quit" => "保存并退出",
            "abandon_run" => "放弃本局",
            "host_multiplayer_lobby" => "创建联机房间",
            "join_multiplayer_lobby" => "加入联机房间",
            "ready_multiplayer_lobby" => "联机准备",
            "unready" => "取消准备",
            "disconnect_multiplayer_lobby" => "离开联机房间",
            "invite_ai_teammate" => "邀请 AI 队友",
            "continue_ai_teammate" => "继续联机存档",
            _ => null
        };
        return label == null ? (key.Length == 0 ? "-" : key) : Loc.T(label);
    }
}
