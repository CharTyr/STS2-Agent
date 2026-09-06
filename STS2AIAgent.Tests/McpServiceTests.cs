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

    private static NativeMcpServer CreateServer(bool enabled = true, FakeMcpBridge? bridge = null)
    {
        var server = new NativeMcpServer(
            bridge ?? new FakeMcpBridge(),
            () => new { status = "ready", service = "sts2-ai-agent" },
            "9.9.9");
        if (enabled)
        {
            server.SetEnabled(true, "http://127.0.0.1:8080/mcp");
        }

        return server;
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
