using System.Reflection;
using System.Text.Json;

namespace STS2AIAgent.Server;

internal static class ProtocolSchemaService
{
    private const string ResourcePrefix = "STS2AIAgent.Schemas.2026-08-31-v2.";

    private static readonly IReadOnlyDictionary<string, string> Resources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["health"] = ResourcePrefix + "health.schema.json",
            ["state"] = ResourcePrefix + "state.schema.json",
            ["event"] = ResourcePrefix + "event.schema.json",
            ["data-collection"] = ResourcePrefix + "data-collection.schema.json"
        };

    public static string[] Names { get; } = Resources.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();

    public static bool TryGet(string rawName, out JsonElement schema)
    {
        var name = rawName.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)
            ? rawName[..^".schema.json".Length]
            : rawName;
        if (!Resources.TryGetValue(name, out var resourceName))
        {
            schema = default;
            return false;
        }

        using var stream = typeof(ProtocolSchemaService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded protocol schema is missing: {resourceName}");
        using var document = JsonDocument.Parse(stream);
        schema = document.RootElement.Clone();
        return true;
    }
}
