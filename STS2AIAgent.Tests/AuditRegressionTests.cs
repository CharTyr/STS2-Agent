using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class AuditRegressionTests
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        foreach (var test in AuditStateAndPlannerTests.All()) yield return test;
        yield return ("Audit.JevFallbackSpend", AgentTurnIntegrityTests.JevFallbackKeepsSpend);
        yield return ("Audit.JevRejectedSpend", AgentTurnIntegrityTests.JevRejectionKeepsSpend);
        yield return ("Audit.JevBudgetBeforeCall", AgentTurnIntegrityTests.JevHonorsBudgetBeforeCalling);
        yield return ("Audit.JevFallbackRequestCap", AgentTurnIntegrityTests.JevFallbackHonorsRequestCap);
        yield return ("Audit.JevFallbackVisionCap", AgentTurnIntegrityTests.JevFallbackHonorsVisionCap);
        yield return ("Audit.JevCancelReceipt", AgentTurnIntegrityTests.JevCancellationRetainsAcceptedAction);
        yield return ("Audit.JevBoundaryReceipt", AgentTurnIntegrityTests.JevBoundaryRetainsAcceptedAction);
        yield return ("Audit.JevFiniteConfidence", AgentTurnIntegrityTests.JevRejectsInvalidConfidence);
        yield return ("Audit.JevFallbackStillReportsConfidence", AgentTurnIntegrityTests.JevFallbackStillReportsConfidence);
        yield return ("Audit.JevUnexpectedFailureKeepsSpend", AgentTurnIntegrityTests.JevUnexpectedFailureKeepsSpend);
        yield return ("Audit.SessionNullElements", () => Task.Run(PlaySessionStoreTests.NullElementsCannotCrashRestore));
        yield return ("Audit.SessionIdentity", () => Task.Run(PlaySessionStoreTests.WrongRunCannotBeRestored));
        yield return ("Audit.SessionStrategySecrets", () => Task.Run(PlaySessionStoreTests.StrategySecretsAreRedacted));
        yield return ("Audit.SessionFilenameCollision", () => Task.Run(PlaySessionStoreTests.OpaqueRunIdsDoNotCollide));
        yield return ("Audit.SessionTrimCount", () => Task.Run(PlaySessionStoreTests.TrimmedTurnsAreCounted));
        yield return ("Audit.SessionDeleteBackup", () => Task.Run(PlaySessionStoreTests.DeleteAlsoRemovesBackup));
    }
}

internal static partial class AgentTurnIntegrityTests
{
    private sealed class AuditDecider : IActionDecider
    {
        public int Calls { get; private set; }
        public ExecutionDecision Decision { get; init; } = new()
        {
            Action = "play_card", CardIndex = 0, Confidence = 0.9,
            RequestsSpent = 1, Usage = new LlmUsage { TotalTokens = 7 }
        };
        public Task<ExecutionDecision> DecideAsync(string state, PlayStrategy strategy, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Decision);
        }
    }

    private static AgentLoop JevLoop(Bridge bridge, Client client, AuditDecider decider,
        SessionBudgetGuard? guard = null, AgentSettings? settings = null)
    {
        settings ??= AgentSettings.CreateDefault();
        settings.DualLayerSoloEnabled = true;
        return new AgentLoop(bridge, client, () => settings, budgetGuard: () => guard,
            decider: decider, strategyStore: new StrategyStore());
    }

    private static AuditDecider UncertainDecider() => new()
    {
        Decision = new ExecutionDecision { Action = "play_card", CardIndex = 0, Confidence = 0.1,
            RequestsSpent = 1, Usage = new LlmUsage { TotalTokens = 7 } }
    };

    public static async Task JevFallbackKeepsSpend()
    {
        var bridge = new Bridge();
        var client = new Client(Act());
        var result = await JevLoop(bridge, client, UncertainDecider()).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, client.Calls);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Equal(17, result.Usage!.TotalTokens);
    }

    public static async Task JevRejectionKeepsSpend()
    {
        var result = await JevLoop(new Bridge { RejectFirstAction = true }, new Client(Act()), new AuditDecider())
            .PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(2, result.RequestsSpent);
        Assert.Equal(17, result.Usage!.TotalTokens);
    }

    public static async Task JevHonorsBudgetBeforeCalling()
    {
        var decider = new AuditDecider();
        var result = await JevLoop(new Bridge(), new Client(Act()), decider,
            new SessionBudgetGuard(maxRequests: 1, initialRequests: 1)).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(0, decider.Calls);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.RequestsSpent);
    }

    public static async Task JevFallbackHonorsRequestCap()
    {
        var client = new Client(Act());
        var result = await JevLoop(new Bridge(), client, UncertainDecider(), new SessionBudgetGuard(maxRequests: 1))
            .PlayOnceAsync(CancellationToken.None);
        Assert.Equal(0, client.Calls);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(7, result.Usage!.TotalTokens);
        Assert.NotNull(result.Error);
    }

    public static async Task JevFallbackHonorsVisionCap()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        var vision = new LlmModelConfig { EndpointId = settings.Endpoints[0].Id, Model = "vision", SupportsVision = true };
        settings.Models.Add(vision);
        settings.VisionModelId = vision.Id;
        var client = new Client(new LlmCompletion { Content = "screen" }, Act());
        var result = await JevLoop(new Bridge { Screenshot = new byte[] { 1 } }, client, UncertainDecider(),
            new SessionBudgetGuard(maxTokens: 7), settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(0, client.Calls);
        Assert.Equal(1, result.RequestsSpent);
        Assert.NotNull(result.Error);
    }

    public static async Task JevCancellationRetainsAcceptedAction()
    {
        using var cancellation = new CancellationTokenSource();
        var bridge = new Bridge { AfterAct = cancellation.Cancel };
        try
        {
            await JevLoop(bridge, new Client(), new AuditDecider()).PlayOnceAsync(cancellation.Token);
            throw new Exception("Expected cancellation");
        }
        catch (AgentTurnCanceledException ex)
        {
            Assert.Equal("play_card", ex.Receipt.Acted);
            Assert.True(ex.Receipt.ExecutedUnsettled);
            Assert.Equal(1, ex.Receipt.RequestsSpent);
            Assert.Equal(7, ex.Receipt.Usage!.TotalTokens);
        }
        Assert.Equal(1, bridge.ActCalls);
    }

    public static async Task JevBoundaryRetainsAcceptedAction()
    {
        var bridge = new Bridge();
        try
        {
            await JevLoop(bridge, new Client(), new AuditDecider()).PlayOnceAsync(CancellationToken.None,
                _ => { if (bridge.ActCalls > 0) throw new AutoPlayStoppedException("run ended", "run_ended"); });
            throw new Exception("Expected run boundary");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.NotNull(ex.Receipt);
            Assert.Equal("play_card", ex.Receipt!.Acted);
            Assert.Equal(1, ex.Receipt.RequestsSpent);
        }
    }

    public static async Task JevRejectsInvalidConfidence()
    {
        foreach (var confidence in new[] { double.NaN, double.PositiveInfinity, 1.1 })
        {
            var decider = new AuditDecider { Decision = new ExecutionDecision
                { Action = "play_card", CardIndex = 0, Confidence = confidence, RequestsSpent = 1 } };
            var client = new Client(Act());
            var result = await JevLoop(new Bridge(), client, decider).PlayOnceAsync(CancellationToken.None);
            Assert.Equal(1, client.Calls);
            Assert.Null(result.Confidence);
        }
    }

    public static async Task JevFallbackStillReportsConfidence()
    {
        double? observed = null;
        var result = await JevLoop(new Bridge(), new Client(Act()), UncertainDecider())
            .PlayOnceAsync(CancellationToken.None, observeDecider: value => observed = value);
        Assert.Equal(0.1, observed);
        Assert.Null(result.Confidence);
    }

    public static async Task JevUnexpectedFailureKeepsSpend()
    {
        foreach (var fallback in new[] { false, true })
        {
            var bridge = new Bridge();
            var client = new Client();
            var result = await JevLoop(bridge, client, fallback ? UncertainDecider() : new AuditDecider())
                .PlayOnceAsync(CancellationToken.None, reportPhase: phase =>
                {
                    if (phase == (fallback ? PlayPhases.RequestingModel : PlayPhases.ExecutingAction))
                        throw new InvalidOperationException("test phase failure");
                });
            Assert.Contains("InvalidOperationException", result.Error!);
            Assert.Equal(1, result.RequestsSpent);
            Assert.Equal(7, result.Usage!.TotalTokens);
            Assert.Equal(0, bridge.ActCalls);
            Assert.Equal(0, client.Calls);
        }
    }
}

internal static partial class PlaySessionStoreTests
{
    public static void NullElementsCannotCrashRestore() => WithStore(store =>
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(Path.Combine(store.Directory, "null-items.json"),
            """{"run_id":"null-items","chat":[null],"decisions":[null],"strategy":{"optionHints":null}}""");
        var loaded = store.Load("null-items");
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded!.Chat!.Count);
        Assert.Equal(0, loaded.Decisions!.Count);
        Assert.NotNull(loaded.Strategy!.OptionHints);
    });

    public static void WrongRunCannotBeRestored() => WithStore(store =>
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(Path.Combine(store.Directory, "wanted.json"), """{"run_id":"other"}""");
        Assert.Null(store.Load("wanted"));
    });

    public static void StrategySecretsAreRedacted() => WithStore(store =>
    {
        const string secret = "sk-abcdefghijklmnopqrstuvwxyz0123456789";
        store.Save(new PlaySessionRecord { RunId = "secret-plan", Strategy = new PlayStrategy
        {
            Instructions = "my key is " + secret,
            OptionHints = new Dictionary<string, string> { ["play_card"] = secret }
        }});
        Assert.False(File.ReadAllText(Path.Combine(store.Directory, "secret-plan.json")).Contains(secret));
    });

    public static void OpaqueRunIdsDoNotCollide() => WithStore(store =>
    {
        foreach (var id in new[] { "one/two", "one:two", "one_two" })
            store.Save(new PlaySessionRecord { RunId = id });
        foreach (var id in new[] { "one/two", "one:two", "one_two" })
            Assert.Equal(id, store.Load(id)!.RunId);
    });

    public static void TrimmedTurnsAreCounted() => WithStore(store =>
    {
        store.Save(new PlaySessionRecord { RunId = "trim", ChatTrimmed = 3,
            Chat = Enumerable.Range(0, PlaySessionStore.MaxChatTurns + 4)
                .Select(i => new ChatTurn { Role = "user", Text = i.ToString() }).ToList() });
        Assert.Equal(7, store.Load("trim")!.ChatTrimmed);
    });

    public static void DeleteAlsoRemovesBackup() => WithStore(store =>
    {
        store.Save(new PlaySessionRecord { RunId = "deleted" });
        store.Save(new PlaySessionRecord { RunId = "deleted" });
        store.Delete("deleted");
        Assert.Equal(0, Directory.GetFiles(store.Directory).Length);
    });
}
