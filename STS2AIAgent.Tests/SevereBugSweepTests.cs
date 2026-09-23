using System.Diagnostics;
using System.Net;
using System.Text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// Regressions for the 2026-09-23 severe-bug sweep: the offline-checkable halves of fixes whose other
/// half runs inside the game (the host watchdog, provider-compatibility parsing, reasoning round-trip,
/// Jev option availability).
/// </summary>
internal static class SevereBugSweepTests
{
    // ---- Companion host watchdog --------------------------------------------------------------

    public static void HostWatchReadsPidAndDetectsAbsence()
    {
        Assert.True(CompanionHostWatch.TryReadHostPid(" 1234 ", out var pid));
        Assert.Equal(1234, pid);
        Assert.False(CompanionHostWatch.TryReadHostPid(null, out _));
        Assert.False(CompanionHostWatch.TryReadHostPid("", out _));
        Assert.False(CompanionHostWatch.TryReadHostPid("-5", out _));
        Assert.False(CompanionHostWatch.TryReadHostPid("abc", out _));

        // The live test process is a host that is still here.
        var self = Environment.ProcessId;
        var start = CompanionHostWatch.TryReadStartTime(self);
        Assert.NotNull(start);
        Assert.False(CompanionHostWatch.IsHostGone(self, start), "A running host must not be reported gone.");

        // A recycled PID (same id, different start time) is not our host.
        Assert.True(CompanionHostWatch.IsDifferentProcess(start, start!.Value.AddMinutes(5)));
        Assert.False(CompanionHostWatch.IsDifferentProcess(start, start.Value));
        Assert.False(CompanionHostWatch.IsDifferentProcess(null, DateTime.Now), "Without a recorded start, trust the id.");
    }

    public static void HostWatchReportsAnExitedHostGone()
    {
        // A process that has certainly exited: start a trivial child and wait for it.
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        })!;
        var childPid = child.Id;
        var childStart = child.StartTime;
        child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        Assert.True(CompanionHostWatch.IsHostGone(childPid, childStart), "An exited host must be reported gone.");
    }

    public static void TheLauncherHandsTheHostPidToTheCompanion()
    {
        var launcher = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs");
        Assert.Contains("startInfo.Environment[CompanionHostWatch.EnvironmentName]", launcher, StringComparison.Ordinal);
        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        Assert.Contains("WatchHostAsync(_lifetime.Token)", runtime, StringComparison.Ordinal);
        Assert.Contains("LocalDualInstanceLauncher.TryCloseCompanion()", runtime, StringComparison.Ordinal);
    }

    // ---- OpenAI-compatible parsing -----------------------------------------------------------

    public static void SseWithLeadingCommentIsStillSse()
    {
        const string payload = ": OPENROUTER PROCESSING\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"hi\"},\"index\":0}]}\n\ndata: [DONE]\n";
        Assert.True(OpenAiCompatibleClient.LooksLikeSse(payload), "A leading SSE comment must not hide the stream.");
        Assert.Equal("hi", OpenAiCompatibleClient.ParseSsePayload(payload).Content);
        Assert.True(OpenAiCompatibleClient.LooksLikeSse("event: message\ndata: {}\n"));
        Assert.False(OpenAiCompatibleClient.LooksLikeSse("{\"choices\":[]}"));
        Assert.False(OpenAiCompatibleClient.LooksLikeSse("   \n  "));
    }

    public static void LooseToolCallShapesStillParse()
    {
        const string payload = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
          {"id":12345,"type":"function","function":{"name":"act","arguments":{"action":"end_turn"}}},
          {"type":"function","function":{"name":"act","arguments":"{\"action\":\"proceed\"}"}}
        ]},"finish_reason":"tool_calls"}]}
        """;
        var completion = OpenAiCompatibleClient.ParseCompletion(payload);
        Assert.Equal(2, completion.ToolCalls.Count);
        Assert.Equal("12345", completion.ToolCalls[0].Id);
        Assert.Contains("end_turn", completion.ToolCalls[0].ArgumentsJson, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(completion.ToolCalls[1].Id), "A call without an id must get one, not vanish.");
        Assert.Contains("proceed", completion.ToolCalls[1].ArgumentsJson, StringComparison.Ordinal);
    }

    public static void StreamedToolCallKeepsItsIdAndSurvivesWithoutOne()
    {
        const string withLaterEmptyId =
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"act\",\"arguments\":\"{\\\"action\\\":\"}}]},\"index\":0}]}\n\n"
            + "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"\",\"function\":{\"arguments\":\"\\\"end_turn\\\"}\"}}]},\"index\":0}]}\n\n"
            + "data: [DONE]\n";
        var first = OpenAiCompatibleClient.ParseSsePayload(withLaterEmptyId);
        Assert.Equal(1, first.ToolCalls.Count);
        Assert.Equal("call_a", first.ToolCalls[0].Id);
        Assert.Contains("end_turn", first.ToolCalls[0].ArgumentsJson, StringComparison.Ordinal);

        const string withoutId =
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"act\",\"arguments\":{\"action\":\"end_turn\"}}}]},\"index\":0}]}\n\n"
            + "data: [DONE]\n";
        var second = OpenAiCompatibleClient.ParseSsePayload(withoutId);
        Assert.Equal(1, second.ToolCalls.Count);
        Assert.False(string.IsNullOrWhiteSpace(second.ToolCalls[0].Id));
        Assert.Contains("end_turn", second.ToolCalls[0].ArgumentsJson, StringComparison.Ordinal);
    }

    public static async Task ReasoningIsEchoedOnlyOnToolCallTurns()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleClient(new LlmEndpoint { BaseUrl = "https://example.test/v1" }, httpClient: http);
        var call = new LlmToolCall { Id = "call_1", Name = "act", ArgumentsJson = "{}" };
        await client.CompleteAsync(new LlmRequest
        {
            Model = "deepseek",
            Stream = false,
            Messages = new[]
            {
                LlmMessage.User("play"),
                LlmMessage.Assistant(null, new[] { call }, "I should end the turn."),
                LlmMessage.Tool("call_1", "{\"status\":\"completed\"}"),
                LlmMessage.Assistant("done", null, "never echoed without tool calls")
            }
        }, CancellationToken.None);

        var body = handler.LastBody ?? "";
        Assert.Contains("\"reasoning_content\":\"I should end the turn.\"", body, StringComparison.Ordinal);
        Assert.False(body.Contains("never echoed", StringComparison.Ordinal),
            "reasoning_content belongs only to assistant turns that called tools.");
    }

    public static void TheLoopCarriesReasoningIntoTheToolRound()
    {
        var loop = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentLoop.cs");
        Assert.Contains("LlmMessage.Assistant(completion.Content, completion.ToolCalls, completion.Reasoning)", loop, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    // ---- Jev option availability ---------------------------------------------------------------

    public static void JevSkipsOptionsTheExecutorWouldReject()
    {
        const string snapshot = """
        {
          "state": {
            "screen": "REST",
            "rest": {"options": [
              {"i": 0, "line": "Rest (heal)", "enabled": false},
              {"i": 1, "line": "Smith", "enabled": true},
              {"i": 2, "line": "Lift"}
            ]},
            "reward": {"rewards": [
              {"i": 0, "line": "Gold: 20", "claimable": false},
              {"i": 1, "line": "Potion", "claimable": true}
            ]},
            "shop": {"relics": [
              {"i": 0, "line": "Anchor", "affordable": false, "stocked": true},
              {"i": 1, "line": "Lantern", "affordable": true, "stocked": false},
              {"i": 2, "line": "Vajra", "affordable": true, "stocked": true}
            ]}
          },
          "available_actions": [
            {"name": "choose_rest_option", "requires_index": true},
            {"name": "claim_reward", "requires_index": true},
            {"name": "buy_relic", "requires_index": true}
          ]
        }
        """;
        var ids = JevOptionEnumerator.Enumerate(snapshot).Select(option => option.Id).ToList();
        Assert.False(ids.Contains("choose_rest_option:0"), "A disabled rest option must not be offered.");
        Assert.True(ids.Contains("choose_rest_option:1"));
        Assert.True(ids.Contains("choose_rest_option:2"), "An absent flag keeps the option.");
        Assert.False(ids.Contains("claim_reward:0"), "A claimed reward must not be offered.");
        Assert.True(ids.Contains("claim_reward:1"));
        Assert.False(ids.Contains("buy_relic:0"), "An unaffordable relic must not be offered.");
        Assert.False(ids.Contains("buy_relic:1"), "A sold-out relic must not be offered.");
        Assert.True(ids.Contains("buy_relic:2"));
    }
}
