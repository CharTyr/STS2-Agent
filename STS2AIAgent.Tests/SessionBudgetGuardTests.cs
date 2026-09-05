namespace STS2AIAgent.Tests;

using STS2AIAgent.Agent;
using STS2AIAgent.Llm;

internal static class SessionBudgetGuardTests
{
    public static void NoLimit_NeverStops()
    {
        var guard = new SessionBudgetGuard();
        Assert.False(guard.HasLimit);

        var stop1 = guard.Record(5000, 10);
        Assert.Null(stop1);
        Assert.Equal(5000, guard.ConsumedTokens);
        Assert.Equal(10, guard.RequestCount);

        var result = new AgentTurnResult
        {
            Acted = "play_card",
            Usage = new LlmUsage { PromptTokens = 100, CompletionTokens = 20, TotalTokens = 120 },
            RequestsSpent = 1
        };
        var stop2 = guard.Observe(result);
        Assert.Null(stop2);
        Assert.Equal(5120, guard.ConsumedTokens);
        Assert.Equal(11, guard.RequestCount);
    }

    public static void MaxTokens_StopsWhenExceeded()
    {
        var guard = new SessionBudgetGuard(maxTokens: 1000);
        Assert.True(guard.HasLimit);

        var res1 = new AgentTurnResult
        {
            Acted = "play_card",
            Usage = new LlmUsage { TotalTokens = 800 },
            RequestsSpent = 1
        };
        Assert.Null(guard.Observe(res1));

        var res2 = new AgentTurnResult
        {
            Acted = "end_turn",
            Usage = new LlmUsage { TotalTokens = 250 },
            RequestsSpent = 1
        };
        var stop = guard.Observe(res2);
        Assert.NotNull(stop);
        Assert.Contains("Token 预算上限", stop);
        Assert.Contains("1,050/1,000", stop);
    }

    public static void CheckBudget_CountsInFlightRequests()
    {
        var guard = new SessionBudgetGuard(maxRequests: 2);
        Assert.Null(guard.CheckBudget());
        Assert.Null(guard.CheckBudget(extraRequests: 1));
        Assert.NotNull(guard.CheckBudget(extraRequests: 2));
        guard.Record(0, 1);
        Assert.NotNull(guard.CheckBudget(extraRequests: 1));
    }

    public static void MaxRequests_StopsEvenWithoutUsage()
    {
        var guard = new SessionBudgetGuard(maxRequests: 3);
        Assert.True(guard.HasLimit);

        var res1 = new AgentTurnResult { Acted = "choose_map_node", RequestsSpent = 1 };
        Assert.Null(guard.Observe(res1));

        var res2 = new AgentTurnResult { Acted = "play_card", RequestsSpent = 1 };
        Assert.Null(guard.Observe(res2));

        var res3 = new AgentTurnResult { Acted = "end_turn", RequestsSpent = 1 };
        var stop = guard.Observe(res3);
        Assert.NotNull(stop);
        Assert.Contains("请求次数上限", stop);
        Assert.Contains("3/3", stop);
    }

    public static async Task Recovery_AutoPlayStopsOnBudgetExceeded()
    {
        var guard = new SessionBudgetGuard(maxRequests: 2);
        var calls = 0;
        var reported = new List<AgentTurnResult>();

        try
        {
            await AutoPlayRecovery.RunAsync(
                turn: token =>
                {
                    calls++;
                    return Task.FromResult(new AgentTurnResult
                    {
                        Acted = "play_card",
                        RequestsSpent = 1,
                        Usage = new LlmUsage { TotalTokens = 50 }
                    });
                },
                report: res => reported.Add(res),
                cancellationToken: CancellationToken.None,
                delay: (ts, token) => Task.CompletedTask,
                budgetGuard: guard);

            Assert.True(false, "Expected AutoPlayStoppedException was not thrown.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Contains("请求次数上限", ex.Message);
            Assert.Equal(2, calls);
            Assert.Equal(2, reported.Count);
        }
    }

    public static void InitialCounters_ResumePreservesCumulativeUsage()
    {
        var guard = new SessionBudgetGuard(maxTokens: 1000, maxRequests: 3, initialTokens: 600, initialRequests: 2);
        Assert.Equal(600, guard.ConsumedTokens);
        Assert.Equal(2, guard.RequestCount);
        Assert.Null(guard.CheckBudget());

        var res = new AgentTurnResult
        {
            Acted = "play_card",
            Usage = new LlmUsage { TotalTokens = 500 },
            RequestsSpent = 1
        };
        var stop = guard.Observe(res);
        Assert.NotNull(stop);
        Assert.Equal(1100, guard.ConsumedTokens);
        Assert.Equal(3, guard.RequestCount);
    }

    public static async Task InitialCounters_AlreadyExceeded_RunAsyncStopsImmediately()
    {
        var guard = new SessionBudgetGuard(maxRequests: 2, initialRequests: 2);
        var calls = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(
                turn: token =>
                {
                    calls++;
                    return Task.FromResult(new AgentTurnResult { Acted = "play_card", RequestsSpent = 1 });
                },
                report: _ => { },
                cancellationToken: CancellationToken.None,
                delay: (ts, token) => Task.CompletedTask,
                budgetGuard: guard);

            Assert.True(false, "Expected AutoPlayStoppedException was not thrown.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Contains("请求次数上限", ex.Message);
            Assert.Equal(0, calls);
        }
    }

    public static async Task InitialTokens_AlreadyExceeded_RunAsyncStopsImmediately()
    {
        var guard = new SessionBudgetGuard(maxTokens: 500, initialTokens: 600);
        var calls = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(
                turn: token =>
                {
                    calls++;
                    return Task.FromResult(new AgentTurnResult { Acted = "play_card", RequestsSpent = 1 });
                },
                report: _ => { },
                cancellationToken: CancellationToken.None,
                delay: (ts, token) => Task.CompletedTask,
                budgetGuard: guard);

            Assert.True(false, "Expected AutoPlayStoppedException was not thrown.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Contains("Token 预算上限", ex.Message);
            Assert.Equal(0, calls);
        }
    }

    public static void Settings_CreateBudgetGuard_CarriesInitialCounters()
    {
        var settings = new Config.AgentSettings
        {
            MaxSessionTokens = 2000,
            MaxSessionRequests = 5
        };
        var guard = settings.CreateBudgetGuard(initialTokens: 350, initialRequests: 2);
        Assert.Equal(2000, guard.MaxTokens);
        Assert.Equal(5, guard.MaxRequests);
        Assert.Equal(350, guard.ConsumedTokens);
        Assert.Equal(2, guard.RequestCount);
    }
}
