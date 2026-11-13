using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class DecisionPacingTests
{
    public static async Task DisabledByDefaultAndNeverDelays()
    {
        var now = TimeSpan.FromSeconds(10);
        var waits = new List<TimeSpan>();
        var gate = new DecisionPacingGate(
            clock: () => now,
            delay: (wait, _) =>
            {
                waits.Add(wait);
                now += wait;
                return Task.CompletedTask;
            });

        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(null, CancellationToken.None));
        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(0, CancellationToken.None));
        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(-5, CancellationToken.None));
        Assert.Equal(0, waits.Count);
    }

    public static async Task WaitsOnlyRemainingElapsedIntervalAndClampsMaximum()
    {
        var now = TimeSpan.FromSeconds(100);
        var waits = new List<TimeSpan>();
        var gate = new DecisionPacingGate(
            clock: () => now,
            delay: (wait, _) =>
            {
                waits.Add(wait);
                now += wait;
                return Task.CompletedTask;
            });

        // First call never waits.
        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(3.0, CancellationToken.None));
        Assert.Equal(0, waits.Count);

        // Simulate 1.25s spent in LLM inference + card animation: second call waits only remaining 1.75s.
        now += TimeSpan.FromSeconds(1.25);
        var waited = await gate.WaitBeforePrimaryRequestAsync(3.0, CancellationToken.None);
        Assert.Equal(1, waits.Count);
        Assert.True(Math.Abs((waited - TimeSpan.FromSeconds(1.75)).TotalMilliseconds) < 1,
            $"Expected 1.75s remaining wait, got {waited.TotalSeconds}s.");

        // If >= 3.0s already elapsed, zero wait occurs.
        now += TimeSpan.FromSeconds(3.5);
        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(3.0, CancellationToken.None));
        Assert.Equal(1, waits.Count);

        // Clamps absurdly large values to MaxDecisionIntervalSeconds (300s).
        var clamped = await gate.WaitBeforePrimaryRequestAsync(999, CancellationToken.None);
        Assert.True(Math.Abs((clamped - TimeSpan.FromSeconds(AgentSettings.MaxDecisionIntervalSeconds)).TotalMilliseconds) < 1);
    }

    public static async Task CancellationAbortsWaitWithoutStamping()
    {
        var now = TimeSpan.FromSeconds(50);
        using var cts = new CancellationTokenSource();
        var shouldCancel = true;
        var postCancelWaits = new List<TimeSpan>();
        var gate = new DecisionPacingGate(
            clock: () => now,
            delay: (w, token) =>
            {
                if (shouldCancel)
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                postCancelWaits.Add(w);
                now += w;
                return Task.CompletedTask;
            });

        await gate.WaitBeforePrimaryRequestAsync(4.0, CancellationToken.None);
        now += TimeSpan.FromSeconds(1.0);

        var canceled = false;
        try
        {
            await gate.WaitBeforePrimaryRequestAsync(4.0, cts.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        Assert.True(canceled, "Canceling the token during pacing wait must throw OperationCanceledException.");

        // Because the canceled turn never dispatched, resuming on the SAME gate at t=52s still waits the remaining 2s from t=50s.
        shouldCancel = false;
        now = TimeSpan.FromSeconds(52);
        var resumedWait = await gate.WaitBeforePrimaryRequestAsync(4.0, CancellationToken.None);
        Assert.True(Math.Abs((resumedWait - TimeSpan.FromSeconds(2.0)).TotalMilliseconds) < 1,
            $"Original gate must retain t=50s stamp and wait 2.0s at t=52s, got {resumedWait.TotalSeconds}s.");
        Assert.Equal(1, postCancelWaits.Count);
        gate.Reset();
        Assert.Equal(TimeSpan.Zero, await gate.WaitBeforePrimaryRequestAsync(4.0, CancellationToken.None));
    }

    public static async Task AgentLoopPacesPrimaryRequestsRefreshesStateAndSkipsAutopilotAndTurnPlans()
    {
        var now = TimeSpan.FromSeconds(10);
        var waits = new List<TimeSpan>();
        var bridge = new PacingBridge();
        var settings = AgentSettings.CreateDefault();
        settings.MinDecisionIntervalSeconds = 5.0;

        var completions = new Queue<LlmCompletion>(new[]
        {
            // Primary request #1: multi-step turn plan (2 cards + end_turn in 1 LLM call).
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "p1",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0,"card_cid":11,"target_index":0,"reason":"strike","then":[{"action":"play_card","card_cid":12,"reason":"defend"},{"action":"end_turn","reason":"done"}]}"""
                    }
                }
            },
            // Primary request #2: next turn after pacing wait; must observe refreshed state after wait!
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "p2",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0,"target_index":0,"reason":"post-wait strike"}"""
                    }
                }
            }
        });

        var capturedStates = new List<string>();
        var loop = new AgentLoop(bridge, new RecordingFactory(completions, capturedStates), () => settings)
        {
            PacingGate = new DecisionPacingGate(
                clock: () => now,
                delay: (wait, _) =>
                {
                    waits.Add(wait);
                    now += wait;
                    // Simulate state settling during the pacing wait so we verify AgentLoop refreshes stateJson!
                    bridge.StateJson = PacingBridge.RefreshedCombatTurn2;
                    return Task.CompletedTask;
                })
        };

        // 1) Forced Autopilot move (empty hand, only end_turn): zero LLM requests, zero pacing waits!
        bridge.StateJson = PacingBridge.ForcedEndTurnState;
        var autoResult = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal("autopilot", autoResult.Source);
        Assert.Equal(0, autoResult.RequestsSpent);
        Assert.Equal(0, waits.Count);

        // 2) Primary LLM turn #1 with 3-step TurnPlan: dispatches immediately (first primary call -> 0 wait)
        // and executes both `then` steps without any pacing delay between steps!
        bridge.StateJson = PacingBridge.PlanCombatTurn1;
        var planResult = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, planResult.RequestsSpent);
        Assert.Equal(3, planResult.PlanSteps?.Count);
        Assert.Equal(TurnPlanStopReasons.Completed, planResult.PlanStopReason);
        Assert.Equal(0, waits.Count);

        // 3) Primary LLM turn #2 arriving 1.5s later: waits remaining 3.5s (5.0 - 1.5) and refreshes stateJson!
        now += TimeSpan.FromSeconds(1.5);
        bridge.StateJson = PacingBridge.StaleCombatTurn2;
        var secondPrimary = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, secondPrimary.RequestsSpent);
        Assert.Equal("play_card", secondPrimary.Acted);
        Assert.Equal(1, waits.Count);
        Assert.True(Math.Abs((waits[0] - TimeSpan.FromSeconds(3.5)).TotalMilliseconds) < 1,
            $"Expected 3.5s wait before second primary LLM call, got {waits[0].TotalSeconds}s.");
        Assert.True(capturedStates[1].Contains("FRESH_AFTER_WAIT", StringComparison.Ordinal),
            "AgentLoop must re-read compact state after a non-zero pacing wait.");
    }

    private sealed class RecordingFactory : ILlmClientFactory, ILlmClient
    {
        private readonly Queue<LlmCompletion> _completions;
        private readonly List<string> _capturedStates;

        public RecordingFactory(Queue<LlmCompletion> completions, List<string> capturedStates)
        {
            _completions = completions;
            _capturedStates = capturedStates;
        }

        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            var stateMsg = request.Messages.First(m => (m.Content ?? string.Empty).StartsWith("Latest compact game state:\n", StringComparison.Ordinal));
            _capturedStates.Add(stateMsg.Content ?? string.Empty);
            return Task.FromResult(_completions.Dequeue());
        }

        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");

        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class PacingBridge : IGameBridge
    {
        public const string ForcedEndTurnState =
            """{"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["end_turn"],"combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":0},"end_turn_will_kill_player":false,"hand":[],"enemies":[{"i":0,"hp":"50/50","alive":true,"hittable":true}]},"run":{"potions":[]}}""";

        public const string PlanCombatTurn1 =
            """{"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],"combat":{"player":{"hp":"70/80","block":0,"energy":2},"hand":[{"i":0,"cid":11,"line":"Strike","playable":true,"targets":[0]},{"i":1,"cid":12,"line":"Defend","playable":true,"targets":[]}],"enemies":[{"i":0,"enemy_id":"BOSS","hp":"100/100","alive":true,"hittable":true,"move_id":"SLAM","intents":[{"intent_type":"Attack","damage":16,"hits":1,"total_damage":16}]}]}}""";

        private const string PlanCombatAfterStep1 =
            """{"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],"combat":{"player":{"hp":"70/80","block":0,"energy":1},"hand":[{"i":0,"cid":12,"line":"Defend","playable":true,"targets":[]}],"enemies":[{"i":0,"enemy_id":"BOSS","hp":"94/100","alive":true,"hittable":true,"move_id":"SLAM","intents":[{"intent_type":"Attack","damage":16,"hits":1,"total_damage":16}]}]}}""";

        public const string StaleCombatTurn2 =
            """{"screen":"COMBAT","in_combat":true,"turn":2,"available_actions":["play_card","end_turn"],"combat":{"player":{"hp":"62/80","block":0,"energy":3},"hand":[{"i":0,"cid":21,"line":"STALE_BEFORE_WAIT","playable":true,"targets":[0]}],"enemies":[{"i":0,"enemy_id":"BOSS","hp":"94/100","alive":true,"hittable":true}]}}""";

        public const string RefreshedCombatTurn2 =
            """{"screen":"COMBAT","in_combat":true,"turn":2,"available_actions":["play_card","end_turn"],"combat":{"player":{"hp":"62/80","block":0,"energy":3},"hand":[{"i":0,"cid":21,"line":"FRESH_AFTER_WAIT","playable":true,"targets":[0]}],"enemies":[{"i":0,"enemy_id":"BOSS","hp":"94/100","alive":true,"hittable":true}]}}""";

        public string StateJson { get; set; } = ForcedEndTurnState;

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(StateJson);

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(StateJson);

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult("""{"available_actions":[{"action":"play_card","requires_card_index":true},{"action":"end_turn"}]}""");

        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"{{\"state\":{StateJson},\"available_actions\":[{{\"action\":\"play_card\",\"requires_card_index\":true}},{{\"action\":\"end_turn\"}}]}}");

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");

        public Task<string> ActAsync(
            string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y,
            string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null, string? mode = null)
        {
            validateState?.Invoke(StateJson);
            if (StateJson == PlanCombatTurn1 && action == "play_card")
            {
                StateJson = PlanCombatAfterStep1;
            }

            return Task.FromResult($"{{\"action\":\"{action}\",\"status\":\"completed\",\"stable\":true,\"state\":{StateJson}}}");
        }

        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");

        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("[]");

        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("[]");

        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }
}
