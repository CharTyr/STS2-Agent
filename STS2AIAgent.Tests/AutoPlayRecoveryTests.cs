using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class AutoPlayRecoveryTests
{
    public static async Task HttpFailuresKeepStatusWithoutStreamReplay()
    {
        foreach (var status in new[] { 401, 403, 404, 408, 429, 500, 503 })
        {
            var handler = new FailureHandler(status);
            using var http = new HttpClient(handler);
            var client = new OpenAiCompatibleClient(new LlmEndpoint { BaseUrl = "https://example.test/v1" }, httpClient: http);
            try
            {
                await client.CompleteAsync(new LlmRequest
                {
                    Model = "test", Messages = new[] { LlmMessage.User("test") }, Stream = true
                }, CancellationToken.None);
                throw new Exception("Expected HTTP failure");
            }
            catch (LlmException ex) { Assert.Equal<int?>(status, ex.StatusCode); }
            Assert.Equal(1, handler.Calls);
        }
    }

    private sealed class FailureHandler(int status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status)
            {
                Content = new StringContent("stream request failed")
            });
        }
    }

    public static async Task RepeatedNoActionStops()
    {
        var calls = 0;
        var delays = new List<double>();
        try
        {
            await AutoPlayRecovery.RunAsync(_ =>
            {
                calls++;
                return Task.FromResult(new AgentTurnResult());
            }, _ => { }, CancellationToken.None, (duration, _) =>
            {
                delays.Add(duration.TotalSeconds);
                return Task.CompletedTask;
            });
            throw new Exception("Expected recovery stop");
        }
        catch (AutoPlayStoppedException) { }
        Assert.Equal(3, calls);
        Assert.Equal("2,4", string.Join(",", delays));
    }

    public static Task WaitingDoesNotHideFailures()
    {
        var policy = new AutoPlayRecovery();
        var failed = new AgentTurnResult { Error = "network" };
        Assert.Null(policy.Observe(failed).StopReason);
        for (var i = 0; i < 100; i++)
            Assert.Null(policy.Observe(new AgentTurnResult { WaitingForGame = true }).StopReason);
        Assert.Null(policy.Observe(failed).StopReason);
        Assert.NotNull(policy.Observe(failed).StopReason);
        return Task.CompletedTask;
    }

    public static Task CompanionMapWaitDoesNotStopAutoPlay()
    {
        var policy = new AutoPlayRecovery();
        var wait = new AgentTurnResult
        {
            Reasoning = "等待你选择地图节点，随后投同一格。",
            WaitingForGame = true,
            ToolRounds = 0,
            RequestsSpent = 0
        };
        for (var i = 0; i < 8; i++)
        {
            Assert.Null(policy.Observe(wait).StopReason);
        }

        return Task.CompletedTask;
    }

    public static Task SuccessfulActionResetsFailures()
    {
        var policy = new AutoPlayRecovery();
        policy.Observe(new AgentTurnResult());
        policy.Observe(new AgentTurnResult());
        policy.Observe(new AgentTurnResult { Acted = "play_card" });
        Assert.Null(policy.Observe(new AgentTurnResult()).StopReason);
        Assert.NotNull(policy.Observe(new AgentTurnResult { RequiresConfiguration = true, Error = "HTTP 401" }).StopReason);
        return Task.CompletedTask;
    }

    public static async Task CancelDuringBackoffPreventsNextTurn()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(_ =>
            {
                calls++;
                throw new HttpRequestException("offline");
            }, _ => { }, cancellation.Token, (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Assert.Equal(1, calls);
    }
}
