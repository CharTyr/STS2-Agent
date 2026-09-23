using System.Net;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class AuditRemediationJevTests
{
    private const string Frame = """{"state":{"screen":"COMBAT","available_actions":["end_turn"]},"available_actions":[{"name":"end_turn"}]}""";

    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("Remediation.Jev429OneRetryAndScore", OneRetryAndScore);
        yield return ("Remediation.Jev429BudgetBlocksRetry", BudgetBlocksRetry);
        yield return ("Remediation.Jev429NeverRetriesTwice", NeverRetriesTwice);
        yield return ("Remediation.Jev429CancellationReceipt", CanceledBackoffRetainsAttempt);
        yield return ("Remediation.Jev429SharedDeadline", SharedDeadline);
        yield return ("Remediation.Jev429RetryHintAndErrors", RetryHintAndErrors);
        yield return ("Remediation.PlannerPeriodicAndSameRunContext", PlannerPeriodicAndSameRunContext);
    }

    private sealed class ScriptedJev : IJevClient
    {
        public int Calls { get; private set; }
        public JevRequest? Request { get; private set; }
        public Func<int, CancellationToken, Task<JevResponse>> Reply { get; init; } = (_, _) => Task.FromResult(Answer());
        public Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken token)
        {
            Request = request;
            return Reply(++Calls, token);
        }
        public Task<string> PingAsync(CancellationToken token) => Task.FromResult("OK");
    }

    private static JevResponse Answer() => new()
    {
        Answers = new Dictionary<string, JevAnswer>
        {
            ["action"] = new JevAnswer { Type = "choice", Choice = "end_turn", Confidence = 0.9 },
            ["danger"] = new JevAnswer { Type = "score", Score = 1.82 }
        },
        Usage = new JevUsage { InputTokens = 3, OutputTokens = 2 }
    };

    private static async Task OneRetryAndScore()
    {
        var seen = new List<int>();
        var waits = new List<TimeSpan>();
        var client = new ScriptedJev
        {
            Reply = (count, _) => count == 1
                ? throw new JevException("rate limit", JevExceptionKind.RateLimited, 429, 999)
                : Task.FromResult(Answer())
        };
        var result = await new JevExecutionDecider(client, "jev", allowAttempt: previous =>
        {
            seen.Add(previous);
            return true;
        }, delay: (duration, _) => { waits.Add(duration); return Task.CompletedTask; })
            .DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        Assert.Equal(2, client.Calls);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Equal("end_turn", result.Action);
        Assert.Equal(5, result.Usage!.TotalTokens);
        Assert.Equal(1.82, result.DangerScore!.Value);
        Assert.NotNull(result.JevElapsedMilliseconds);
        Assert.Equal("score", client.Request!.Questions["danger"].Type);
        Assert.Equal(0, seen[0]);
        Assert.Equal(1, seen[1]);
        Assert.Equal(TimeSpan.FromSeconds(30), waits.Single());
    }

    private static async Task BudgetBlocksRetry()
    {
        var client = new ScriptedJev
        {
            Reply = (_, _) => throw new JevException("limited", JevExceptionKind.RateLimited, 429, 0)
        };
        var guard = new SessionBudgetGuard(maxRequests: 2, initialRequests: 1);
        var result = await new JevExecutionDecider(client, "jev", allowAttempt: previous =>
            guard.CheckBudget(extraRequests: previous) == null,
            delay: (_, _) => Task.CompletedTask).DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Contains("budget", result.Error);
    }

    private static async Task NeverRetriesTwice()
    {
        var client = new ScriptedJev
        {
            Reply = (_, _) => throw new JevException("limited", JevExceptionKind.RateLimited, 429, 0)
        };
        var result = await new JevExecutionDecider(client, "jev", delay: (_, _) => Task.CompletedTask)
            .DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        Assert.Equal(2, client.Calls);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Contains("RateLimited", result.Error);
    }

    private static async Task CanceledBackoffRetainsAttempt()
    {
        using var cancel = new CancellationTokenSource();
        var client = new ScriptedJev
        {
            Reply = (_, _) => throw new JevException("limited", JevExceptionKind.RateLimited, 429, 1)
        };
        var decider = new JevExecutionDecider(client, "jev", delay: (_, token) =>
        {
            cancel.Cancel();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        try
        {
            await decider.DecideAsync(Frame, PlayStrategy.Default, cancel.Token);
            throw new Exception("Expected cancellation");
        }
        catch (AgentTurnCanceledException ex)
        {
            Assert.Equal(1, client.Calls);
            Assert.Equal(1, ex.Receipt.RequestsSpent);
            Assert.NotNull(ex.Receipt.JevElapsedMilliseconds);
        }
    }

    private static async Task SharedDeadline()
    {
        var client = new ScriptedJev
        {
            Reply = (attempt, token) => attempt == 1
                ? throw new JevException("limited", JevExceptionKind.RateLimited, 429, 0)
                : WaitForever(token)
        };
        var result = await new JevExecutionDecider(client, "jev",
            turnTimeout: TimeSpan.FromMilliseconds(40), delay: (_, _) => Task.CompletedTask)
            .DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, client.Calls);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Contains("timed out", result.Error);
    }

    private static async Task<JevResponse> WaitForever(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new Exception("unreachable");
    }

    private sealed class StatusHandler(HttpStatusCode status, string? retryAfter = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("failure") };
            if (retryAfter != null) response.Headers.TryAddWithoutValidation("retry-after", retryAfter);
            return Task.FromResult(response);
        }
    }

    private static async Task RetryHintAndErrors()
    {
        foreach (var (status, kind) in new[]
        {
            (HttpStatusCode.BadRequest, JevExceptionKind.Config),
            (HttpStatusCode.RequestTimeout, JevExceptionKind.Network),
            (HttpStatusCode.InternalServerError, JevExceptionKind.Network)
        })
        {
            using var http = new HttpClient(new StatusHandler(status));
            try
            {
                await new JevClient("https://example.test", "test", "jev", http, maxRetries: 0)
                    .SystemOneAsync(new JevRequest { Model = "jev" }, CancellationToken.None);
                throw new Exception("Expected HTTP error");
            }
            catch (JevException ex) { Assert.Equal(kind, ex.Kind); }
        }

        using var rateHttp = new HttpClient(new StatusHandler(HttpStatusCode.TooManyRequests, "999"));
        try
        {
            await new JevClient("https://example.test", "test", "jev", rateHttp, maxRetries: 0)
                .SystemOneAsync(new JevRequest { Model = "jev" }, CancellationToken.None);
            throw new Exception("Expected 429");
        }
        catch (JevException ex)
        {
            Assert.Equal(JevExceptionKind.RateLimited, ex.Kind);
            Assert.Equal<int?>(30, ex.RetryAfterSeconds);
        }
    }

    private sealed class CapturingModel : ILlmClientFactory, ILlmClient
    {
        public string? Context { get; private set; }
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken token)
        {
            Context = string.Join("\n", request.Messages.Select(message => message.Content));
            return Task.FromResult(new LlmCompletion { Content = "{\"instructions\":\"new plan\"}" });
        }
        public Task<string> PingAsync(string model, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken token) => Task.FromResult(true);
    }

    private static async Task PlannerPeriodicAndSameRunContext()
    {
        var model = new CapturingModel();
        var store = new StrategyStore();
        store.Update(new PlayStrategy { Instructions = "hold potions", Source = "mcp" });
        var planner = new StrategyPlanner(model, AgentSettings.CreateDefault, store);
        Assert.True(planner.ShouldRefresh("run|COMBAT|1", 0.9, 0.35));
        for (var i = 1; i < StrategyPlanner.PeriodicRefreshTurns; i++)
            Assert.False(planner.ShouldRefresh("run|COMBAT|1", 0.9, 0.35));
        Assert.True(planner.ShouldRefresh("run|COMBAT|1", 0.9, 0.35));
        var entries = new[]
        {
            new DecisionLogEntry(1, "now", "jev", "end_turn", "defend", null, 1, null, "run-a"),
            new DecisionLogEntry(2, "old", "jev", "buy_card", "wrong run", null, 1, null, "run-b")
        };
        await planner.RefreshAsync("hp 9", CancellationToken.None, recentDecisions: entries, runId: "run-a");
        Assert.Contains("hold potions", model.Context);
        Assert.Contains("end_turn", model.Context);
        Assert.False(model.Context!.Contains("buy_card", StringComparison.Ordinal));
    }
}
