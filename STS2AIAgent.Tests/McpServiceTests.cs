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

    private static NativeMcpServer CreateServer(bool enabled = true, FakeMcpBridge? bridge = null, string endpointUrl = "http://127.0.0.1:8080/mcp")
    {
        var server = new NativeMcpServer(
            bridge ?? new FakeMcpBridge(),
            () => new { status = "ready", service = "sts2-ai-agent" },
            "9.9.9");
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

        public string? LastAction { get; private set; }

        public int? LastCardIndex { get; private set; }

        public string CompactStateJson { get; set; } =
            """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]}],"enemies":[{"i":0}]}}""";

        public string AvailableActionsJson { get; set; } =
            """[{"name":"play_card","requires_index":true,"requires_target":false}]""";

        public IReadOnlyList<string> AvailableActionNames { get; set; } =
            new[] { "play_card", "end_turn" };

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(CompactStateJson);

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult("""{"screen":"COMBAT","raw":true}""");

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => Task.FromResult(AvailableActionsJson);

        public Task<IReadOnlyList<string>> GetAvailableActionNamesAsync(CancellationToken cancellationToken) => Task.FromResult(AvailableActionNames);

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");

        public Task<string> ActAsync(
            string action,
            int? cardIndex,
            int? targetIndex,
            int? optionIndex,
            int? x,
            int? y,
            string? tool,
            CancellationToken cancellationToken)
        {
            ActCalls++;
            LastAction = action;
            LastCardIndex = cardIndex;
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
