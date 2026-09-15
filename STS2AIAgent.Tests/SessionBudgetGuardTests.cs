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

    public static void UpdateLimits_LoweringMaxRequests_CheckBudgetFailsWhileTotalsStay()
    {
        var guard = new SessionBudgetGuard(maxRequests: 5, initialRequests: 3);
        Assert.Null(guard.CheckBudget());
        guard.Record(40, 1);
        Assert.Equal(4, guard.RequestCount);
        Assert.Equal(40, guard.ConsumedTokens);
        Assert.Null(guard.CheckBudget());

        guard.UpdateLimits(maxTokens: null, maxRequests: 4);
        Assert.Equal(4, guard.RequestCount);
        Assert.Equal(40, guard.ConsumedTokens);
        Assert.Equal(4, guard.MaxRequests);
        var stop = guard.CheckBudget();
        Assert.NotNull(stop);
        Assert.Contains("请求次数上限", stop);
    }

    public static void UpdateLimits_RaisingMaxRequests_AllowsFurtherRecordWithoutStaleStop()
    {
        var guard = new SessionBudgetGuard(maxRequests: 2, initialRequests: 2);
        var stop = guard.CheckBudget();
        Assert.NotNull(stop);
        Assert.Equal(2, guard.RequestCount);

        guard.UpdateLimits(maxTokens: null, maxRequests: 5);
        Assert.Equal(2, guard.RequestCount);
        Assert.Null(guard.CheckBudget());

        Assert.Null(guard.Record(10, 1));
        Assert.Equal(3, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);

        var result = new AgentTurnResult
        {
            Acted = "play_card",
            Usage = new LlmUsage { TotalTokens = 5 },
            RequestsSpent = 1
        };
        Assert.Null(guard.Observe(result));
        Assert.Equal(4, guard.RequestCount);
        Assert.Equal(15, guard.ConsumedTokens);
        Assert.Null(guard.CheckBudget());
    }

    public static void UpdateLimits_ClearingLimit_RemovesHasLimitForThatAxis()
    {
        var guard = new SessionBudgetGuard(maxTokens: 1000, maxRequests: 3, initialTokens: 200, initialRequests: 2);
        Assert.True(guard.HasLimit);
        Assert.Equal(1000, guard.MaxTokens);
        Assert.Equal(3, guard.MaxRequests);

        guard.UpdateLimits(maxTokens: 0, maxRequests: 3);
        Assert.Null(guard.MaxTokens);
        Assert.Equal(3, guard.MaxRequests);
        Assert.True(guard.HasLimit);
        Assert.Equal(200, guard.ConsumedTokens);
        Assert.Equal(2, guard.RequestCount);

        guard.UpdateLimits(maxTokens: null, maxRequests: -1);
        Assert.Null(guard.MaxTokens);
        Assert.Null(guard.MaxRequests);
        Assert.False(guard.HasLimit);
        Assert.Equal(200, guard.ConsumedTokens);
        Assert.Equal(2, guard.RequestCount);
        Assert.Null(guard.CheckBudget());
    }

    public static void UpdateLimits_LockSafeWithConcurrentRecord()
    {
        var guard = new SessionBudgetGuard(maxRequests: 10_000);
        const int n = 200;
        Parallel.For(0, n, _ =>
        {
            guard.Record(1, 1);
            guard.UpdateLimits(maxTokens: 50_000, maxRequests: 10_000);
            guard.CheckBudget();
        });
        Assert.Equal(n, guard.ConsumedTokens);
        Assert.Equal(n, guard.RequestCount);
        Assert.Equal(50_000, guard.MaxTokens);
        Assert.Equal(10_000, guard.MaxRequests);
        Assert.Null(guard.CheckBudget());
    }

    public static void Runtime_SaveAndReload_UpdateLimitsInPlaceWithoutReplacingGuard()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        var save = AgentSourceFixture.DeclarationBody(source, "public void SaveSettings(AgentSettings settings)");
        var reload = AgentSourceFixture.DeclarationBody(source, "public AgentSettings ReloadSettings()");
        var reset = AgentSourceFixture.DeclarationBody(source, "public bool TryResetSessionStats(out string message)");

        Assert.Contains("_budgetGuard.UpdateLimits(settings.MaxSessionTokens, settings.MaxSessionRequests)", save, StringComparison.Ordinal);
        Assert.False(save.Contains("CreateBudgetGuard", StringComparison.Ordinal), "SaveSettings must not recreate the budget guard.");
        Assert.False(save.Contains("_budgetGuard =", StringComparison.Ordinal), "SaveSettings must keep the existing guard instance.");

        Assert.Contains("_budgetGuard.UpdateLimits(loaded.MaxSessionTokens, loaded.MaxSessionRequests)", reload, StringComparison.Ordinal);
        Assert.False(reload.Contains("CreateBudgetGuard", StringComparison.Ordinal), "ReloadSettings must not recreate the budget guard.");
        Assert.False(reload.Contains("_budgetGuard =", StringComparison.Ordinal), "ReloadSettings must keep the existing guard instance.");

        Assert.Contains("_budgetGuard = _settings.CreateBudgetGuard()", reset, StringComparison.Ordinal);
    }

    public static void Observe_ZeroRequestsSpent_DoesNotInventARequest()
    {
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var immediate = new AgentTurnResult
        {
            Acted = "choose_map_node",
            ToolRounds = 0,
            RequestsSpent = 0
        };
        Assert.Null(guard.Observe(immediate));
        Assert.Equal(0, guard.RequestCount);
        Assert.Null(guard.CheckBudget());
    }

    public static void Observe_WaitingForGame_StillRecordsZero()
    {
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var wait = new AgentTurnResult
        {
            WaitingForGame = true,
            RequestsSpent = 0
        };
        Assert.Null(guard.Observe(wait));
        Assert.Equal(0, guard.RequestCount);
    }

    public static void Observe_NegativeRequestsSpent_ClampsToZero()
    {
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var negative = new AgentTurnResult
        {
            Acted = "play_card",
            RequestsSpent = -3
        };
        Assert.Null(guard.Observe(negative));
        Assert.Equal(0, guard.RequestCount);
    }

    public static void Observe_MultiRoundRequestsSpent_RecordsExactCount()
    {
        var guard = new SessionBudgetGuard(maxRequests: 5);
        var multi = new AgentTurnResult
        {
            Acted = "play_card",
            RequestsSpent = 2,
            Usage = new LlmUsage { TotalTokens = 40 }
        };
        Assert.Null(guard.Observe(multi));
        Assert.Equal(2, guard.RequestCount);
        Assert.Equal(40, guard.ConsumedTokens);
    }

    public static async Task Recovery_ImmediateZeroRequestActs_DoNotHitRequestCap()
    {
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var calls = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(
                turn: _ =>
                {
                    calls++;
                    if (calls <= 3)
                    {
                        return Task.FromResult(new AgentTurnResult
                        {
                            Acted = "choose_map_node",
                            ToolRounds = 0,
                            RequestsSpent = 0
                        });
                    }

                    return Task.FromResult(new AgentTurnResult
                    {
                        Acted = "play_card",
                        RequestsSpent = 1
                    });
                },
                report: _ => { },
                cancellationToken: CancellationToken.None,
                delay: (_, _) => Task.CompletedTask,
                budgetGuard: guard);
            Assert.True(false, "Expected AutoPlayStoppedException after the first real model request.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Contains("请求次数上限", ex.Message);
            Assert.Equal(4, calls);
            Assert.Equal(1, guard.RequestCount);
        }
    }
}
