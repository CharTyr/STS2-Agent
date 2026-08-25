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

    private static readonly object WaitParameters = new
    {
        type = "object",
        properties = new
        {
            timeout_seconds = new { type = "number", description = "Maximum wait in seconds. Default 20." }
        }
    };

    public static readonly IReadOnlyList<LlmTool> ReadOnly = new[]
    {
        Tool("get_game_state", "Read the compact live game state. Always prefer this over memory. This is sufficient to play every screen without vision."),
        Tool("get_raw_game_state", "Read the full raw /state snapshot when compact agent_view is missing a field."),
        Tool("get_available_actions", "List currently legal actions with requires_index / requires_target hints."),
        Tool("get_game_data_item", "Look up one card/relic/monster/potion/event/power/character by id.", CollectionItemParameters),
        Tool("get_game_data_items", "Look up several metadata entities by comma-separated ids.", CollectionItemsParameters),
        Tool("get_relevant_game_data", "Look up metadata with fields trimmed for the current screen.", CollectionItemsParameters),
        Tool("wait_until_actionable", "Wait until a non-passive action is available, then return fresh compact state. Use during animations and screen transitions.", WaitParameters)
    };

    public static readonly IReadOnlyList<LlmTool> Play = ReadOnly.Concat(new[]
    {
        new LlmTool
        {
            Name = "act",
            Description = "Execute one legal game action. Only use names from the latest available_actions. Recompute indexes from the latest state.",
            Parameters = ActParameters
        }
    }).ToArray();

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
