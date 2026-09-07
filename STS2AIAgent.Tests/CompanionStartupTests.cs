using System.Net;
using System.Text.Json;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Config;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class CompanionStartupTests
{
    public static void OfflineLaunchKeepsAccountsIsolated()
    {
        Assert.Equal(1002UL, CoopLaunchPolicy.ResolveCompanionClientId("1001"));
        Assert.Equal(CoopLaunchPolicy.DefaultCompanionClientId, CoopLaunchPolicy.ResolveCompanionClientId(null));
        Assert.Equal("--windowed --force-steam off --clientId 902 -fastmp join", CoopLaunchPolicy.CompanionArguments(null, "901"));
        Assert.Equal("--windowed --force-steam off --clientId 902 -fastmp join", CoopLaunchPolicy.CompanionArguments("off", "901"));
        Assert.Equal("--windowed --force-steam off --clientId 1001 -fastmp join", CoopLaunchPolicy.CompanionArguments("off", null));
        Assert.Contains("-fastmp join", CoopLaunchPolicy.CompanionArguments(null, null));

        Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionArguments("off", ulong.MaxValue.ToString()));
        Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionArguments("off", "not-a-number"));

        Assert.True(CoopLaunchPolicy.TryGetCompanionArguments("off", "901", out var args, out var err));
        Assert.Equal("--windowed --force-steam off --clientId 902 -fastmp join", args);
        Assert.True(err == null);

        Assert.False(CoopLaunchPolicy.TryGetCompanionArguments("off", ulong.MaxValue.ToString(), out var badArgs, out var badErr));
        Assert.Equal(string.Empty, badArgs);
        Assert.True(badErr != null && badErr.Contains("adjacent companion ID"));
    }

    public static void LocalJoinOccupiesOneSlotInFourPlayerLobby()
    {
        var occupancy = OccupancyFromStateJson("""
            {"screen":"CHARACTER_SELECT","multiplayer":{"connected_player_ids":["1","1001"]},"character_select":{"player_count":2,"max_players":4}}
            """);
        Assert.Equal(2, occupancy.Occupied);
        Assert.Equal(4, occupancy.Max);
        Assert.Equal(2, occupancy.FreeSlots);
        Assert.True(occupancy.KeepsFourPlayerLobby);

        Expect<InvalidOperationException>(() => OccupancyFromStateJson("""
            {"screen":"CHARACTER_SELECT","multiplayer":{"connected_player_ids":["1","1001"]},"character_select":{"player_count":2,"max_players":2}}
            """));
        Expect<InvalidOperationException>(() => OccupancyFromStateJson("""
            {"screen":"CHARACTER_SELECT","multiplayer":{"connected_player_ids":["1","1001"]},"character_select":{"player_count":2,"max_players":0}}
            """));
        Expect<InvalidOperationException>(() => CoopLaunchPolicy.FromRoomState(2, 2, new[] { "1", "1001" }));
        Expect<InvalidOperationException>(() => CoopLaunchPolicy.FromRoomState(3, 4, new[] { "1", "1001", "1002" }));
        Expect<InvalidOperationException>(() => CoopLaunchPolicy.FromRoomState(1, 4, new[] { "1" }));
    }

    private static CoopOccupancy OccupancyFromStateJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ids = root.GetProperty("multiplayer").GetProperty("connected_player_ids")
            .EnumerateArray().Select(id => id.GetString() ?? string.Empty).ToArray();
        var select = root.GetProperty("character_select");
        return CoopLaunchPolicy.FromRoomState(
            select.GetProperty("player_count").GetInt32(),
            select.GetProperty("max_players").GetInt32(),
            ids);
    }

    public static void CompanionActionsTargetOnlyLocalCharacter()
    {
        Assert.True(CompanionActPolicy.Allows("play_card", isCompanion: true, actorIsLocal: true, requestedPlayerId: "1001", localPlayerId: "1001"));
        Assert.True(!CompanionActPolicy.Allows("play_card", isCompanion: true, actorIsLocal: false, requestedPlayerId: "1", localPlayerId: "1001"));
        Assert.True(!CompanionActPolicy.Allows("end_turn", isCompanion: true, actorIsLocal: true, requestedPlayerId: "1", localPlayerId: "1001"));
        Assert.True(CompanionActPolicy.Allows("play_card", isCompanion: false, actorIsLocal: true, requestedPlayerId: "1", localPlayerId: "1"));
        Assert.True(!CompanionActPolicy.Allows("", isCompanion: true, actorIsLocal: true));
        Assert.True(CompanionActPolicy.Allows("choose_event_option", isCompanion: true, actorIsLocal: true));
        Assert.True(CompanionActPolicy.Allows("choose_map_node", isCompanion: true, actorIsLocal: true));
    }

    public static void CompanionBootstrapJoinsAsExtraPlayerThenReady()
    {
        Assert.True(CoopLaunchPolicy.NextCompanionBootstrapAction("MAIN_MENU", Array.Empty<string>(), hasLobby: false) == null);
        Assert.True(CoopLaunchPolicy.NextCompanionBootstrapAction(
            "MAIN_MENU",
            new[] { "close_main_menu_submenu" },
            hasLobby: false) == null);
        Assert.Equal("join_multiplayer_lobby", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "MULTIPLAYER_LOBBY",
            new[] { "join_multiplayer_lobby" },
            hasLobby: false));
        Assert.Equal("ready_multiplayer_lobby", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "MULTIPLAYER_LOBBY",
            new[] { "select_character", "ready_multiplayer_lobby" },
            hasLobby: true));
        Assert.Equal("embark", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "CHARACTER_SELECT",
            new[] { "select_character", "embark" },
            hasLobby: true));
        Assert.True(CoopLaunchPolicy.NextCompanionBootstrapAction(
            "CHARACTER_SELECT",
            new[] { "unready" },
            hasLobby: true) == null);
        Assert.Equal("choose_bundle", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "BUNDLE_SELECTION",
            new[] { "choose_bundle" },
            hasLobby: true));
        Assert.Equal("confirm_bundle", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "BUNDLE_SELECTION",
            new[] { "choose_bundle", "confirm_bundle" },
            hasLobby: true));
        Assert.Equal("select_deck_card", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "CARD_SELECTION",
            new[] { "select_deck_card" },
            hasLobby: true));
        Assert.Equal("choose_event_option", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "EVENT",
            new[] { "choose_event_option" },
            hasLobby: true));
        Assert.Equal("claim_reward", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "REWARD",
            new[] { "claim_reward" },
            hasLobby: true));
        Assert.Equal("choose_capstone_option", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "CAPSTONE_SELECTION",
            new[] { "choose_capstone_option" },
            hasLobby: true));
        Assert.Equal("dismiss_modal", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "MODAL",
            new[] { "confirm_modal", "dismiss_modal" },
            hasLobby: false));
        Assert.True(CoopLaunchPolicy.NeedsOptionIndex("choose_event_option"));
        Assert.True(CoopLaunchPolicy.NeedsOptionIndex("select_character"));
        Assert.True(!CoopLaunchPolicy.NeedsOptionIndex("embark"));
        Assert.True(!CoopLaunchPolicy.CompanionHasJoinedRun("CHARACTER_SELECT", new[] { "unready" }));
        Assert.True(!CoopLaunchPolicy.CompanionHasJoinedRun("EVENT", new[] { "choose_event_option" }));
        Assert.True(!CoopLaunchPolicy.CompanionHasJoinedRun("BUNDLE_SELECTION", new[] { "choose_bundle" }));
        Assert.True(CoopLaunchPolicy.CompanionHasJoinedRun("MAP", new[] { "choose_map_node" }));
        Assert.True(CoopLaunchPolicy.CompanionHasJoinedRun("COMBAT", new[] { "play_card", "end_turn" }));
        Assert.True(!CoopLaunchPolicy.CompanionHasJoinedRun("MAIN_MENU", new[] { "close_main_menu_submenu" }));
        Assert.Equal(CompanionImmediateDecision.Wait, CompanionPlayPolicy.DecideMapVote(
            "MAP",
            new[] { "choose_map_node" },
            Array.Empty<CompanionMapOption>()).Kind);
        var follow = CompanionPlayPolicy.DecideMapVote(
            "MAP",
            new[] { "choose_map_node" },
            new[]
            {
                new CompanionMapOption(0, 0, false),
                new CompanionMapOption(1, 1, false)
            });
        Assert.Equal(CompanionImmediateDecision.Act, follow.Kind);
        Assert.Equal("choose_map_node", follow.Action);
        Assert.Equal(1, follow.OptionIndex);
        Assert.Equal(CompanionImmediateDecision.Wait, CompanionPlayPolicy.DecideMapVote(
            "MAP",
            new[] { "choose_map_node" },
            new[] { new CompanionMapOption(0, 1, true) }).Kind);
        Assert.Equal(CompanionImmediateDecision.None, CompanionPlayPolicy.DecideMapVote(
            "COMBAT",
            new[] { "play_card" },
            null).Kind);
    }

    public static void CombatRulesFtueIsConfirmedImmediately()
    {
        var captured = CompanionPlayPolicy.DecideBlockingModal(
            "MODAL",
            new[] { "confirm_modal" },
            "NCombatRulesFtue",
            canConfirm: true,
            canDismiss: false,
            inCombat: true);
        Assert.Equal(CompanionImmediateDecision.Act, captured.Kind);
        Assert.Equal("confirm_modal", captured.Action);

        var hostAutoplay = CompanionPlayPolicy.DecideImmediate(
            "MODAL",
            new[] { "confirm_modal" },
            null,
            "NCombatRulesFtue",
            canConfirm: true,
            canDismiss: false,
            inCombat: true,
            followMapVotes: false);
        Assert.Equal(CompanionImmediateDecision.Act, hostAutoplay.Kind);
        Assert.Equal("confirm_modal", hostAutoplay.Action);

        var companionAutoplay = CompanionPlayPolicy.DecideImmediate(
            "MODAL",
            new[] { "confirm_modal" },
            new[] { new CompanionMapOption(0, 1, false) },
            "NCombatRulesFtue",
            canConfirm: true,
            canDismiss: false,
            inCombat: true,
            followMapVotes: true);
        Assert.Equal("confirm_modal", companionAutoplay.Action);

        Assert.Equal(CompanionImmediateDecision.None, CompanionPlayPolicy.DecideBlockingModal(
            "MODAL",
            new[] { "confirm_modal", "dismiss_modal" },
            "NConfirmDialog",
            canConfirm: true,
            canDismiss: true,
            inCombat: false).Kind);
        Assert.Equal(CompanionImmediateDecision.None, CompanionPlayPolicy.DecideBlockingModal(
            "COMBAT",
            new[] { "play_card" },
            "NCombatRulesFtue",
            canConfirm: false,
            canDismiss: false,
            inCombat: true).Kind);
        Assert.Equal(CompanionImmediateDecision.None, CompanionPlayPolicy.DecideImmediate(
            "COMBAT",
            new[] { "play_card" },
            null,
            null,
            canConfirm: false,
            canDismiss: false,
            inCombat: true,
            followMapVotes: false).Kind);
        Assert.Equal("choose_map_node", CompanionPlayPolicy.DecideImmediate(
            "MAP",
            new[] { "choose_map_node" },
            new[] { new CompanionMapOption(0, 1, false) },
            null,
            canConfirm: false,
            canDismiss: false,
            inCombat: false,
            followMapVotes: true).Action);

        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        Assert.Contains("CompanionPlayPolicy.DecideImmediate(", runtime);
        Assert.Contains("payload.modal?.type_name", runtime);
        var waitMarker = runtime.IndexOf("等待你选择地图节点", StringComparison.Ordinal);
        Assert.True(waitMarker >= 0);
        var waitSnippet = runtime.Substring(waitMarker, Math.Min(280, runtime.Length - waitMarker));
        Assert.Contains("WaitingForGame = true", waitSnippet);
        var lobby = AgentSourceFixture.Read("scripts/test-multiplayer-lobby-flow.ps1");
        Assert.Contains("Clear-BlockingFtueModals", lobby);
        Assert.Contains("Test-ShouldConfirmBlockingModal", lobby);
    }

    public static void FirstRunProviderConfigIsReachable()
    {
        var defaults = AgentSettings.CreateDefault();
        var filled = FirstRunSetup.Evaluate(defaults);
        Assert.True(!filled.ReadyToInvite);
        Assert.Equal("filled_unverified", filled.Phase);
        Assert.True(filled.ProviderConfigReachable);
        ModelRoleProbe.Upsert(defaults, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, defaults.TryResolvePlayModel()!));
        var ready = FirstRunSetup.Evaluate(defaults);
        Assert.True(ready.ReadyToInvite);
        Assert.Contains("1 人", ready.Hint);
        Assert.Contains("1 AI", ready.Hint);

        var empty = FirstRunSetup.Evaluate((ResolvedModel?)null);
        Assert.True(!empty.ReadyToInvite);
        Assert.Contains("设置", empty.Hint);

        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].BaseUrl = "not-a-url";
        var invalid = FirstRunSetup.Evaluate(settings);
        Assert.True(!invalid.ReadyToInvite);
        Assert.Contains("地址", invalid.Hint);
        Assert.Contains("设置", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", (ResolvedModel?)null));
    }

    public static void CompanionProfileEnablesTheMod()
    {
        var created = System.Text.Json.Nodes.JsonNode.Parse(CompanionProfileBootstrap.EnsureModsAgreed(null));
        Assert.True(created!["mod_settings"]!["mods_enabled"]!.GetValue<bool>());
        Assert.Contains("STS2AIAgent", created.ToJsonString());
        var root = Path.Combine(Path.GetTempPath(), "sts2-coop-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var steamDir = Path.Combine(root, "steam", "76561198000000000");
            Directory.CreateDirectory(steamDir);
            File.WriteAllText(Path.Combine(steamDir, "settings.save"), """
                { "mod_settings": { "mods_enabled": false, "mod_list": [ { "id": "OtherMod", "is_enabled": true } ] } }
                """);
            var written = CompanionProfileBootstrap.WriteCompanionSave(root, "1001");
            Assert.Equal(CompanionProfileBootstrap.CompanionSavePath(root, "1001"), written);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(written));
            Assert.True(json!["mod_settings"]!["mods_enabled"]!.GetValue<bool>());
            Assert.Contains("STS2AIAgent", json.ToJsonString());
            Assert.Contains("OtherMod", json.ToJsonString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static void SettingsPathCanBeIsolated()
    {
        var previous = Environment.GetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), "sts2-coop-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var isolated = Path.Combine(tempDir, "settings.json");
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", isolated);
            Assert.Equal(Path.GetFullPath(isolated), new SettingsStore().Path);
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", "relative.json");
            Expect<InvalidOperationException>(() => new SettingsStore());

            var derived = CoopLaunchPolicy.CompanionSettingsPath(isolated);
            Assert.Equal(Path.Combine(tempDir, "settings.companion.json"), derived);

            var explicitCustom = Path.Combine(tempDir, "custom.companion.json");
            Assert.Equal(explicitCustom, CoopLaunchPolicy.CompanionSettingsPath(isolated, explicitCustom));
            Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionSettingsPath(isolated, "relative.companion.json"));
            Expect<ArgumentException>(() => CoopLaunchPolicy.CompanionSettingsPath(""));
            Expect<InvalidOperationException>(() => CoopLaunchPolicy.SeedCompanionSettings(isolated, isolated));

            File.WriteAllText(isolated, "{\"main\":true}");
            Assert.False(File.Exists(derived));
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.True(File.Exists(derived));
            Assert.Equal("{\"main\":true}", File.ReadAllText(derived));

            File.WriteAllText(derived, "{\"companion\":true}");
            File.WriteAllText(isolated, "{\"main\":updated}");
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.Equal("{\"main\":updated}", File.ReadAllText(derived));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", previous);
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static void CompanionPortFileRoundTrip()
    {
        var pid = 424242;
        CompanionPortFile.Delete(pid);
        Assert.True(CompanionPortFile.TryRead(pid) == null);
        CompanionPortFile.Write(pid, 18081);
        Assert.Equal(18081, CompanionPortFile.TryRead(pid));
        var ports = CompanionPortFile.EnumerateCandidates(18080, pid).ToArray();
        Assert.Equal(18080, ports[0]);
        Assert.True(ports.Contains(18081));
        CompanionPortFile.Delete(pid);
        Assert.True(CompanionPortFile.TryRead(pid) == null);
    }

    public static void CompanionBootstrapDoesNotHostLobby()
    {
        var source = File.ReadAllText(FindSource("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs"));
        var start = source.IndexOf("RunCompanionBootstrapAsync", StringComparison.Ordinal);
        var open = source.IndexOf('{', start);
        var depth = 0;
        var end = open;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                end = i;
                break;
            }
        }

        var body = source[open..(end + 1)];
        Assert.False(body.Contains("OpenMultiplayerTestAsync", StringComparison.Ordinal));
        Assert.Contains("NextCompanionBootstrapAction", body, StringComparison.Ordinal);
        Assert.Equal("join_multiplayer_lobby", CoopLaunchPolicy.NextCompanionBootstrapAction(
            "MULTIPLAYER_LOBBY",
            new[] { "join_multiplayer_lobby" },
            hasLobby: false));
    }

    public static void HealthRequiresExactCompanionIdentity()
    {
        string Health(int pid = 123, int port = 8081, string role = "companion", string service = "sts2-ai-agent", string status = "ready") =>
            JsonSerializer.Serialize(new { ok = true, data = new { process_id = pid, api_port = port, instance_role = role, service, status } });
        Assert.True(CompanionHealth.IsExpectedProcess(Health(), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(pid: 456), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(port: 8082), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(role: "human"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(service: "other"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(status: "not ready"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("{\"ok\":false,\"status\":\"ready\"}", 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("{\"ok\":true,\"data\":[]}", 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("not JSON", 8081, 123));
    }

    public static void LaunchPreconditionsProtectHumanRun()
    {
        var settings = AgentSettings.CreateDefault();
        var model = settings.ResolvePlayModel();
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model) == null);
        Assert.Contains("主窗口", CoopLaunchPolicy.GetError(true, false, "MAIN_MENU", model));
        Assert.Contains("暂停", CoopLaunchPolicy.GetError(false, true, "MAIN_MENU", model));
        Assert.Contains("主菜单", CoopLaunchPolicy.GetError(false, false, "COMBAT", model));
        Assert.Contains("模型", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", (ResolvedModel?)null));
        model.Endpoint.BaseUrl = "file:///tmp/model";
        Assert.Contains("地址", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model));
        model.Endpoint.BaseUrl = "http://localhost:1234/v1";
        model.Endpoint.ApiKey = "";
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model) == null);
    }

    private static string FindSource(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(relativePath);
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
