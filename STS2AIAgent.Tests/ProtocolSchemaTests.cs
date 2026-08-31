using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class ProtocolSchemaTests
{
    private static readonly string[] SceneProperties =
    [
        "combat", "run", "multiplayer", "multiplayer_lobby", "map", "selection", "character_select",
        "timeline", "unlock", "chest", "event", "crystal_sphere", "shop", "rest", "reward", "bundles",
        "capstone", "modal", "game_over", "agent_view"
    ];

    public static void Catalog_EmbedsEveryPublishedSchema()
    {
        Assert.Equal(4, ProtocolSchemaService.Names.Length);
        foreach (var name in ProtocolSchemaService.Names)
        {
            Assert.True(ProtocolSchemaService.TryGet(name, out var schema), name);
            Assert.Equal(ProtocolContract.SchemaDraft, schema.GetProperty("$schema").GetString());
            Assert.True(schema.TryGetProperty("examples", out var examples) && examples.GetArrayLength() > 0, name);
        }
    }

    public static void StateSchema_CoversEveryTopLevelScene()
    {
        Assert.True(ProtocolSchemaService.TryGet("state.schema.json", out var schema));
        var properties = schema.GetProperty("properties");
        foreach (var propertyName in SceneProperties)
        {
            Assert.True(properties.TryGetProperty(propertyName, out _), propertyName);
        }

        Assert.Equal(12, properties.GetProperty("state_version").GetProperty("const").GetInt32());
        Assert.True(schema.GetProperty("allOf").GetArrayLength() >= 16);
    }

    public static void EventSchema_CoversFactEventsAndCorrelationFields()
    {
        Assert.True(ProtocolSchemaService.TryGet("event", out var schema));
        var properties = schema.GetProperty("properties");
        foreach (var field in new[] { "event_id", "sequence", "protocol_version", "correlation_id", "run_id", "combat_id", "type", "timestamp_utc", "data" })
        {
            Assert.True(properties.TryGetProperty(field, out _), field);
        }

        var eventTypes = properties.GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var eventType in new[] { "action_started", "damage_resolved", "action_finished", "ai_decision", "combat_ended" })
        {
            Assert.True(eventTypes.Contains(eventType), eventType);
        }
    }
}
