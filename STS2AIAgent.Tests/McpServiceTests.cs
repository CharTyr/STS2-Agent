using System.Net;
using System.Text;
using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class McpServiceTests
{
    public static async Task Disabled_Returns403()
    {
        var server = CreateServer(enabled: false);
        var result = await server.ProcessAsync("POST", "application/json", null, """{"jsonrpc":"2.0","id":1,"method":"initialize"}""", CancellationToken.None);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("mcp_disabled", result.Body);
    }

    public static async Task Initialize_ReturnsServerInfoAndSession()
    {
        var server = CreateServer();
        var result = await server.ProcessAsync(
            "POST",
            "application/json",
            null,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
            CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.SessionId);
        using var doc = JsonDocument.Parse(result.Body!);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        var info = root.GetProperty("result").GetProperty("serverInfo");
        Assert.Equal("sts2-ai-agent", info.GetProperty("name").GetString());
        Assert.Equal("9.9.9", info.GetProperty("version").GetString());
        Assert.Equal("2025-03-26", root.GetProperty("result").GetProperty("protocolVersion").GetString());
        var instructions = root.GetProperty("result").GetProperty("instructions").GetString();
        Assert.Contains("continue_game_over", instructions);
        Assert.Contains("get_game_state", instructions);
        Assert.Contains("same play contract as the STS2 MCP player skill", instructions);
    }

    public static async Task ToolsList_IncludesHealthAndAct()
    {
        var server = CreateServer();
        var listed = await Rpc(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var names = listed.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.True(names.Contains("health_check"), "expected health_check");
        Assert.True(names.Contains("get_game_state"), "expected get_game_state");
        Assert.True(names.Contains("act"), "expected act");
        var act = listed.GetProperty("result").GetProperty("tools").EnumerateArray()
            .First(tool => tool.GetProperty("name").GetString() == "act");
        Assert.True(act.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("action", out _));
    }

    public static async Task Resources_ExposeSharedPlaySkill()
    {
        var server = CreateServer();
        var listed = await Rpc(server, """{"jsonrpc":"2.0","id":12,"method":"resources/list"}""");
        var uris = listed.GetProperty("result").GetProperty("resources").EnumerateArray()
            .Select(resource => resource.GetProperty("uri").GetString())
            .ToArray();
        Assert.True(uris.Contains("sts2://skill/play-contract"), "expected play-contract resource");
        Assert.True(uris.Contains("sts2://skill/screen-playbooks"), "expected screen-playbooks resource");

        var read = await Rpc(server, """{"jsonrpc":"2.0","id":13,"method":"resources/read","params":{"uri":"sts2://skill/play-contract"}}""");
        var text = read.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
        Assert.Contains("continue_game_over", text);
        Assert.Contains("local_vote", text);
    }

    public static async Task ToolsCall_GetGameStateAndAct()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);
        var state = await Rpc(server, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_game_state"}}""");
        Assert.False(state.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("COMBAT", state.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

        var acted = await Rpc(server, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":0}}}""");
        Assert.False(acted.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("play_card", bridge.LastAction);
        Assert.Equal(0, bridge.LastCardIndex);
        Assert.Equal(1, bridge.ActCalls);
    }

    public static async Task ToolsCall_DecisionLogRecordsAcceptedActOnly()
    {
        var bridge = new FakeMcpBridge();
        var decisions = new DecisionLog();
        var server = CreateServer(bridge: bridge, decisions: decisions);

        var rejected = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":20,"method":"tools/call","params":{"name":"act","arguments":{"action":"not_a_real_action","reason":"should never be logged"}}}""");
        Assert.True(rejected.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, decisions.Snapshot().Count);

        var accepted = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":0,"reason":"Strike the weakest slime.  "}}}""");
        Assert.False(accepted.GetProperty("result").GetProperty("isError").GetBoolean());

        var logged = await Rpc(server, """{"jsonrpc":"2.0","id":22,"method":"tools/call","params":{"name":"get_decision_log","arguments":{"limit":10}}}""");
        var text = logged.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var doc = JsonDocument.Parse(text!);
        var entries = doc.RootElement.GetProperty("decisions");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("native_mcp", entries[0].GetProperty("source").GetString());
        Assert.Equal("play_card", entries[0].GetProperty("action").GetString());
        Assert.Equal("Strike the weakest slime.", entries[0].GetProperty("reason").GetString());
    }

    public static async Task NativeServerWithoutDecisionLog_StaysSilent()
    {
        var server = CreateServer();
        var read = await Rpc(server, """{"jsonrpc":"2.0","id":23,"method":"tools/call","params":{"name":"get_decision_log"}}""");
        var text = read.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal(0, doc.RootElement.GetProperty("decisions").GetArrayLength());
    }

    public static async Task ToolsCall_RunSummaryUsesRawState()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        var summary = await Rpc(server, """{"jsonrpc":"2.0","id":24,"method":"tools/call","params":{"name":"get_run_summary"}}""");
        var text = summary.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var document = JsonDocument.Parse(text!);

        // The summary is built from the raw payload, whose field names docs/api.md documents, rather
        // than from the compact view that renames many of them.
        Assert.True(bridge.RawStateCalls > 0, "expected get_run_summary to read the raw state");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("run").ValueKind);
    }

    /// <summary>
    /// Some MCP clients serialize the arguments object into a JSON string. A stringified array or
    /// scalar must degrade to an empty arguments object (the tool then answers with its own
    /// structured error) instead of throwing <see cref="InvalidOperationException"/> out of the
    /// argument readers.
    /// </summary>
    public static async Task ToolsCall_StringifiedNonObjectArgumentsDegradeToEmpty()
    {
        var server = CreateServer();

        var array = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":30,"method":"tools/call","params":{"name":"act","arguments":"[1,2]"}}""");
        var arrayText = array.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("action is required", arrayText);

        var scalar = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":31,"method":"tools/call","params":{"name":"act","arguments":"42"}}""");
        var scalarText = scalar.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("action is required", scalarText);
    }

    /// <summary>
    /// A stale index has to be correctable on this surface too: the tool answers with the same error
    /// object the HTTP API sends, and its details name the offending field and the indices the
    /// payload actually offers.
    /// </summary>
    public static async Task ToolsCall_IndexRejectionNamesTheValidIndices()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        var rejected = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":40,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":9}}}""");

        Assert.True(rejected.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, bridge.ActCalls);
        using var document = JsonDocument.Parse(
            rejected.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.Equal("invalid_target", error.GetProperty("code").GetString());
        Assert.Equal("card_index 9 is not in the latest combat.hand.", error.GetProperty("message").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(9, root.GetProperty("card_index").GetInt32());

        var details = error.GetProperty("details");
        Assert.Equal("card_index", details.GetProperty("field").GetString());
        Assert.Equal(9, details.GetProperty("submitted").GetInt32());
        Assert.Equal("combat.hand", details.GetProperty("valid_field").GetString());
        Assert.True(
            details.GetProperty("valid_indices").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 0 }),
            "the fake payload's hand holds index 0 only");
        Assert.Equal("end_turn", details.GetProperty("available_actions")[1].GetString());
    }

    /// <summary>
    /// raw_state is the escape hatch from the compact post-action snapshot, and it has to reach the
    /// bridge rather than being read and dropped.
    /// </summary>
    public static async Task ToolsCall_RawStateFlagReachesTheBridge()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        await Rpc(server, """{"jsonrpc":"2.0","id":41,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":0}}}""");
        Assert.False(bridge.LastRawState, "the compact view is the default");

        await Rpc(server, """{"jsonrpc":"2.0","id":42,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":0,"raw_state":true}}}""");
        Assert.True(bridge.LastRawState, "raw_state must reach the bridge");

        // The legality check and the index validator share one snapshot: this used to be three state
        // builds per act, each its own game-thread turn.
        Assert.Equal(2, bridge.SnapshotCalls);
    }

    /// <summary>
    /// decide answers the whole documented loop from one state read, and its guidance block is the
    /// same one get_scene_guidance returns.
    /// </summary>
    public static async Task ToolsCall_DecideAnswersOneDecisionPerRead()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        var decided = await Rpc(server, """{"jsonrpc":"2.0","id":46,"method":"tools/call","params":{"name":"decide"}}""");

        Assert.False(decided.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(1, bridge.SnapshotCalls);
        using var document = JsonDocument.Parse(
            decided.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var root = document.RootElement;

        Assert.Equal("COMBAT", root.GetProperty("state").GetProperty("screen").GetString());
        Assert.Equal("play_card", root.GetProperty("available_actions")[0].GetProperty("name").GetString());

        var guidance = root.GetProperty("scene_guidance");
        Assert.Equal("COMBAT", guidance.GetProperty("screen").GetString());
        Assert.Equal("combat", guidance.GetProperty("scene").GetString());
        Assert.Contains("Combat: what to prioritise", guidance.GetProperty("guidance").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(guidance.GetProperty("playbook").GetString()),
            "the playbook slice is never empty; an unmapped screen gets the index instead");
    }

    /// <summary>
    /// Both surfaces answer the same guidance keys, and the playbook half is the per-screen slice
    /// rather than the whole document.
    /// </summary>
    public static async Task ToolsCall_SceneGuidanceCarriesThePlaybookSlice()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        var read = await Rpc(server, """{"jsonrpc":"2.0","id":47,"method":"tools/call","params":{"name":"get_scene_guidance"}}""");
        using var document = JsonDocument.Parse(
            read.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var guidance = document.RootElement;

        foreach (var key in new[] { "screen", "scene", "guidance", "playbook" })
        {
            Assert.True(guidance.TryGetProperty(key, out _), "get_scene_guidance is missing " + key);
        }

        var playbook = guidance.GetProperty("playbook").GetString();
        Assert.Contains("## COMBAT", playbook);
        Assert.False(
            playbook!.Contains("## SHOP", StringComparison.Ordinal),
            "the playbook answer is the screen's slice, not the whole document");

        // An unmapped screen still gets an answer: the index of the sections it could read.
        bridge.CompactStateJson = """{"screen":"UNKNOWN","available_actions":[]}""";
        var unmapped = await Rpc(server, """{"jsonrpc":"2.0","id":48,"method":"tools/call","params":{"name":"get_scene_guidance"}}""");
        using var unmappedDocument = JsonDocument.Parse(
            unmapped.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var unmappedPlaybook = unmappedDocument.RootElement.GetProperty("playbook").GetString();
        Assert.Contains("No section of this playbook matches", unmappedPlaybook);
        Assert.Equal(string.Empty, unmappedDocument.RootElement.GetProperty("guidance").GetString());
    }

    /// <summary>
    /// An exception is not a message string: the tool error content carries the code, details, and
    /// retryable flag the shared play contract tells a client to branch on.
    /// </summary>
    public static async Task ToolsCall_ExceptionCarriesTheStructuredEnvelope()
    {
        var bridge = new FakeMcpBridge
        {
            ActFailure = new ApiException(
                503,
                "state_unavailable",
                "Local player is unavailable.",
                new { action = "play_card" },
                retryable: true)
        };
        var server = CreateServer(bridge: bridge);

        var failed = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":43,"method":"tools/call","params":{"name":"act","arguments":{"action":"play_card","card_index":0}}}""");

        Assert.True(failed.GetProperty("result").GetProperty("isError").GetBoolean());
        using var document = JsonDocument.Parse(
            failed.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("state_unavailable", error.GetProperty("code").GetString());
        Assert.Equal("Local player is unavailable.", error.GetProperty("message").GetString());
        Assert.True(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(503, error.GetProperty("status_code").GetInt32());
        Assert.Equal("play_card", error.GetProperty("details").GetProperty("action").GetString());
    }

    /// <summary>The two protocol-level refusals answer with the same envelope, not a bare string.</summary>
    public static async Task ToolsCall_ToolNameRefusalsAreStructured()
    {
        var server = CreateServer();

        var unnamed = await Rpc(server, """{"jsonrpc":"2.0","id":44,"method":"tools/call","params":{}}""");
        using var unnamedDocument = JsonDocument.Parse(
            unnamed.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal("invalid_request", unnamedDocument.RootElement.GetProperty("error").GetProperty("code").GetString());

        var unknown = await Rpc(server, """{"jsonrpc":"2.0","id":45,"method":"tools/call","params":{"name":"not_a_tool"}}""");
        using var unknownDocument = JsonDocument.Parse(
            unknown.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal("unknown_tool", unknownDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Unknown tool 'not_a_tool'.", unknownDocument.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    public static async Task ToolsCall_DiffStateComparesTwoPayloads()
    {
        var server = CreateServer();

        var diff = await Rpc(
            server,
            """{"jsonrpc":"2.0","id":25,"method":"tools/call","params":{"name":"diff_state","arguments":{"before":{"gold":212},"after":{"gold":180}}}}""");
        var text = diff.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var document = JsonDocument.Parse(text!);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("change_count").GetInt32());
        Assert.Equal("gold", root.GetProperty("changes")[0].GetProperty("path").GetString());
        Assert.Equal(212d, root.GetProperty("changes")[0].GetProperty("before").GetDouble());
        Assert.Equal(180d, root.GetProperty("changes")[0].GetProperty("after").GetDouble());
    }

    public static async Task ToolsCall_SceneGuidanceFollowsTheScreen()
    {
        var bridge = new FakeMcpBridge();
        var server = CreateServer(bridge: bridge);

        var combat = await Rpc(server, """{"jsonrpc":"2.0","id":26,"method":"tools/call","params":{"name":"get_scene_guidance"}}""");
        var combatText = combat.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using (var document = JsonDocument.Parse(combatText!))
        {
            var root = document.RootElement;
            Assert.Equal("COMBAT", root.GetProperty("screen").GetString());
            Assert.Equal("combat", root.GetProperty("scene").GetString());
            Assert.Contains("Combat: what to prioritise", root.GetProperty("guidance").GetString());
            Assert.False(
                root.GetProperty("guidance").GetString()!.Contains("Route: which node to enter", StringComparison.Ordinal),
                "a combat screen must not be sent the route rules");
        }

        // The screen comes from live state, so the same tool answers for a different screen.
        bridge.CompactStateJson = """{"screen":"REST","available_actions":["choose_rest_option"]}""";
        var rest = await Rpc(server, """{"jsonrpc":"2.0","id":27,"method":"tools/call","params":{"name":"get_scene_guidance"}}""");
        var restText = rest.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using (var document = JsonDocument.Parse(restText!))
        {
            Assert.Contains("Rest site: heal or upgrade", document.RootElement.GetProperty("guidance").GetString());
        }

        // A screen with no strategic choice answers with an empty string, not an error and not null.
        bridge.CompactStateJson = """{"screen":"REWARD","available_actions":["collect_rewards_and_proceed"]}""";
        var reward = await Rpc(server, """{"jsonrpc":"2.0","id":28,"method":"tools/call","params":{"name":"get_scene_guidance"}}""");
        var rewardText = reward.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using (var document = JsonDocument.Parse(rewardText!))
        {
            Assert.Equal(string.Empty, document.RootElement.GetProperty("guidance").GetString());
        }
    }

    public static async Task Notification_Returns202()
    {
        var server = CreateServer();
        var result = await server.ProcessAsync(
            "POST",
            "application/json",
            null,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            CancellationToken.None);
        Assert.Equal(202, result.StatusCode);
        Assert.True(string.IsNullOrEmpty(result.Body));
    }

    public static Task ClientConfig_UsesEnabledUrl()
    {
        var server = CreateServer();
        var json = server.BuildClientConfigJson();
        Assert.Contains("http://127.0.0.1:8080/mcp", json);
        Assert.Contains("\"type\": \"http\"", json);
        return Task.CompletedTask;
    }

    public static async Task MissingOrigin_AllowsNativeInitializeAndOptions()
    {
        var server = CreateServer();
        var options = await Process(server, "OPTIONS", body: null);
        Assert.Equal(204, options.StatusCode);
        Assert.Null(options.AllowOrigin);

        var initialized = await Process(server, "POST", InitializeBody);
        Assert.Equal(200, initialized.StatusCode);
        Assert.NotNull(initialized.SessionId);
        Assert.Null(initialized.AllowOrigin);
        Assert.False(string.IsNullOrWhiteSpace(initialized.Body));
    }

    public static async Task UntrustedOrigin_RejectedWithoutCreatingSession()
    {
        var server = CreateServer();
        var rejected = await Process(
            server,
            "POST",
            InitializeBody,
            origin: "https://evil.example",
            host: "127.0.0.1:8080",
            sessionHeader: "forged-session");
        Assert.Equal(403, rejected.StatusCode);
        Assert.Contains("origin_not_allowed", rejected.Body);
        Assert.Null(rejected.SessionId);
        Assert.Null(rejected.AllowOrigin);

        var options = await Process(server, "OPTIONS", body: null, origin: "https://evil.example", host: "127.0.0.1:8080");
        Assert.Equal(403, options.StatusCode);
        Assert.Contains("origin_not_allowed", options.Body);
        Assert.Null(options.SessionId);

        var native = await Process(server, "POST", InitializeBody);
        Assert.Equal(200, native.StatusCode);
        Assert.NotNull(native.SessionId);
    }

    public static async Task SameOriginHost_AllowsInitializeAndEchoesOrigin()
    {
        var server = CreateServer();
        var result = await Process(
            server,
            "POST",
            InitializeBody,
            origin: "http://127.0.0.1:8080",
            host: "127.0.0.1:8080");
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.SessionId);
        Assert.Equal("http://127.0.0.1:8080", result.AllowOrigin);
    }

    public static async Task NullLiteralOrigin_Rejected()
    {
        var server = CreateServer();
        var result = await Process(server, "POST", InitializeBody, origin: "null", host: "127.0.0.1:8080");
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("origin_not_allowed", result.Body);
        Assert.Null(result.SessionId);
    }

    public static async Task EvilOriginAndHost_RejectedEvenWhenTheyMatchEachOther()
    {
        var server = CreateServer();
        foreach (var (origin, host) in new[]
        {
            ("https://evil.example", "evil.example"),
            ("http://evil.example:8080", "evil.example:8080"),
            ("https://evil.example:443", "evil.example:443")
        })
        {
            var result = await Process(server, "POST", InitializeBody, origin, host);
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("origin_not_allowed", result.Body);
            Assert.Null(result.SessionId);
            Assert.Null(result.AllowOrigin);
        }
    }

    public static async Task MalformedOrigin_Rejected()
    {
        var server = CreateServer();
        foreach (var origin in new[]
        {
            "http://user:pass@127.0.0.1:8080",
            "http://127.0.0.1:8080/mcp",
            "http://127.0.0.1:8080/?x=1",
            "http://127.0.0.1:8080/#frag",
            "http://127.0.0.1:8080,https://evil.example",
            "http://127.0.0.1:8080 https://evil.example"
        })
        {
            var result = await Process(server, "POST", InitializeBody, origin, "127.0.0.1:8080");
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("origin_not_allowed", result.Body);
            Assert.Null(result.SessionId);
        }
    }

    public static async Task HandleHttp_OriginPolicyRejectsUntrustedAndAllowsSameOrigin()
    {
        var started = LoopbackListener.Start(49170, allowFallback: true);
        using var listener = started.Listener;
        var endpoint = $"http://127.0.0.1:{started.Port}/mcp";
        var server = CreateServer(endpointUrl: endpoint);
        var origin = $"http://127.0.0.1:{started.Port}";
        var url = $"{origin}/mcp";

        var untrusted = await RoundTrip(listener, server, url, HttpMethod.Post, InitializeBody, "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, untrusted.StatusCode);
        Assert.False(untrusted.HasWildcardCors, "untrusted Origin must not receive Access-Control-Allow-Origin: *");
        Assert.False(untrusted.HasHeader("Access-Control-Allow-Origin"));
        Assert.Contains("origin_not_allowed", untrusted.Body);
        Assert.False(untrusted.HasHeader("Mcp-Session-Id"));

        var preflight = await RoundTrip(listener, server, url, HttpMethod.Options, body: null, origin: "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, preflight.StatusCode);
        Assert.False(preflight.HasWildcardCors, "untrusted OPTIONS must not receive Access-Control-Allow-Origin: *");
        Assert.False(preflight.HasHeader("Access-Control-Allow-Origin"));

        var allowed = await RoundTrip(listener, server, url, HttpMethod.Post, InitializeBody, origin);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.False(allowed.HasWildcardCors, "allowed Origin must echo itself instead of *");
        Assert.Equal(origin, allowed.Get("Access-Control-Allow-Origin"));
        Assert.True(allowed.HasHeader("Mcp-Session-Id"));

        var native = await RoundTrip(listener, server, url, HttpMethod.Post, InitializeBody, origin: null);
        Assert.Equal(HttpStatusCode.OK, native.StatusCode);
        Assert.False(native.HasWildcardCors, "native clients without Origin must not receive Access-Control-Allow-Origin: *");
        Assert.False(native.HasHeader("Access-Control-Allow-Origin"));
    }

    private static NativeMcpServer CreateServer(
        bool enabled = true,
        FakeMcpBridge? bridge = null,
        string endpointUrl = "http://127.0.0.1:8080/mcp",
        DecisionLog? decisions = null)
    {
        var server = new NativeMcpServer(
            bridge ?? new FakeMcpBridge(),
            () => new { status = "ready", service = "sts2-ai-agent" },
            "9.9.9",
            decisions);
        if (enabled)
        {
            server.SetEnabled(true, endpointUrl);
        }

        return server;
    }

    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private static Task<McpHttpResult> Process(
        NativeMcpServer server,
        string method,
        string? body,
        string? origin = null,
        string? host = null,
        string? sessionHeader = null)
    {
        return server.ProcessAsync(method, "application/json", sessionHeader, body, CancellationToken.None, origin, host);
    }

    private static async Task<HttpProbe> RoundTrip(
        HttpListener listener,
        NativeMcpServer server,
        string url,
        HttpMethod method,
        string? body,
        string? origin)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(method, url);
        if (origin != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var incoming = listener.GetContextAsync();
        var send = http.SendAsync(request);
        var context = await incoming;
        try
        {
            await server.HandleHttpAsync(context, CancellationToken.None);
        }
        finally
        {
            context.Response.Close();
        }

        using var response = await send;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
        }

        return new HttpProbe
        {
            StatusCode = response.StatusCode,
            Body = await response.Content.ReadAsStringAsync(),
            Headers = headers
        };
    }


    private sealed class HttpProbe
    {
        public HttpStatusCode StatusCode { get; init; }

        public string Body { get; init; } = string.Empty;

        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasWildcardCors =>
            Headers.TryGetValue("Access-Control-Allow-Origin", out var value) && value.Trim() == "*";

        public bool HasHeader(string name) => Headers.ContainsKey(name);

        public string? Get(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private static async Task<JsonElement> Rpc(NativeMcpServer server, string body)
    {
        var result = await server.ProcessAsync("POST", "application/json", null, body, CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
        return JsonDocument.Parse(result.Body!).RootElement.Clone();
    }

    private sealed class FakeMcpBridge : IGameBridge
    {
        public int ActCalls { get; private set; }

        public int RawStateCalls { get; private set; }

        public int SnapshotCalls { get; private set; }

        public string RawStateJson { get; set; } = """{"screen":"COMBAT","raw":true}""";

        public string? LastAction { get; private set; }

        public int? LastCardIndex { get; private set; }

        public bool LastRawState { get; private set; }

        /// <summary>When set, <see cref="ActAsync"/> throws it instead of answering.</summary>
        public Exception? ActFailure { get; init; }

        public string CompactStateJson { get; set; } =
            """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]}],"enemies":[{"i":0}]}}""";

        public string AvailableActionsJson { get; set; } =
            """[{"name":"play_card","requires_index":true,"requires_target":false}]""";

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(CompactStateJson);

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken)
        {
            RawStateCalls++;
            return Task.FromResult(RawStateJson);
        }

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => Task.FromResult(AvailableActionsJson);

        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken)
        {
            SnapshotCalls++;
            // The same two halves the real bridge builds from one state read.
            using var state = JsonDocument.Parse(CompactStateJson);
            using var actions = JsonDocument.Parse(AvailableActionsJson);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                state = state.RootElement.Clone(),
                available_actions = actions.RootElement.Clone()
            }));
        }

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");

        public Task<string> ActAsync(
            string action,
            int? cardIndex,
            int? targetIndex,
            int? optionIndex,
            int? x,
            int? y,
            string? tool,
            CancellationToken cancellationToken,
            bool rawState = false)
        {
            ActCalls++;
            LastAction = action;
            LastCardIndex = cardIndex;
            LastRawState = rawState;
            if (ActFailure != null)
            {
                throw ActFailure;
            }

            return Task.FromResult("""{"action":"play_card","status":"completed","stable":true}""");
        }

        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken)
            => Task.FromResult("""{"id":"STRIKE"}""");

        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
            => Task.FromResult("""{"STRIKE":{"id":"STRIKE"}}""");

        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
            => Task.FromResult("""{"STRIKE":{"id":"STRIKE","name":"Strike"}}""");

        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }
}
