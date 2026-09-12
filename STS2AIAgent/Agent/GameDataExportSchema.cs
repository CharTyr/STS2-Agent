namespace STS2AIAgent.Agent;

/// <summary>
/// Field contract for <c>GET /data/{collection}</c>, transcribed from the literal property
/// names assigned by the matching <c>ExportXxx()</c> method in
/// <see cref="STS2AIAgent.Game.GameDataExportService"/>.
///
/// This is the offline truth source for "what the game actually exports". Scene field sets in
/// <see cref="GameDataFilter"/> must only reference names found here, and
/// <c>GameDataExportSchemaTests</c> walks both directions: scene fields must exist in this
/// inventory, and every inventory field must still be assigned inside the export code. A field
/// added to or removed from the export must be reflected here in the same change.
/// </summary>
internal static class GameDataExportSchema
{
    /// <summary>
    /// Exported fields per collection, in the order the export methods assign them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Collections =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["cards"] = new[]
            {
                "id", "name", "description", "description_raw", "type", "rarity", "target", "cost",
                "is_x_cost", "star_cost", "is_x_star_cost", "color", "damage", "block", "keywords",
                "tags", "vars", "upgrade"
            },
            ["relics"] = new[]
            {
                "id", "name", "description", "rarity", "pool", "is_melted"
            },
            ["potions"] = new[]
            {
                "id", "name", "description", "rarity", "pool", "usage", "target_type"
            },
            ["events"] = new[]
            {
                "id", "name", "type", "act", "description", "options"
            },
            ["powers"] = new[]
            {
                "id", "name", "description", "type", "stack_type", "allow_negative"
            },
            ["characters"] = new[]
            {
                "id", "name", "description", "starting_hp", "starting_gold", "max_energy",
                "orb_slots", "gender", "color", "starting_deck", "starting_relics", "starting_potions"
            },
            ["monsters"] = new[]
            {
                "id", "name", "type", "min_hp", "max_hp", "moves", "damage_values", "block_values"
            }
        };

    /// <summary>
    /// Looks up the exported field list for a collection name.
    /// </summary>
    public static bool TryGetFields(string collection, out IReadOnlyList<string> fields)
    {
        if (Collections.TryGetValue(collection, out var known))
        {
            fields = known;
            return true;
        }

        fields = Array.Empty<string>();
        return false;
    }
}
