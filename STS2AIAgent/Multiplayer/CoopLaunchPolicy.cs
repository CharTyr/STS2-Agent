using STS2AIAgent.Config;

namespace STS2AIAgent.Multiplayer;

internal readonly record struct CoopOccupancy(int Occupied, int Max, int FreeSlots)
{
    public bool KeepsFourPlayerLobby => Max == CoopLaunchPolicy.MaxLobbyPlayers && FreeSlots >= 2;
}

internal static class CoopLaunchPolicy
{
    public const int MaxLobbyPlayers = 4;
    public const int CompanionSlotCount = 1;
    public const ulong DefaultCompanionClientId = 1001;

    public static string CompanionArguments(string? forceSteam, string? clientId)
    {
        _ = forceSteam;
        var id = ResolveCompanionClientId(clientId)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "--windowed --force-steam off --clientId " + id + " -fastmp join";
    }

    public static ulong ResolveCompanionClientId(string? hostClientId)
    {
        if (string.IsNullOrWhiteSpace(hostClientId))
        {
            return DefaultCompanionClientId;
        }

        if (!ulong.TryParse(hostClientId, out var id))
        {
            throw new InvalidOperationException("Offline clientId must be a non-negative integer.");
        }

        if (id == ulong.MaxValue)
        {
            throw new InvalidOperationException("Offline clientId leaves no adjacent companion ID.");
        }

        return id + 1;
    }

    public static bool TryGetCompanionArguments(string? forceSteam, string? clientId, out string arguments, out string? error)
    {
        try
        {
            arguments = CompanionArguments(forceSteam, clientId);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            arguments = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    public static string CompanionSettingsPath(string mainSettingsPath, string? explicitCompanionPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitCompanionPath))
        {
            if (!System.IO.Path.IsPathFullyQualified(explicitCompanionPath))
                throw new InvalidOperationException("Companion settings path must be an absolute file path.");
            return System.IO.Path.GetFullPath(explicitCompanionPath);
        }

        if (string.IsNullOrWhiteSpace(mainSettingsPath))
            throw new ArgumentException("Main settings path must not be empty.", nameof(mainSettingsPath));

        var fullMain = System.IO.Path.GetFullPath(mainSettingsPath);
        var dir = System.IO.Path.GetDirectoryName(fullMain) ?? string.Empty;
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fullMain);
        var ext = System.IO.Path.GetExtension(fullMain);
        return System.IO.Path.Combine(dir, $"{nameWithoutExt}.companion{ext}");
    }

    public static void SeedCompanionSettings(string mainSettingsPath, string companionSettingsPath)
    {
        if (string.IsNullOrWhiteSpace(mainSettingsPath) || string.IsNullOrWhiteSpace(companionSettingsPath))
            return;

        if (string.Equals(System.IO.Path.GetFullPath(mainSettingsPath), System.IO.Path.GetFullPath(companionSettingsPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Companion settings path must be different from the host settings path.");

        var dir = System.IO.Path.GetDirectoryName(companionSettingsPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        if (System.IO.File.Exists(mainSettingsPath))
            System.IO.File.Copy(mainSettingsPath, companionSettingsPath, overwrite: true);
    }

    public static CoopOccupancy FromRoomState(
        int playerCount,
        int maxPlayers,
        IReadOnlyList<string>? connectedPlayerIds = null)
    {
        var occupied = connectedPlayerIds is { Count: > 0 } ? connectedPlayerIds.Count : playerCount;
        if (occupied < 1)
        {
            throw new InvalidOperationException("Local co-op needs the human host.");
        }

        var companionSlots = 0;
        if (connectedPlayerIds != null)
        {
            for (var i = 0; i < connectedPlayerIds.Count; i++)
            {
                if (!string.Equals(connectedPlayerIds[i], "1", StringComparison.Ordinal))
                {
                    companionSlots++;
                }
            }
        }
        else if (occupied >= 2)
        {
            companionSlots = occupied - 1;
        }

        if (companionSlots != CompanionSlotCount)
        {
            throw new InvalidOperationException("The AI teammate occupies exactly one player slot.");
        }

        if (maxPlayers != MaxLobbyPlayers || occupied > maxPlayers || maxPlayers - occupied < 2)
        {
            throw new InvalidOperationException("Local 1 human + 1 AI must leave room for online players.");
        }

        return new CoopOccupancy(occupied, maxPlayers, maxPlayers - occupied);
    }

    public static string? GetError(bool isCompanion, bool autoPlayRunning, string screen, ResolvedModel? model)
    {
        if (isCompanion) return "当前窗口已是 AI 队友。请在你的主窗口邀请队友。";
        if (autoPlayRunning) return "请先暂停当前角色的自动游玩，再邀请 AI 队友。";
        if (screen != "MAIN_MENU") return "请先回到主菜单，再邀请 AI 队友组队。";
        var firstRun = FirstRunSetup.Evaluate(model);
        if (!firstRun.ReadyToInvite) return firstRun.Hint;
        return null;
    }

    public static string? NextCompanionBootstrapAction(
        string screen,
        IReadOnlyList<string> availableActions,
        bool hasLobby)
    {
        var actions = availableActions ?? Array.Empty<string>();
        if (string.Equals(screen, "MODAL", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "dismiss_modal")) return "dismiss_modal";
            if (Contains(actions, "confirm_modal")) return "confirm_modal";
            return null;
        }

        if (string.Equals(screen, "CHARACTER_SELECT", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "embark")) return "embark";
            if (Contains(actions, "ready_multiplayer_lobby")) return "ready_multiplayer_lobby";
            if (Contains(actions, "select_character")) return "select_character";
            return null;
        }

        if (string.Equals(screen, "BUNDLE_SELECTION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(screen, "BUNDLES", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "confirm_bundle")) return "confirm_bundle";
            if (Contains(actions, "choose_bundle")) return "choose_bundle";
            return null;
        }

        if (string.Equals(screen, "CARD_SELECTION", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "confirm_selection")) return "confirm_selection";
            if (Contains(actions, "select_deck_card")) return "select_deck_card";
            return null;
        }

        if (string.Equals(screen, "CAPSTONE_SELECTION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(screen, "CAPSTONE", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "choose_capstone_option")) return "choose_capstone_option";
            return null;
        }

        if (string.Equals(screen, "EVENT", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "choose_event_option")) return "choose_event_option";
            if (Contains(actions, "proceed")) return "proceed";
            return null;
        }

        if (string.Equals(screen, "REWARD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(screen, "CARD_REWARD", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "claim_reward")) return "claim_reward";
            if (Contains(actions, "choose_reward_card")) return "choose_reward_card";
            if (Contains(actions, "skip_reward_cards")) return "skip_reward_cards";
            if (Contains(actions, "proceed")) return "proceed";
            return null;
        }

        if (string.Equals(screen, "MULTIPLAYER_LOBBY", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasLobby && Contains(actions, "join_multiplayer_lobby")) return "join_multiplayer_lobby";
            if (Contains(actions, "ready_multiplayer_lobby")) return "ready_multiplayer_lobby";
            if (Contains(actions, "select_character")) return "select_character";
            return null;
        }

        if (string.Equals(screen, "MAIN_MENU", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(actions, "dismiss_modal")) return "dismiss_modal";
            if (Contains(actions, "confirm_modal")) return "confirm_modal";
            // -fastmp join opens the friends submenu and connects in the background.
            // Closing that submenu cancels JoinFlow; do not host from the companion.
            return null;
        }

        return null;
    }

    public static bool NeedsOptionIndex(string action)
    {
        return action is "select_character"
            or "choose_bundle"
            or "select_deck_card"
            or "choose_event_option"
            or "choose_capstone_option"
            or "claim_reward"
            or "choose_reward_card";
    }

    public static bool CompanionHasJoinedRun(string screen, IReadOnlyList<string> availableActions)
    {
        _ = availableActions;
        // Personal setup screens (Neow, bundles, cards) still need bootstrap
        // clicks. Auto-play starts on shared climb screens so both players
        // reach the map together instead of stalling on EVENT.
        return screen is "MAP" or "COMBAT" or "MAP_WAIT" or "REST" or "SHOP"
            or "TREASURE" or "CHEST" or "GAME_OVER" or "CRYSTAL_SPHERE";
    }

    private static bool Contains(IReadOnlyList<string> actions, string name)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
