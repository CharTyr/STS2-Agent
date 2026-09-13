using System.Globalization;
using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

/// <summary>
/// Pure half of the co-op save precheck: it reads the player ids out of a multiplayer run save and
/// decides whether this process may load that save. It deliberately references no game types, so
/// <c>STS2AIAgent.Tests</c> can compile and drive it offline.
///
/// Why the check exists at all: the game's own load path
/// (<c>MegaCrit.Sts2.Core.Runs.RunManager.CanonicalizeSave</c>) throws when the save's
/// <c>players[].net_id</c> set does not contain the local player id, and
/// <c>RunSaveManager.RenameBrokenMultiplayerRunSave</c> then renames <c>current_run_mp.save</c> and
/// its <c>.backup</c> to <c>*.VAL.corrupt</c> without restoring them. There is one multiplayer save
/// slot, so a mismatch has to be caught before the load is attempted.
/// </summary>
internal static class CoopSavePrecheckPolicy
{
    /// <summary>
    /// Player ids from <c>players[].net_id</c>. An empty list means "no evidence" for every
    /// caller: null, blank, malformed JSON and a missing <c>players</c> array all return empty
    /// instead of throwing, and ids given as numbers or numeric strings both count.
    /// </summary>
    public static IReadOnlyList<ulong> ReadPlayerNetIds(string? saveJson)
    {
        if (string.IsNullOrWhiteSpace(saveJson))
        {
            return Array.Empty<ulong>();
        }

        try
        {
            using var document = JsonDocument.Parse(saveJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("players", out var players)
                || players.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ulong>();
            }

            var ids = new List<ulong>();
            foreach (var player in players.EnumerateArray())
            {
                if (player.ValueKind != JsonValueKind.Object
                    || !player.TryGetProperty("net_id", out var netId)
                    || !TryReadNetId(netId, out var id))
                {
                    continue;
                }

                ids.Add(id);
            }

            return ids;
        }
        catch (JsonException)
        {
            // An unparseable save proves nothing about the ids, so it must not block a load.
            return Array.Empty<ulong>();
        }
    }

    /// <summary>
    /// Null when the save cannot prove a mismatch - unreadable ids, or an id that is present - so
    /// a save the probe could not read never blocks a legitimate load. Otherwise the player-facing
    /// explanation, which names both ids and the consequence.
    /// </summary>
    public static string? DescribeHostMismatch(IReadOnlyList<ulong> netIds, ulong localPlayerId)
    {
        if (netIds is not { Count: > 0 } || Contains(netIds, localPlayerId))
        {
            return null;
        }

        return Loc.T(
            "主机的 NetId {0} 不在联机存档的玩家列表（{1}）里：游戏会拒绝读档，并把这局存档改名成 .VAL.corrupt 挪走且不还原，所以现在开不了房。离线主机的 NetId 就是启动参数 --clientId 的值（未传时默认为 1），请改用它重开主机；或者先删掉这份存档再开。",
            localPlayerId,
            Join(netIds));
    }

    /// <summary>
    /// Null on the same fail-open rule as <see cref="DescribeHostMismatch"/>. The companion id has
    /// to be in the save too: the host refuses a join for an id that is not in the saved roster
    /// (<c>JoinFlow: Disconnected during join flow, reason: NotInSaveGame</c>), which leaves the
    /// teammate back at the main menu and the host stuck on the load screen.
    /// </summary>
    public static string? DescribeCompanionMismatch(IReadOnlyList<ulong> netIds, ulong companionClientId)
    {
        if (netIds is not { Count: > 0 } || Contains(netIds, companionClientId))
        {
            return null;
        }

        return Loc.T(
            "本次会用 NetId {0} 拉起 AI 队友，但联机存档的玩家列表（{1}）里没有这个 id：队友加入会被游戏拒绝（NotInSaveGame）退回主菜单，主机则卡在读档界面。队友 id 是主机 id + 1，请把主机启动参数 --clientId 改成「存档里队友的 id 减一」后重开。",
            companionClientId,
            Join(netIds));
    }

    private static bool TryReadNetId(JsonElement element, out ulong id)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetUInt64(out id);
            case JsonValueKind.String:
                return ulong.TryParse(
                    element.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out id);
            default:
                id = 0;
                return false;
        }
    }

    private static bool Contains(IReadOnlyList<ulong> ids, ulong id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    private static string Join(IReadOnlyList<ulong> ids)
    {
        var parts = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            parts[i] = ids[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", parts);
    }
}
