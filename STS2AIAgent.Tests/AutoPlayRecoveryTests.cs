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

    public static async Task TimeoutFailureDoesNotLookLikeUserCancel()
    {
        using var lifetime = new CancellationTokenSource();
        var calls = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(token =>
            {
                Assert.False(token.IsCancellationRequested);
                calls++;
                if (calls >= 2)
                {
                    lifetime.Cancel();
                }

                throw new LlmException("LLM request timed out.", 408);
            }, _ => { }, lifetime.Token, (_, _) => Task.CompletedTask);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (AutoPlayStoppedException)
        {
        }

        Assert.True(calls >= 2, "timeout LlmException must be retryable, not treated as user cancel");
    }

    // --- No-progress guard -----------------------------------------------------------------------

    /// <summary>
    /// A legal action that changes nothing is the spin the old success branch reset away: every turn
    /// looked successful, so the failure budget never grew. The same (action, state) pair has to be
    /// treated as no progress and stop the session.
    /// </summary>
    public static Task UnchangedActionStopsTheLoop()
    {
        var policy = new AutoPlayRecovery();
        var same = new AgentTurnResult
        {
            Acted = "play_card",
            StateFingerprint = "same-state",
            ActResultJson = """{"action":"play_card","status":"completed","stable":true}"""
        };

        // The first occurrence records the pair; the run reaches the threshold on the third turn.
        for (var turn = 1; turn < NoProgressPolicy.RepeatThreshold; turn++)
        {
            Assert.Null(policy.Observe(same).StopReason);
        }

        var stop = policy.Observe(same);
        Assert.NotNull(stop.StopReason);
        Assert.Equal(StopKindPolicy.Failed, stop.StopKind);
        Assert.Equal(TimeSpan.Zero, stop.Delay);
        Assert.Contains("play_card", stop.StopReason!, StringComparison.Ordinal);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The other half of the truth table: the guard must not fire on real progress, or it would stop
    /// healthy sessions. A changed action, a changed state, or a turn with no fingerprint at all is
    /// not a repeat.
    /// </summary>
    public static Task ProgressResetsTheRepeatRun()
    {
        // Alternating actions on a frozen state is not "the same action repeated".
        var alternating = new AutoPlayRecovery();
        for (var turn = 0; turn < 10; turn++)
        {
            var action = turn % 2 == 0 ? "play_card" : "end_turn";
            Assert.Null(alternating.Observe(new AgentTurnResult { Acted = action, StateFingerprint = "frozen" }).StopReason);
        }

        // The same action that keeps moving the state forward is progress, not a spin.
        var moving = new AutoPlayRecovery();
        for (var turn = 0; turn < 10; turn++)
        {
            Assert.Null(moving.Observe(new AgentTurnResult { Acted = "play_card", StateFingerprint = "state-" + turn }).StopReason);
        }

        // Two identical turns, then real progress, then two more: the run never reaches the threshold.
        var mixed = new AutoPlayRecovery();
        for (var cycle = 0; cycle < 5; cycle++)
        {
            Assert.Null(mixed.Observe(new AgentTurnResult { Acted = "play_card", StateFingerprint = "a" }).StopReason);
            Assert.Null(mixed.Observe(new AgentTurnResult { Acted = "play_card", StateFingerprint = "a" }).StopReason);
            Assert.Null(mixed.Observe(new AgentTurnResult { Acted = "play_card", StateFingerprint = "b" }).StopReason);
        }

        // A turn that read no state cannot prove a stall, so it must never stop by itself.
        var fingerprintless = new AutoPlayRecovery();
        for (var turn = 0; turn < 10; turn++)
        {
            Assert.Null(fingerprintless.Observe(new AgentTurnResult { Acted = "play_card" }).StopReason);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// A pending act is not a failure, so it must not spend the retry budget: three hard failures are
    /// still required to stop, even with four executed-but-unsettled turns in between.
    /// </summary>
    public static Task UnsettledTurnsDoNotSpendTheRetryBudget()
    {
        var policy = new AutoPlayRecovery();
        Assert.Null(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);
        Assert.Null(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);

        for (var turn = 1; turn < NoProgressPolicy.UnsettledLimit; turn++)
        {
            Assert.Null(policy.Observe(new AgentTurnResult
            {
                Acted = "play_card",
                ExecutedUnsettled = true,
                StateFingerprint = "pending-" + turn
            }).StopReason);
        }

        // The hard failures still need a third one to stop; none of the pending turns counted.
        Assert.NotNull(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);
        return Task.CompletedTask;
    }

    /// <summary>
    /// "Not a failure" must not mean "wait forever": a run of pending turns has its own upper bound.
    /// </summary>
    public static Task UnsettledRunStopsAtItsLimit()
    {
        var policy = new AutoPlayRecovery();
        for (var turn = 1; turn < NoProgressPolicy.UnsettledLimit; turn++)
        {
            var pending = policy.Observe(new AgentTurnResult
            {
                Acted = "play_card",
                ExecutedUnsettled = true,
                StateFingerprint = "pending"
            });
            Assert.Null(pending.StopReason);
            Assert.True(pending.Delay > TimeSpan.Zero, "an unsettled turn should back off before asking again");
        }

        var stop = policy.Observe(new AgentTurnResult
        {
            Acted = "play_card",
            ExecutedUnsettled = true,
            StateFingerprint = "pending"
        });
        Assert.NotNull(stop.StopReason);
        Assert.Equal(StopKindPolicy.Failed, stop.StopKind);
        Assert.Equal(TimeSpan.Zero, stop.Delay);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The limit counts a run, not a total: one settled turn in the middle restarts the count, so a
    /// session that is slow but still making progress is never stopped.
    /// </summary>
    public static Task SettledTurnClearsTheUnsettledRun()
    {
        var policy = new AutoPlayRecovery();
        for (var turn = 1; turn < NoProgressPolicy.UnsettledLimit; turn++)
        {
            Assert.Null(policy.Observe(new AgentTurnResult
            {
                Acted = "play_card",
                ExecutedUnsettled = true,
                StateFingerprint = "first-" + turn
            }).StopReason);
        }

        Assert.Null(policy.Observe(new AgentTurnResult { Acted = "end_turn", StateFingerprint = "settled" }).StopReason);

        for (var turn = 1; turn < NoProgressPolicy.UnsettledLimit; turn++)
        {
            Assert.Null(policy.Observe(new AgentTurnResult
            {
                Acted = "play_card",
                ExecutedUnsettled = true,
                StateFingerprint = "second-" + turn
            }).StopReason);
        }

        Assert.NotNull(policy.Observe(new AgentTurnResult
        {
            Acted = "play_card",
            ExecutedUnsettled = true,
            StateFingerprint = "second-final"
        }).StopReason);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The waiting branch already refuses to erase earlier failures; a pending turn has the same
    /// duty, or a slow animation would heal a dying endpoint.
    /// </summary>
    public static Task UnsettledTurnsDoNotEraseEarlierFailures()
    {
        var policy = new AutoPlayRecovery();
        Assert.Null(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);
        Assert.Null(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);
        Assert.Null(policy.Observe(new AgentTurnResult
        {
            Acted = "play_card",
            ExecutedUnsettled = true,
            StateFingerprint = "pending"
        }).StopReason);

        Assert.NotNull(policy.Observe(new AgentTurnResult { Error = "network" }).StopReason);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The AgentLoop half of the fix. Its pending branch needs a bridge that never settles to reach,
    /// so the return shape is pinned here: the act stays an act, the turn carries the unsettled flag,
    /// and no error is reported for it.
    /// </summary>
    public static void PendingActReturnsWithoutAnError()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(
                AgentSourceFixture.Read("STS2AIAgent/Agent/AgentLoop.cs"),
                "private async Task<(string? Action, string ResultJson, string? Fingerprint, bool Unsettled, string? Error)> ExecuteActAsync("));

        Assert.Contains(
            "return(action,result,NoProgressPolicy.Fingerprint(latest),!settled,null);",
            body,
            StringComparison.Ordinal);
    }
}
