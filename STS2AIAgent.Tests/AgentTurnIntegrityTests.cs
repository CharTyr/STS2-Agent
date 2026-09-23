using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static partial class AgentTurnIntegrityTests
{
    private static LlmCompletion Act(string args = "{\"action\":\"play_card\",\"card_index\":0}") => new()
    {
        Usage = new LlmUsage { TotalTokens = 10 },
        ToolCalls = new[] { new LlmToolCall { Id = "act", Name = "act", ArgumentsJson = args } }
    };
    private static LlmCompletion Read() => new()
    {
        Usage = new LlmUsage { TotalTokens = 10 },
        ToolCalls = new[] { new LlmToolCall { Id = "read", Name = "get_game_state", ArgumentsJson = "{}" } }
    };
    private static AgentLoop Loop(Bridge bridge, Client client, SessionBudgetGuard? guard = null, AgentSettings? settings = null) =>
        new(bridge, client, () => settings ?? AgentSettings.CreateDefault(), budgetGuard: () => guard);

    public static async Task AcceptedActionSurvivesObservationFailure()
    {
        foreach (var pending in new[] { false, true })
        {
            var bridge = new Bridge { FailPostActionRead = true, Pending = pending };
            var client = new Client(Act(), Act());
            var result = await Loop(bridge, client).PlayOnceAsync(CancellationToken.None);
            Assert.Equal(1, bridge.ActCalls);
            Assert.Equal(1, client.Calls);
            Assert.Equal("play_card", result.Acted);
            Assert.True(result.ExecutedUnsettled);
            Assert.Null(result.Error);
            Assert.Equal(1, result.RequestsSpent);
            using var response = JsonDocument.Parse(result.ActResultJson!);
            Assert.False(response.RootElement.GetProperty("stable").GetBoolean());
        }
    }

    public static async Task ChatDoesNotReplayAcceptedAction()
    {
        var bridge = new Bridge { FailPostActionRead = true };
        var client = new Client(Act(), Act(), new LlmCompletion { Content = "done" });
        // The message itself is the opt-in: with no act switch left on the conversation, a turn only
        // reaches the act tool when the player's own words ask for it.
        var result = await Loop(bridge, client).ChatAsync("帮我打这回合", Array.Empty<ChatTurn>(),
            new ChatOptions(), CancellationToken.None);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal("play_card", result.Acted);
    }

    public static async Task InTurnTokensStopTheNextRequest()
    {
        var guard = new SessionBudgetGuard(maxTokens: 100, initialTokens: 95);
        var client = new Client(Read(), Act());
        var result = await Loop(new Bridge(), client, guard).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, client.Calls);
        Assert.NotNull(result.Error);
        Assert.Equal(10, result.Usage!.TotalTokens);
        Assert.Equal(95, guard.ConsumedTokens);
        guard.Observe(result);
        Assert.Equal(105, guard.ConsumedTokens);
        Assert.Equal(1, guard.RequestCount);
    }

    public static async Task VisionTokensStopThePrimaryRequest()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        var vision = new LlmModelConfig { EndpointId = settings.Endpoints[0].Id, Model = "vision-test", SupportsVision = true };
        settings.Models.Add(vision);
        settings.VisionModelId = vision.Id;
        var guard = new SessionBudgetGuard(maxTokens: 100, initialTokens: 95);
        var client = new Client(new LlmCompletion { Content = "screen", Usage = new LlmUsage { TotalTokens = 10 } }, Act());
        var result = await Loop(new Bridge { Screenshot = new byte[] { 1, 2, 3 } }, client, guard, settings)
            .PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, client.Calls);
        Assert.NotNull(result.Error);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(10, result.Usage!.TotalTokens);
    }

    public static async Task CanceledCompletedTurnIsRecorded()
    {
        using var cancellation = new CancellationTokenSource();
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var reports = new List<AgentTurnResult>();
        var canceled = false;
        try
        {
            await AutoPlayRecovery.RunAsync(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(new AgentTurnResult { Acted = "play_card", RequestsSpent = 1, Usage = new LlmUsage { TotalTokens = 25 } });
            }, reports.Add, cancellation.Token, (_, _) => Task.CompletedTask, guard);
        }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled);
        Assert.Equal(1, reports.Count);
        Assert.Equal("play_card", reports[0].Acted);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(25, guard.ConsumedTokens);
    }

    public static async Task CancellationAfterAcceptedActionKeepsReceipt()
    {
        using var cancellation = new CancellationTokenSource();
        var bridge = new Bridge { AfterAct = cancellation.Cancel };
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var client = new Client(Act(), Act());
        var reports = new List<AgentTurnResult>();
        var canceled = false;
        try { await AutoPlayRecovery.RunAsync(token => Loop(bridge, client, guard).PlayOnceAsync(token),
            reports.Add, cancellation.Token, (_, _) => Task.CompletedTask, guard); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, reports.Count);
        Assert.Equal("play_card", reports[0].Acted);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);
    }

    public static async Task CanceledLaterModelRequestKeepsEarlierUsage()
    {
        using var cancellation = new CancellationTokenSource();
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var client = new Client(Read(), Act()) { OnRequest = calls => { if (calls == 2) cancellation.Cancel(); } };
        var reports = new List<AgentTurnResult>();
        try { await AutoPlayRecovery.RunAsync(token => Loop(new Bridge(), client, guard).PlayOnceAsync(token),
            reports.Add, cancellation.Token, (_, _) => Task.CompletedTask, guard); }
        catch (OperationCanceledException) { }
        Assert.Equal(1, reports.Count);
        Assert.Equal(2, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);
        Assert.Null(reports[0].Acted);
    }

    public static async Task NonObjectActArgumentsAreRecoverable()
    {
        foreach (var args in new[] { "[]", "null", "42", "true", "\"text\"" })
        {
            var bridge = new Bridge();
            var client = new Client(Act(args), Act());
            var result = await Loop(bridge, client).PlayOnceAsync(CancellationToken.None);
            Assert.Equal(1, bridge.ActCalls);
            Assert.Equal(2, result.RequestsSpent);
            Assert.Equal(20, result.Usage!.TotalTokens);
            Assert.Equal("play_card", result.Acted);
            Assert.True(client.Requests[1].Messages.Any(message => message.Role == "tool" && (message.Content?.Contains("error") ?? false)));
        }
    }

    public static void ProactiveWorkRunsAfterRecoveryAccounting()
    {
        var runtime = AgentSourceFixture.MethodBody(AgentSourceFixture.ReadAgentRuntime(), "AutoPlayLoopAsync");
        Assert.Contains("afterTurn:", runtime);
        var after = runtime.IndexOf("afterTurn:", StringComparison.Ordinal);
        Assert.False(runtime[..after].Contains("await TryProactiveChatAsync", StringComparison.Ordinal));
    }

    public static async Task NormalTurnStillActsOnce()
    {
        var bridge = new Bridge();
        var client = new Client(Act(), Act());
        var result = await Loop(bridge, client).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, result.RequestsSpent);
        Assert.False(result.ExecutedUnsettled);
        Assert.Null(result.Error);
    }

    public static async Task ProactiveCannotStartAfterPlayHitsRequestCap()
    {
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var client = new Client(Act());
        var bridge = new Bridge();
        var reports = 0;
        var proactive = 0;
        var stopped = false;
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(bridge, client, guard).PlayOnceAsync(token),
                _ => reports++, CancellationToken.None, (_, _) => Task.CompletedTask, guard,
                afterTurn: _ => { proactive++; return Task.CompletedTask; });
        }
        catch (AutoPlayStoppedException ex) { stopped = ex.Kind == StopKindPolicy.Budget; }
        Assert.True(stopped);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, reports);
        Assert.Equal(0, proactive);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);
    }

    public static async Task ProactiveSeesCommittedPlayAndCannotDoubleCharge()
    {
        var guard = new SessionBudgetGuard(maxRequests: 2);
        var playClient = new Client(Act(), Act());
        var chatClient = new Client(new LlmCompletion { Content = "hello", Usage = new LlmUsage { TotalTokens = 7 } });
        var reports = 0;
        var proactive = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(new Bridge(), playClient, guard).PlayOnceAsync(token),
                _ => reports++, CancellationToken.None, (_, _) => Task.CompletedTask, guard,
                afterTurn: async token =>
                {
                    proactive++;
                    Assert.Equal(1, guard.RequestCount);
                    Assert.Equal(10, guard.ConsumedTokens);
                    var chat = await Loop(new Bridge(), chatClient, guard).ChatAsync("hello", Array.Empty<ChatTurn>(),
                        new ChatOptions { ReadOnly = true }, token);
                    guard.Observe(chat);
                });
        }
        catch (AutoPlayStoppedException ex) { Assert.Equal(StopKindPolicy.Budget, ex.Kind); }
        Assert.Equal(1, proactive);
        Assert.Equal(1, playClient.Calls);
        Assert.Equal(1, chatClient.Calls);
        Assert.Equal(1, reports);
        Assert.Equal(2, guard.RequestCount);
        Assert.Equal(17, guard.ConsumedTokens);
    }

    public static async Task InterruptedCallbackDoesNotPublishLiveUi()
    {
        using var cancellation = new CancellationTokenSource();
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var live = 0;
        var receipts = new List<AgentTurnResult>();
        try
        {
            await AutoPlayRecovery.RunAsync(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(new AgentTurnResult { Acted = "play_card", RequestsSpent = 1 });
            }, _ => live++, cancellation.Token, (_, _) => Task.CompletedTask, guard,
                reportInterrupted: receipts.Add);
        }
        catch (OperationCanceledException) { }
        Assert.Equal(0, live);
        Assert.Equal(1, receipts.Count);
        Assert.Equal("play_card", receipts[0].Acted);
        Assert.Null(receipts[0].Usage);
        Assert.Equal(1, guard.RequestCount);
    }

    public static async Task CancellationBeforeFirstRequestSpendsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var client = new Client(Act());
        var reports = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(new Bridge(), client, guard).PlayOnceAsync(token),
                _ => reports++, cancellation.Token, (_, _) => Task.CompletedTask, guard);
        }
        catch (OperationCanceledException) { }
        Assert.Equal(0, reports);
        Assert.Equal(0, client.Calls);
        Assert.Equal(0, guard.RequestCount);
    }

    public static async Task CanceledVisionRetainsRequestWithUnknownUsage()
    {
        using var cancellation = new CancellationTokenSource();
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        var vision = new LlmModelConfig { EndpointId = settings.Endpoints[0].Id, Model = "vision-test", SupportsVision = true };
        settings.Models.Add(vision);
        settings.VisionModelId = vision.Id;
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var client = new Client(Act()) { OnRequest = _ => cancellation.Cancel() };
        var receipts = new List<AgentTurnResult>();
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(new Bridge { Screenshot = new byte[] { 1 } }, client, guard, settings).PlayOnceAsync(token),
                receipts.Add, cancellation.Token, (_, _) => Task.CompletedTask, guard);
        }
        catch (OperationCanceledException) { }
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(1, receipts.Count);
        Assert.Null(receipts[0].Usage);
        Assert.Null(receipts[0].Acted);
    }

    public static async Task RunBoundaryAfterActionRetainsReceipt()
    {
        var bridge = new Bridge();
        var client = new Client(Act());
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var receipts = new List<AgentTurnResult>();
        var stopped = false;
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(bridge, client, guard).PlayOnceAsync(token,
                _ => { if (bridge.ActCalls > 0) throw new AutoPlayStoppedException("run ended", "run_ended"); }),
                receipts.Add, CancellationToken.None, (_, _) => Task.CompletedTask, guard);
        }
        catch (AutoPlayStoppedException ex) { stopped = true; Assert.Equal("run ended", ex.Message); }
        Assert.True(stopped);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, receipts.Count);
        Assert.Equal("play_card", receipts[0].Acted);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);
    }

    public static async Task JsonFallbackAlsoKeepsAcceptedAction()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsTools = false;
        var client = new Client(new LlmCompletion { Content = "{\"action\":\"play_card\",\"card_index\":0}", Usage = new LlmUsage { TotalTokens = 12 } });
        var bridge = new Bridge { FailPostActionRead = true };
        var result = await Loop(bridge, client, settings: settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, client.Calls);
        Assert.Equal("play_card", result.Acted);
        Assert.True(result.ExecutedUnsettled);
        Assert.Equal(12, result.Usage!.TotalTokens);
    }

    public static async Task MalformedArgumentsReturnStructuredErrorWithinBudget()
    {
        foreach (var input in new[] { "[]", "null", "42", "true", "\"text\"", "{broken" })
        {
            var guard = new SessionBudgetGuard(maxRequests: 2);
            var client = new Client(Act(input), new LlmCompletion { Content = "done" });
            var bridge = new Bridge();
            var result = await Loop(bridge, client, guard).PlayOnceAsync(CancellationToken.None);
            Assert.Equal(0, bridge.ActCalls);
            Assert.NotNull(result.Error);
            Assert.Equal(2, result.RequestsSpent);
            var message = client.Requests[1].Messages.Single(message => message.Role == "tool");
            using var error = JsonDocument.Parse(message.Content!);
            Assert.Equal("invalid_request", error.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(10, result.Usage!.TotalTokens);
        }
    }

    public static void PendingBudgetCheckDoesNotMutateCounters()
    {
        var guard = new SessionBudgetGuard(maxTokens: 100, initialTokens: 95, initialRequests: 3);
        for (var i = 0; i < 3; i++) Assert.NotNull(guard.CheckBudget(1, 10));
        Assert.Equal(95, guard.ConsumedTokens);
        Assert.Equal(3, guard.RequestCount);
        Assert.Null(guard.CheckBudget(extraTokens: -10));
    }

    public static void RuntimeConsumersPreserveInterruptedReceipts()
    {
        var runtime = AgentSourceFixture.ReadAgentRuntime();
        foreach (var method in new[] { "SendChatCoreAsync", "StepOnceCoreAsync", "ReplyToTeammateAsync", "TryProactiveChatAsync" })
        {
            var body = AgentSourceFixture.MethodBody(runtime, method);
            Assert.Contains("catch (AgentTurnCanceledException ex)", body);
            Assert.Contains("RecordTurnReceipt(ex.Receipt, recordBudget: true)", body);
        }
        var autoplay = AgentSourceFixture.MethodBody(runtime, "AutoPlayLoopAsync");
        Assert.Contains("reportInterrupted: result => RecordTurnReceipt(result)", autoplay);
        Assert.False(autoplay.Contains("reportInterrupted: result => ApplyPlayResult", StringComparison.Ordinal));
    }

    public static async Task WaitingTurnSeesUsageBeforeGateRelease()
    {
        using var turnGate = new SemaphoreSlim(1, 1);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var reported = 0;
        var recovery = AutoPlayRecovery.RunAsync(async _ =>
        {
            entered.SetResult(true);
            await finish.Task;
            return new AgentTurnResult { Acted = "play_card", RequestsSpent = 1, Usage = new LlmUsage { TotalTokens = 10 } };
        }, _ => { reported++; Assert.Equal(0, turnGate.CurrentCount); }, CancellationToken.None,
            budgetGuard: guard, turnGate: turnGate);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        async Task<int> WaitingRequest()
        {
            await turnGate.WaitAsync();
            try { return guard.RequestCount; }
            finally { turnGate.Release(); }
        }
        var waiting = WaitingRequest();
        Assert.False(waiting.IsCompleted);
        finish.SetResult(true);
        try { await recovery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (AutoPlayStoppedException ex) { Assert.Equal(StopKindPolicy.Budget, ex.Kind); }
        Assert.Equal(1, await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, reported);
        Assert.Equal(1, turnGate.CurrentCount);
    }

    public static async Task InterruptedReceiptIsCommittedBeforeGateRelease()
    {
        using var cancellation = new CancellationTokenSource();
        using var turnGate = new SemaphoreSlim(1, 1);
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var receipts = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(_ =>
            {
                cancellation.Cancel();
                throw new AgentTurnCanceledException(new AgentTurnResult { RequestsSpent = 1 },
                    new OperationCanceledException(cancellation.Token), cancellation.Token);
            }, _ => throw new Exception("Live UI must not update"), cancellation.Token,
                budgetGuard: guard,
                reportInterrupted: _ => { receipts++; Assert.Equal(0, turnGate.CurrentCount); },
                turnGate: turnGate);
        }
        catch (OperationCanceledException) { }
        Assert.Equal(1, receipts);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(1, turnGate.CurrentCount);
    }

    public static void RuntimeCommitsBeforeReleasingTheTurnGate()
    {
        var runtime = AgentSourceFixture.ReadAgentRuntime();
        var autoplay = AgentSourceFixture.MethodBody(runtime, "AutoPlayLoopAsync");
        Assert.Contains("turnGate: _turnGate", autoplay);
        foreach (var (method, marker) in new[]
        {
            ("SendChatCoreAsync", "RecordTurnReceipt(result, recordBudget: true)"),
            ("StepOnceCoreAsync", "_budgetGuard.Observe(result)"),
            ("ReplyToTeammateAsync", "AccountTurn(result, recordBudget: true)")
        })
        {
            var body = AgentSourceFixture.MethodBody(runtime, method);
            var commit = body.IndexOf(marker, StringComparison.Ordinal);
            var release = body.IndexOf("_turnGate.Release()", StringComparison.Ordinal);
            Assert.True(commit >= 0 && release > commit, method + " releases before accounting");
        }
    }

    public static async Task ReadToolCancellationPreservesPriorModelUsage()
    {
        using var cancellation = new CancellationTokenSource();
        var bridge = new Bridge { OnStateRead = n => { if (n == 2) cancellation.Cancel(); } };
        var client = new Client(Read(), Act());
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var receipts = new List<AgentTurnResult>();
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(bridge, client, guard).PlayOnceAsync(token),
                receipts.Add, cancellation.Token, budgetGuard: guard);
        }
        catch (OperationCanceledException) { }
        Assert.Equal(1, receipts.Count);
        Assert.Equal(1, client.Calls);
        Assert.Equal(0, bridge.ActCalls);
        Assert.Equal(1, guard.RequestCount);
        Assert.Equal(10, guard.ConsumedTokens);
    }

    public static async Task ProactiveCancellationKeepsPlayAndChatReceiptsOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var guard = new SessionBudgetGuard(maxRequests: 10);
        var bridge = new Bridge();
        var playClient = new Client(Act());
        var chatClient = new Client(Read(), Act()) { OnRequest = n => { if (n == 2) cancellation.Cancel(); } };
        var reported = new List<AgentTurnResult>();
        var chatReceipts = 0;
        try
        {
            await AutoPlayRecovery.RunAsync(token => Loop(bridge, playClient, guard).PlayOnceAsync(token),
                reported.Add, cancellation.Token, budgetGuard: guard,
                afterTurn: async token =>
                {
                    try
                    {
                        var result = await Loop(new Bridge(), chatClient, guard).ChatAsync("hello", Array.Empty<ChatTurn>(),
                            new ChatOptions { ReadOnly = true }, token);
                        guard.Observe(result);
                    }
                    catch (AgentTurnCanceledException ex)
                    {
                        chatReceipts++;
                        guard.Observe(ex.Receipt);
                        throw;
                    }
                });
        }
        catch (OperationCanceledException) { }
        Assert.Equal(1, reported.Count);
        Assert.Equal("play_card", reported[0].Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, playClient.Calls);
        Assert.Equal(2, chatClient.Calls);
        Assert.Equal(1, chatReceipts);
        Assert.Equal(3, guard.RequestCount);
        Assert.Equal(20, guard.ConsumedTokens);
    }

    public static async Task ExplicitRejectionStillAllowsACorrectedAction()
    {
        var bridge = new Bridge { RejectFirstAction = true };
        var client = new Client(Act(), Act());
        var result = await Loop(bridge, client).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(2, client.Calls);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Equal("play_card", result.Acted);
        Assert.Null(result.Error);
    }

    private sealed class Client(params LlmCompletion[] completions) : ILlmClientFactory, ILlmClient
    {
        private readonly Queue<LlmCompletion> _queue = new(completions);
        public int Calls { get; private set; }
        public Action<int>? OnRequest { get; init; }
        public List<LlmRequest> Requests { get; } = new();
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;
        public Task<string> PingAsync(string model, CancellationToken token) => throw new InvalidOperationException("No real provider calls.");
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken token) => Task.FromResult(true);
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            Requests.Add(request);
            OnRequest?.Invoke(Calls);
            token.ThrowIfCancellationRequested();
            if (_queue.Count == 0) throw new InvalidOperationException("Unexpected extra model request.");
            return Task.FromResult(_queue.Dequeue());
        }
    }

    private sealed class Bridge : IGameBridge
    {
        public int ActCalls { get; private set; }
        public bool FailPostActionRead { get; init; }
        public bool Pending { get; init; }
        public Action? AfterAct { get; init; }
        public Action<int>? OnStateRead { get; init; }
        public bool RejectFirstAction { get; init; }
        private bool _rejected;
        private int _stateReads;
        public byte[]? Screenshot { get; init; }
        private bool _failedRead;
        private const string State = "{\"screen\":\"COMBAT\",\"actions\":[\"play_card\"],\"available_actions\":[\"play_card\"],\"combat\":{\"hand\":[{\"i\":0,\"targets\":[]}]}}";
        public Task<string> GetCompactStateJsonAsync(CancellationToken token)
        {
            OnStateRead?.Invoke(++_stateReads);
            token.ThrowIfCancellationRequested();
            if (FailPostActionRead && ActCalls == 1 && !_failedRead)
            {
                _failedRead = true;
                throw new IOException("Injected post-action read failure");
            }
            return Task.FromResult(State);
        }
        public Task<string> GetRawStateJsonAsync(CancellationToken token) => GetCompactStateJsonAsync(token);
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken token) => Task.FromResult("[{\"name\":\"play_card\",\"requires_index\":true}]");
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken token) =>
            Task.FromResult("{\"state\":" + State + ",\"available_actions\":" + "[{\"name\":\"play_card\",\"requires_index\":true}]" + "}");
        public Task<string> GetScreenAsync(CancellationToken token) => Task.FromResult("COMBAT");
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken token, bool rawState = false)
        {
            token.ThrowIfCancellationRequested();
            if (RejectFirstAction && !_rejected)
            {
                _rejected = true;
                return Task.FromResult("""{"error":{"code":"invalid_action","message":"Rejected before mutation","retryable":false}}""");
            }
            ActCalls++;
            AfterAct?.Invoke();
            return Task.FromResult(Pending
                ? "{\"action\":\"play_card\",\"status\":\"pending\",\"stable\":false}"
                : "{\"action\":\"play_card\",\"status\":\"completed\",\"stable\":true}");
        }
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken token) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> ids, CancellationToken token) => Task.FromResult("{}");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> ids, CancellationToken token) => Task.FromResult("{}");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken token) => Task.FromResult(Screenshot);
    }
}
