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
            option_index = new { type = "integer", description = "Option index for map/reward/shop/event/rest/lobby choices." }
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

    public static readonly IReadOnlyList<LlmTool> ReadOnly = new[]
    {
        Tool("get_game_state", "Read the compact live game state. Always prefer this over memory."),
        Tool("get_available_actions", "List currently legal actions with requires_index / requires_target hints."),
        Tool("get_game_data_item", "Look up one card/relic/monster/potion/event/power/character by id.", CollectionItemParameters),
        Tool("get_game_data_items", "Look up several metadata entities by comma-separated ids.", CollectionItemsParameters),
        Tool("get_relevant_game_data", "Look up metadata with fields trimmed for the current screen.", CollectionItemsParameters)
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
