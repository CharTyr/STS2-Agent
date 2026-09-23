using System.Net;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class AuditStateAndPlannerTests
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("Audit.MemoryExtendsRestoredRun", () => Task.Run(MemoryExtendsRestoredRun));
        yield return ("Audit.MemoryBoundedAndIsolated", () => Task.Run(MemoryBoundedAndIsolated));
        yield return ("Audit.StrategySnapshotsAreImmutable", () => Task.Run(StrategySnapshotsAreImmutable));
        yield return ("Audit.StrategyChangesNotifyPersistence", () => Task.Run(StrategyChangesNotifyPersistence));
        yield return ("Audit.PlannerCannotOverwriteNewerPlan", PlannerCannotOverwriteNewerPlan);
        yield return ("Audit.PlannerInvalidatedOnPause", PlannerInvalidatedOnPause);
        yield return ("Audit.PlannerBudgetPreventsRequest", PlannerBudgetPreventsRequest);
        yield return ("Audit.PlannerNoModelNoCharge", PlannerNoModelNoCharge);
        yield return ("Audit.PlannerCancelKeepsReturnedUsage", PlannerCancelKeepsReturnedUsage);
        yield return ("Audit.PlannerFailureReservesExactlyOnce", PlannerFailureReservesExactlyOnce);
        yield return ("Audit.PlannerCadenceResets", () => Task.Run(PlannerCadenceResets));
        yield return ("Audit.RuntimePersistsAllWriters", () => Task.Run(RuntimePersistsAllWriters));
        yield return ("Audit.StoreFailedSaveCanRetry", () => Task.Run(StoreFailedSaveCanRetry));
        yield return ("Audit.StoreLoadPreservesTimestamp", () => Task.Run(StoreLoadPreservesTimestamp));
        yield return ("Audit.StoreWindowsNames", () => Task.Run(StoreWindowsNames));
        yield return ("Audit.StoreLegacyDelete", () => Task.Run(StoreLegacyDelete));
        yield return ("Audit.JevErrorRedactsEchoedKey", JevErrorRedactsEchoedKey);
        yield return ("Audit.JevFailureAccountsAttempt", JevFailureAccountsAttempt);
        yield return ("Audit.JevMalformedChoiceKeepsUsage", JevMalformedChoiceKeepsUsage);
        yield return ("Audit.JevInFlightCancelAccountsAttempt", JevInFlightCancelAccountsAttempt);
        yield return ("Audit.JevCannotInventRequiredArguments", () => Task.Run(JevCannotInventRequiredArguments));
        yield return ("Audit.JevUsesSnapshotScreen", JevUsesSnapshotScreen);
        yield return ("Audit.JevTimeoutIsOneBoundedAttempt", JevTimeoutIsOneBoundedAttempt);
        yield return ("Audit.JevInvalidUrlIsConfiguration", () => Task.Run(JevInvalidUrlIsConfiguration));
        yield return ("Audit.StoreDeletePreservesCollidingLegacyRun", () => Task.Run(StoreDeletePreservesCollidingLegacyRun));
        yield return ("Audit.StoreSavePreservesCollidingLegacyRun", () => Task.Run(StoreSavePreservesCollidingLegacyRun));
    }

    private static DecisionLogEntry Entry(long id, string run, string time = "old") =>
        new(id, time, "agent_loop", "play_card", "reason", "fp", 1, 4, run);

    private static void MemoryExtendsRestoredRun()
    {
        var memory = new PlaySessionMemory();
        memory.Restore("run-a", new[] { Entry(1, "run-a") });
        Assert.True(memory.Record(Entry(1, "run-a", "new")));
        Assert.False(memory.Record(Entry(1, "run-a", "new")));
        Assert.False(memory.Record(Entry(2, "run-b")));
        Assert.Equal(2, memory.Snapshot().Count);
        Assert.Equal("new", memory.Snapshot()[1].timestamp);
    }

    private static void MemoryBoundedAndIsolated()
    {
        var memory = new PlaySessionMemory();
        memory.Restore("run-a", Enumerable.Range(0, 250).Select(i => Entry(i, "run-a")));
        Assert.Equal(PlaySessionStore.MaxDecisions, memory.Snapshot(999).Count);
        Assert.Equal(249L, memory.Snapshot()[^1].id);
        memory.Restore("run-b", new[] { Entry(250, "run-a") });
        Assert.Equal(0, memory.Snapshot().Count);
    }

    private static void StrategySnapshotsAreImmutable()
    {
        var hints = new Dictionary<string, string> { ["play_card"] = "old" };
        var store = new StrategyStore();
        store.Update(new PlayStrategy { OptionHints = hints });
        hints["play_card"] = "mutated";
        Assert.Equal("old", store.Current.OptionHints["play_card"]);
        Assert.True(((IDictionary<string, string>)store.Current.OptionHints).IsReadOnly);
    }

    private static void StrategyChangesNotifyPersistence()
    {
        var store = new StrategyStore();
        var changed = 0;
        store.Changed += () => changed++;
        store.Update(new PlayStrategy { Source = "mcp" });
        var revision = store.Revision;
        Assert.True(store.TryUpdate(new PlayStrategy { Source = "llm" }, revision));
        Assert.False(store.TryUpdate(PlayStrategy.Default, revision));
        Assert.Equal(2, changed);
    }

    private static StrategyPlanner Planner(Client client, StrategyStore store, AgentSettings? settings = null) =>
        new(client, () => settings ?? AgentSettings.CreateDefault(), store);
    private static LlmCompletion Plan() => new() { Content = "{\"instructions\":\"new plan\"}", Usage = new LlmUsage { TotalTokens = 9 } };

    private static async Task PlannerCannotOverwriteNewerPlan()
    {
        var ready = new TaskCompletionSource<LlmCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { Completion = (_, _) => ready.Task };
        var store = new StrategyStore();
        var pending = Planner(client, store).RefreshAsync("state", CancellationToken.None);
        store.Update(new PlayStrategy { Source = "mcp", Instructions = "player plan" });
        ready.SetResult(Plan());
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Adopted);
        Assert.Equal("player plan", store.Current.Instructions);
        Assert.Equal(9, result.Usage!.TotalTokens);
    }

    private static async Task PlannerInvalidatedOnPause()
    {
        var ready = new TaskCompletionSource<LlmCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new StrategyStore();
        var pending = Planner(new Client { Completion = (_, _) => ready.Task }, store).RefreshAsync("state", CancellationToken.None);
        store.Invalidate();
        ready.SetResult(Plan());
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Adopted);
        Assert.Equal(PlayStrategy.DefaultSource, store.Current.Source);
    }

    private static async Task PlannerBudgetPreventsRequest()
    {
        var client = new Client();
        var guard = new SessionBudgetGuard(maxRequests: 1, initialRequests: 1);
        var result = await Planner(client, new StrategyStore()).RefreshAsync("state", CancellationToken.None,
            _ => Task.FromResult(guard.CheckBudget() == null));
        Assert.False(result.Adopted);
        Assert.Equal(0, client.Calls);
        Assert.Equal(1, guard.RequestCount);
    }

    private static async Task PlannerNoModelNoCharge()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Models.Clear();
        settings.Endpoints.Clear();
        var reserved = 0;
        var client = new Client();
        await Planner(client, new StrategyStore(), settings).RefreshAsync("state", CancellationToken.None,
            _ => { reserved++; return Task.FromResult(true); });
        Assert.Equal(0, client.Calls);
        Assert.Equal(0, reserved);
    }

    private static async Task PlannerCancelKeepsReturnedUsage()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new Client { Completion = (_, _) => { cancellation.Cancel(); return Task.FromResult(Plan()); } };
        var store = new StrategyStore();
        var result = await Planner(client, store).RefreshAsync("state", cancellation.Token);
        Assert.False(result.Adopted);
        Assert.Equal(9, result.Usage!.TotalTokens);
        Assert.Equal(PlayStrategy.DefaultSource, store.Current.Source);
    }

    private static async Task PlannerFailureReservesExactlyOnce()
    {
        var client = new Client { Completion = (_, _) => throw new IOException("test failure") };
        var guard = new SessionBudgetGuard(maxRequests: 2);
        try
        {
            await Planner(client, new StrategyStore()).RefreshAsync("state", CancellationToken.None,
                _ => { guard.Record(0, 1); return Task.FromResult(true); });
            throw new Exception("Expected provider failure");
        }
        catch (IOException) { }
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, guard.RequestCount);
    }

    private static void PlannerCadenceResets()
    {
        var planner = Planner(new Client(), new StrategyStore());
        Assert.True(planner.ShouldRefresh("combat", 0.1, 0.35));
        for (var round = 0; round < 2; round++)
        {
            Assert.False(planner.ShouldRefresh("combat", 0.1, 0.35));
            Assert.False(planner.ShouldRefresh("combat", 0.1, 0.35));
            Assert.True(planner.ShouldRefresh("combat", 0.1, 0.35));
        }
    }

    private static void RuntimePersistsAllWriters()
    {
        var runtime = AgentSourceFixture.ReadAgentRuntime();
        Assert.Contains("_decisions.Recorded += OnSessionDecision", runtime);
        Assert.Contains("_strategyStore.Changed += MarkSessionDirty", runtime);
        Assert.Contains("MarkSessionDirty()", AgentSourceFixture.MethodBody(runtime, "ClearChat"));
        Assert.Contains("FlushSessionIfDirty()", AgentSourceFixture.MethodBody(runtime, "AutoPlayLoopAsync"));
        foreach (var method in new[] { "StepOnceCoreAsync", "SendChatCoreAsync" })
        {
            var body = AgentSourceFixture.MethodBody(runtime, method);
            Assert.Contains("ObserveCurrentSessionAsync", body);
            Assert.Contains("FlushSessionIfDirty()", body);
        }
        Assert.Contains("BeginRequest, revision", AgentSourceFixture.MethodBody(runtime, "ObserveStrategyContext"));
        Assert.Contains("CancelStrategyRefresh()", AgentSourceFixture.MethodBody(runtime, "StopAutoPlay"));
    }

    private static void WithStore(Action<PlaySessionStore> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sts2-audit-" + Guid.NewGuid().ToString("N"));
        try { test(new PlaySessionStore(dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); else if (File.Exists(dir)) File.Delete(dir); }
    }

    private static void StoreFailedSaveCanRetry() => WithStore(store =>
    {
        File.WriteAllText(store.Directory, "block directory creation");
        var record = new PlaySessionRecord { RunId = "retry" };
        Assert.False(store.Save(record));
        File.Delete(store.Directory);
        Assert.True(store.Save(record));
        Assert.NotNull(store.Load("retry"));
    });

    private static void StoreLoadPreservesTimestamp() => WithStore(store =>
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(Path.Combine(store.Directory, "timestamp.json"), """{"run_id":"timestamp","updated_at":"2026-01-01T00:00:00Z"}""");
        Assert.Equal("2026-01-01T00:00:00Z", store.Load("timestamp")!.UpdatedAt);
    });

    private static void StoreWindowsNames() => WithStore(store =>
    {
        var ids = new[] { "seed", "Seed", "con", "a*b?c", new string('a', 240) };
        foreach (var id in ids) Assert.True(store.Save(new PlaySessionRecord { RunId = id }));
        foreach (var id in ids) Assert.Equal(id, store.Load(id)!.RunId);
    });

    private static void StoreLegacyDelete() => WithStore(store =>
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(Path.Combine(store.Directory, "Seed.json"), """{"run_id":"Seed"}""");
        Assert.NotNull(store.Load("Seed"));
        store.Delete("Seed");
        Assert.Null(store.Load("Seed"));
    });

    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; init; } = "{}";
        public HttpStatusCode Status { get; init; } = HttpStatusCode.BadRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
    }

    private static async Task JevErrorRedactsEchoedKey()
    {
        const string secret = "test-secret-unusual-provider-token-123456789";
        using var http = new HttpClient(new Handler { Body = "invalid token: " + secret });
        var client = new JevClient("https://example.test", secret, "jev", http, maxRetries: 0);
        var message = await client.PingAsync(CancellationToken.None);
        Assert.False(message.Contains(secret, StringComparison.Ordinal));
        Assert.Contains("400", message);
    }

    private sealed class JevStub : IJevClient
    {
        public JevRequest? LastRequest { get; private set; }
        public Func<CancellationToken, Task<JevResponse>> Response { get; init; } = _ => Task.FromResult(new JevResponse());
        public Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken token) { LastRequest = request; return Response(token); }
        public Task<string> PingAsync(CancellationToken token) => Task.FromResult("test");
    }
    private const string Frame = """{"state":{"screen":"COMBAT","available_actions":["end_turn"]},"available_actions":[{"name":"end_turn"}]}""";

    private static async Task JevFailureAccountsAttempt()
    {
        var decider = new JevExecutionDecider(new JevStub { Response = _ => throw new JevException("offline", JevExceptionKind.Network) }, "jev");
        var result = await decider.DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        Assert.Equal(1, result.RequestsSpent);
        Assert.NotNull(result.Error);
    }

    private static async Task JevMalformedChoiceKeepsUsage()
    {
        var decider = new JevExecutionDecider(new JevStub { Response = _ => Task.FromResult(new JevResponse
        {
            Answers = new Dictionary<string, JevAnswer> { ["action"] = null! },
            Usage = new JevUsage { InputTokens = 5, OutputTokens = 2 }
        }) }, "jev");
        var result = await decider.DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(7, result.Usage!.TotalTokens);
        Assert.NotNull(result.Error);
    }

    private static async Task JevInFlightCancelAccountsAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var decider = new JevExecutionDecider(new JevStub { Response = token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); throw new Exception(); } }, "jev");
        try { await decider.DecideAsync(Frame, PlayStrategy.Default, cancellation.Token); throw new Exception("Expected cancel"); }
        catch (AgentTurnCanceledException ex) { Assert.Equal(1, ex.Receipt.RequestsSpent); }
    }

    private static void JevCannotInventRequiredArguments()
    {
        foreach (var frame in new[]
        {
            """{"state":{},"available_actions":[{"name":"choose_map_node","requires_index":true}]}""",
            """{"state":{"map":{"options":[{"i":0,"locked":true}]}},"available_actions":[{"name":"choose_map_node","requires_index":true}]}""",
            """{"state":{},"available_actions":[{"name":"custom_tool","requires_tool":true}]}"""
        }) Assert.Equal(0, JevOptionEnumerator.Enumerate(frame).Count);
    }

    private static async Task JevUsesSnapshotScreen()
    {
        var client = new JevStub();
        await new JevExecutionDecider(client, "jev").DecideAsync(Frame, PlayStrategy.Default, CancellationToken.None);
        var instructions = System.Text.Json.JsonSerializer.SerializeToElement(client.LastRequest!.Questions.Values.Single(q => q.Type == "choice").Instructions);
        Assert.Equal("COMBAT", instructions.GetProperty("screen").GetString());
        Assert.Equal(PlayPrompt.PlaybookGuidance("COMBAT"), instructions.GetProperty("playbook").GetString());
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Expected timeout cancellation");
        }
    }

    private static async Task JevTimeoutIsOneBoundedAttempt()
    {
        var handler = new StalledHandler();
        using var http = new HttpClient(handler);
        var client = new JevClient("https://example.test", "test-key", "jev", http, maxRetries: 0, requestTimeout: TimeSpan.FromMilliseconds(20));
        try
        {
            await client.SystemOneAsync(new JevRequest { Model = "jev" }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            throw new Exception("Expected timeout");
        }
        catch (JevException ex) { Assert.Equal(JevExceptionKind.Network, ex.Kind); }
        Assert.Equal(1, handler.Calls);
    }

    private static void JevInvalidUrlIsConfiguration()
    {
        foreach (var url in new[] { "not a URL", "file:///private", "https://user:secret@example.test", "https://example.test?key=secret" })
        {
            try { _ = new JevClient(url, "test-key", "jev"); throw new Exception("Expected configuration error"); }
            catch (JevException ex) { Assert.Equal(JevExceptionKind.Config, ex.Kind); Assert.False(ex.Message.Contains("secret")); }
        }
    }

    private static void StoreDeletePreservesCollidingLegacyRun() => WithStore(store =>
    {
        Assert.True(store.Save(new PlaySessionRecord { RunId = "a/b" }));
        Assert.True(store.Save(new PlaySessionRecord { RunId = "a_b" }));
        store.Delete("a/b");
        Assert.Null(store.Load("a/b"));
        Assert.NotNull(store.Load("a_b"));
        File.WriteAllText(Path.Combine(store.Directory, "seed.json"), """{"run_id":"Seed"}""");
        store.Delete("seed");
        Assert.True(File.Exists(Path.Combine(store.Directory, "seed.json")));
        Assert.Equal("Seed", store.Load("Seed")!.RunId);
    });

    private static void StoreSavePreservesCollidingLegacyRun() => WithStore(store =>
    {
        Directory.CreateDirectory(store.Directory);
        foreach (var (oldId, newId) in new[] { ("Seed", "seed"), ("one/two", "one_two") })
        {
            // Use the colliding canonical spelling so this also runs on case-sensitive hosts.
            File.WriteAllText(Path.Combine(store.Directory, newId + ".json"), "{\"run_id\":\"" + oldId + "\"}");
            Assert.True(store.Save(new PlaySessionRecord { RunId = newId }));
            Assert.True(store.Save(new PlaySessionRecord { RunId = newId }));
            Assert.Equal(oldId, store.Load(oldId)!.RunId);
            Assert.Equal(newId, store.Load(newId)!.RunId);
        }
        File.WriteAllText(Path.Combine(store.Directory, "back_up.json.bak"), """{"run_id":"back/up"}""");
        Assert.True(store.Save(new PlaySessionRecord { RunId = "back_up" }));
        Assert.True(store.Save(new PlaySessionRecord { RunId = "back_up" }));
        Assert.Equal("back/up", store.Load("back/up")!.RunId);
    });

    private sealed class Client : ILlmClientFactory, ILlmClient
    {
        public int Calls { get; private set; }
        public Func<LlmRequest, CancellationToken, Task<LlmCompletion>> Completion { get; init; } = (_, _) => Task.FromResult(Plan());
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken token) { Calls++; return Completion(request, token); }
        public Task<string> PingAsync(string model, CancellationToken token) => throw new InvalidOperationException("No network");
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken token) => Task.FromResult(true);
    }
}
