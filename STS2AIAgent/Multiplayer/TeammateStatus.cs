using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

/// <summary>
/// The teammate's live game state, read from the companion instance's own <c>/state</c>.
/// </summary>
/// <remarks>
/// The host already knows whether the companion process is alive; it had no idea what that process's
/// character was doing. This turns the companion's payload into the two facts a co-op player actually
/// needs mid-fight — how much health the teammate has and whether it can act — without the host
/// having to guess from the process tree.
///
/// The connection reads the companion process, where the AI character has is_local=true.
/// Its combat.hand belongs to that same local player. Selecting a non-local player here mixes
/// the host's health and energy with the AI's hand.
///
/// Parsing never throws. A companion that is mid-transition, restarting, or answering a payload this
/// host does not understand must produce "no summary", not an exception on the overlay's tick.
/// </remarks>
/// <summary>The local AI player as the companion process reports it.</summary>
internal readonly record struct TeammatePlayer(
    string? PlayerId,
    int? CurrentHp,
    int? MaxHp,
    int? Block,
    int? Energy,
    bool IsAlive);

internal readonly record struct TeammateStatus(
    string? Screen,
    bool InCombat,
    IReadOnlyList<TeammatePlayer> Players,
    int HandCount)
{
    /// <summary>
    /// Reads a companion <c>/state</c> payload, or a status with no players when it cannot be read.
    /// </summary>
    public static TeammateStatus Parse(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(stateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            // HTTP /state wraps the payload in { ok, request_id, data }; direct bridge reads
            // and fixtures may already contain the payload. Never display data from an error.
            if (root.TryGetProperty("ok", out var ok))
            {
                if (ok.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return Empty;
                root = data;
            }

            var screen = Text(root, "screen");
            var combat = Object(root, "combat");
            var inCombat = combat is { } combatPayload;

            // In combat the live party block is combat.players[]; outside it, run.players[]. Both
            // carry is_local, which is what decides who counts as the teammate.
            var source = inCombat
                ? ArrayOf(combat!.Value, "players")
                : Object(root, "run") is { } run
                    ? ArrayOf(run, "players")
                    : System.Array.Empty<JsonElement>();

            var players = new List<TeammatePlayer>();
            foreach (var player in source)
            {
                // The payload is fetched from the companion, so its own player is the AI.
                if (Bool(player, "is_local") != true)
                {
                    continue;
                }

                players.Add(new TeammatePlayer(
                    Text(player, "player_id"),
                    Number(player, "current_hp"),
                    Number(player, "max_hp"),
                    Number(player, "block"),
                    Number(player, "energy"),
                    Bool(player, "is_alive") ?? true));
            }

            var handCount = inCombat ? ArrayOf(combat!.Value, "hand").Count : 0;
            return new TeammateStatus(screen, inCombat, players, handCount);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static TeammateStatus Empty => new(null, false, System.Array.Empty<TeammatePlayer>(), 0);

    /// <summary>One line for the overlay, or null when there is nothing to say.</summary>
    public string? Describe()
    {
        if (Players.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(Players.Count + 1);
        foreach (var player in Players)
        {
            var health = player.CurrentHp is { } hp && player.MaxHp is { } max
                ? hp + "/" + max
                : Loc.T("未知");

            var line = Loc.T("{0}：{1} HP", Label(player), health);
            if (player.Block is > 0)
            {
                line += Loc.T("，{0} 格挡", player.Block);
            }

            if (!player.IsAlive)
            {
                line += Loc.T("（已倒下）");
            }
            else if (InCombat && player.Energy is { } energy)
            {
                line += Loc.T("，{0} 能量", energy);
            }

            parts.Add(line);
        }

        if (InCombat)
        {
            parts.Add(Loc.T("手牌 {0} 张", HandCount));
        }

        return string.Join("　·　", parts);
    }

    private static string Label(TeammatePlayer player)
    {
        return string.IsNullOrWhiteSpace(player.PlayerId) ? Loc.T("队友") : player.PlayerId!;
    }

    private static JsonElement? Object(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static IReadOnlyList<JsonElement> ArrayOf(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return System.Array.Empty<JsonElement>();
        }

        var items = new List<JsonElement>();
        foreach (var item in value.EnumerateArray())
        {
            items.Add(item);
        }

        return items;
    }

    private static string? Text(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? Number(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static bool? Bool(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
