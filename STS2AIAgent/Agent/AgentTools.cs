using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

internal static class AgentTools
{
    private static readonly object ActParameters = new
    {
        type = "object",
        properties = new
        {
            action = new { type = "string", description = "Action name from available_actions." },
            card_index = new { type = "integer", description = "Hand card index for play_card." },
            target_index = new { type = "integer", description = "Target index when the card or potion requires a target." },
            option_index = new { type = "integer", description = "Option index for map/reward/shop/event/rest/lobby choices." },
            x = new { type = "integer", description = "Crystal Sphere grid x-coordinate for crystal_clear_cell." },
            y = new { type = "integer", description = "Crystal Sphere grid y-coordinate for crystal_clear_cell." },
            tool = new
            {
                type = "string",
                @enum = new[] { "big", "small" },
                description = "Crystal Sphere tool."
            },
            reason = new
            {
                type = "string",
                description = "One short sentence saying why you chose this action. Shown to the player as the decision's rationale."
            },
            raw_state = new
            {
                type = "boolean",
                description = "Return the full raw post-action state instead of the compact agent_view. Default false; only for a field the compact view does not carry."
            }
        },
        required = new[] { "action" }
    };

    private static readonly object CollectionItemParameters = new
    {
        type = "object",
        properties = new
        {
            collection = new { type = "string", description = "cards, relics, monsters, potions, events, powers, or characters." },
            item_id = new { type = "string", description = "Entity id, for example ABRASIVE." }
        },
        required = new[] { "collection", "item_id" }
    };

    private static readonly object CollectionItemsParameters = new
    {
        type = "object",
        properties = new
        {
            collection = new { type = "string", description = "cards, relics, monsters, potions, events, powers, or characters." },
            item_ids = new { type = "string", description = "Comma-separated entity ids." }
        },
        required = new[] { "collection", "item_ids" }
    };

    /// <summary>
    /// get_relevant_game_data names its ids optionally: the whole point of the tool is that the
    /// current scene decides what is relevant, so omitting item_ids is the normal call and the
    /// server derives them from live state.
    /// </summary>
    private static readonly object RelevantDataParameters = new
    {
        type = "object",
        properties = new
        {
            collection = new { type = "string", description = "cards, relics, monsters, potions, events, powers, or characters." },
            item_ids = new { type = "string", description = "Optional comma-separated ids. Omit to use the ids the current scene is about." }
        },
        required = new[] { "collection" }
    };

    private static readonly object WaitParameters = new
    {
        type = "object",
        properties = new
        {
            timeout_seconds = new { type = "number", description = "Maximum wait in seconds. Default 20." },
            raw_state = new
            {
                type = "boolean",
                description = "Return the full raw state instead of the compact agent_view. Default false; only for a field the compact view does not carry."
            }
        }
    };

    private static readonly object DecisionLogParameters = new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "How many recent decisions to return, newest last. Default 50, maximum 200." }
        }
    };

    private static readonly object DiffStateParameters = new
    {
        type = "object",
        properties = new
        {
            before = new { type = "object", description = "The earlier /state payload (the data object, not the whole envelope)." },
            after = new { type = "object", description = "The later /state payload to compare against it." },
            limit = new { type = "integer", description = "Maximum number of changed paths to report. Default 200." }
        },
        required = new[] { "before", "after" }
    };

    public static readonly IReadOnlyList<LlmTool> ReadOnly = new[]
    {
        Tool("get_game_state", "Read the compact live game state. Always prefer this over memory. This is sufficient to play every screen without vision."),
        Tool("get_raw_game_state", "Read the full raw /state snapshot when compact agent_view is missing a field."),
        Tool("get_available_actions", "List currently legal actions with requires_index / requires_target hints."),
        Tool("get_game_data_item", "Look up one card/relic/monster/potion/event/power/character by id.", CollectionItemParameters),
        Tool("get_game_data_items", "Look up several metadata entities by comma-separated ids.", CollectionItemsParameters),
        Tool("get_relevant_game_data", "Look up metadata with fields trimmed for the current screen. Omit item_ids to use the ids this screen is about.", RelevantDataParameters),
        Tool("wait_until_actionable", "Wait until a non-passive action is available, then return fresh compact state. Use during animations and screen transitions.", WaitParameters)
    };

    public static readonly IReadOnlyList<LlmTool> Play = ReadOnly.Concat(new[]
    {
        new LlmTool
        {
            Name = "act",
            Description = "Execute one legal game action. Only use names from the latest available_actions. Recompute indexes from the latest state, and attach a short reason so the player can see why. Returns the compact agent_view for the next decision unless raw_state is set.",
            Parameters = ActParameters
        }
    }).ToArray();

    public static readonly IReadOnlyList<LlmTool> Mcp = new[]
    {
        Tool("health_check", "Check whether the STS2 AI Agent mod is loaded and this MCP endpoint is open."),
        Tool("decide", "Read everything one decision needs in a single state read: the compact state, the legal actions with their index/target hints, and the guidance for the screen. Use it when a step would otherwise spend three calls rebuilding the same state."),
        Tool("get_decision_log", "Read the recent accepted decisions with the rationale each one carried. Newest last; use it to review why the agent played the way it did.", DecisionLogParameters),
        Tool("get_run_summary", "Summarise the current run in one call: character, floor, act, boss, HP, gold, and the deck/relic/potion counts."),
        Tool("get_scene_guidance", "Return the strategy rules that apply to the screen the game is on right now, plus how to drive it. Empty strategy on a screen with no strategic choice."),
        Tool("diff_state", "Compare two /state payloads and report the paths that differ. Use it to see exactly what an action changed.", DiffStateParameters),
        Tool("get_planner_briefing", "Read the current strategy, dual-layer/Jev flags, current-run summary and screen, plus up to five same-run Jev choices and their confidence trend. Missing run or scores remain null/empty. Use this read-only snapshot before adjusting the play strategy."),
        Tool("update_play_strategy", "Write a new dual-layer play strategy: a posture (aggressive/defensive/balanced), standing instructions, and optional per-option hints. The Jev execution model follows it on the next action. Only meaningful when dual-layer mode is on.", StrategyParameters)
    }.Concat(Play).ToArray();

    /// <summary>The parameters <c>update_play_strategy</c> accepts: the strategy fields, all optional.</summary>
    private static readonly object StrategyParameters = new
    {
        type = "object",
        properties = new
        {
            posture = new { type = "string", description = "Overall bias: aggressive, defensive, or balanced." },
            instructions = new { type = "string", description = "Standing guidance the execution model follows. Do not name specific indices; they change every frame." },
            option_hints = new { type = "object", description = "Optional per-option nudges keyed by option kind." }
        }
    };

    private static LlmTool Tool(string name, string description, object? parameters = null)
    {
        return new LlmTool
        {
            Name = name,
            Description = description,
            Parameters = parameters ?? new { type = "object", properties = new { } }
        };
    }
}
