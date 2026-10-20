using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Tests;

internal static class SandpitSafetyTests
{
    // Regression frame reconstructed from the observed turn 5: HP does not prevent devouring.
    private const string Critical = """
        {"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{
          "player":{"hp":"62/87","energy":3},
          "lethal_risks":[{"risk_id":"sandpit_countdown","will_kill_player":true,"power_amount":1}],
          "hand":[{"i":0,"card_id":"UPPERCUT","energy_cost":2,"playable":true,"targets":[]},
                  {"i":1,"card_id":"FRANTIC_ESCAPE","energy_cost":1,"playable":true,"targets":[]}]}}
        """;
    private static string Safe => Critical.Replace("\"power_amount\":1", "\"power_amount\":3");
    private static LlmCompletion Action(string action, int? index = null) => new()
    {
        ToolCalls = new[] { new LlmToolCall { Id = "a", Name = "act", ArgumentsJson = JsonSerializer.Serialize(new { action, card_index = index }) } }
    };

    public static async Task CriticalTurnEscapesBeforeCallingTheModel()
    {
        var bridge = new Bridge(Critical);
        var client = new Client();
        var result = await new AgentLoop(bridge, client, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, bridge.Cards.Single());
        Assert.Equal(0, client.Calls);
        Assert.Equal(0, result.RequestsSpent);
        Assert.Contains("狂乱逃离", result.Reasoning);
    }

    public static async Task NoncriticalTurnsRemainModelControlled()
    {
        var bridge = new Bridge(Safe);
        var client = new Client(Action("play_card", 0));
        var result = await new AgentLoop(bridge, client, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(0, bridge.Cards.Single());
        Assert.Equal(1, client.Calls);
    }

    public static async Task AcceptedEscapeSurvivesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var bridge = new Bridge(Critical) { AfterAct = cancellation.Cancel };
        try
        {
            await new AgentLoop(bridge, new Client(), AgentSettings.CreateDefault).PlayOnceAsync(cancellation.Token);
            throw new Exception("Expected cancellation after the accepted escape.");
        }
        catch (AgentTurnCanceledException ex)
        {
            Assert.Equal("play_card", ex.Receipt.Acted);
            Assert.True(ex.Receipt.ExecutedUnsettled);
            Assert.Equal(0, ex.Receipt.RequestsSpent);
        }
        Assert.Single(bridge.Actions);
    }

    public static async Task FreshSnapshotPreventsSpendingEscapeEnergy()
    {
        var bridge = new Bridge(Safe);
        var client = new Client(Action("play_card", 0), Action("play_card", 1))
        { OnRequest = () => bridge.State = Critical };
        var result = await new AgentLoop(bridge, client, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(2, client.Calls);
        Assert.Single(bridge.Cards);
        Assert.Equal(1, bridge.Cards[0]);
    }

    public static async Task NoEscapeStopsBeforeLethalEndTurn()
    {
        var bridge = new Bridge(Critical.Replace("\"card_id\":\"FRANTIC_ESCAPE\"", "\"card_id\":\"DEFEND\""));
        var client = new Client(Action("end_turn"));
        try
        {
            await new AgentLoop(bridge, client, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
            throw new Exception("Expected a safety stop before end_turn.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Contains("沙坑", ex.Message);
            Assert.Equal(StopKindPolicy.Failed, ex.Kind);
            Assert.Equal(1, ex.Receipt!.RequestsSpent);
        }
        Assert.Equal(0, bridge.Actions.Count);
    }

    public static void UnplayableCardsAreNeverForced()
    {
        var state = Critical.Replace("\"energy_cost\":1,\"playable\":true", "\"energy_cost\":2,\"playable\":false");
        var decision = SandpitSafetyPolicy.Read(state);
        Assert.True(decision.Critical);
        Assert.Null(decision.EscapeIndex);
        Assert.Null(SandpitSafetyPolicy.Reject(decision, "use_potion", null));
        Assert.NotNull(SandpitSafetyPolicy.Reject(decision, "end_turn", null));
        Assert.False(SandpitSafetyPolicy.Read(Safe).Critical);
    }

    public static void EnemyPowerTargetAndUpgradeCopyAreWired()
    {
        var root = AgentSourceFixture.Root;
        var risks = File.ReadAllText(Path.Combine(root, "STS2AIAgent/Game/GameStateService.CombatRisks.cs"));
        Assert.Contains(".Powers.OfType<SandpitPower>()", risks);
        Assert.Contains("power.Target == playerCreature", risks);
        Assert.Contains(".Where(enemy => !enemy.IsDead)", risks);
        var data = File.ReadAllText(Path.Combine(root, "STS2AIAgent/Game/GameDataExportService.cs"));
        var preview = AgentSourceFixture.MethodBody(data, "BuildCardUpgradePreview");
        Assert.Contains("ModelDb.GetById<CardModel>(card.Id).ToMutable()", preview);
        Assert.Contains("upgraded.UpgradeInternal()", preview);
        Assert.Contains("upgraded.EnergyCost.GetWithModifiers", preview);
        Assert.False(preview.Contains("card.UpgradeInternal()"));
        Assert.Contains("BuildCardDynamicValuePayloads(upgraded, detachedCopy: true)", preview);
        var values = AgentSourceFixture.MethodBody(data, "BuildCardDynamicValuePayloads");
        Assert.Contains("detachedCopy ? card.DynamicVars : card.DynamicVars.Clone(card)", values);
        Assert.Contains("card.UpdateDynamicVarPreview(CardPreviewMode.Normal, card.CurrentTarget, previewSet)", values);
        Assert.Contains("catch when (!detachedCopy)", values);
        Assert.True(preview.IndexOf("BuildCardDynamicValuePayloads(upgraded,", StringComparison.Ordinal)
            < preview.IndexOf("upgraded.GetDescriptionForPile", StringComparison.Ordinal));
        Assert.Contains("Card rewards: take or skip", PlayPrompt.ScreenGuidance("REWARD"));
        Assert.Contains("upgrade.cost", PlayPrompt.ScreenGuidance("REST"));
        Assert.Contains("sandpit_countdown", PlayPrompt.ScreenGuidance("COMBAT"));
    }

    public static async Task NewRiskAtDispatchCannotEndTheTurn()
    {
        var bridge = new Bridge(Safe);
        bridge.BeforeGate = () => bridge.State = Critical.Replace("FRANTIC_ESCAPE", "DEFEND");
        try
        {
            await new AgentLoop(bridge, new Client(Action("end_turn")), AgentSettings.CreateDefault)
                .PlayOnceAsync(CancellationToken.None);
            throw new Exception("A risk appearing after preflight must still stop end_turn.");
        }
        catch (AutoPlayStoppedException ex)
        {
            Assert.Equal(StopKindPolicy.Failed, ex.Kind);
            Assert.Equal(1, ex.Receipt!.RequestsSpent);
        }
        Assert.Equal(0, bridge.Actions.Count);
        Assert.True(bridge.GateHeldForValidation);
        Assert.False(bridge.Gate.IsHeld);
    }

    public static async Task ChangedHandCannotPlayTheOldEscapeIndex()
    {
        var bridge = new Bridge(Critical);
        bridge.BeforeGate = () => bridge.State = Critical.Replace("FRANTIC_ESCAPE", "DEFEND");
        var client = new Client();
        var result = await new AgentLoop(bridge, client, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Null(result.Acted);
        Assert.Contains("手牌已变化", result.Error);
        Assert.Equal(0, bridge.Actions.Count);
        Assert.Equal(0, client.Calls);
        Assert.True(bridge.GateHeldForValidation);
        Assert.False(bridge.Gate.IsHeld);
    }

    public static async Task ClearedRiskCancelsTheStaleAutomaticEscape()
    {
        var bridge = new Bridge(Critical);
        bridge.BeforeGate = () => bridge.State = Safe;
        var result = await new AgentLoop(bridge, new Client(), AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Null(result.Acted);
        Assert.Contains("已取消自动逃离", result.Error);
        Assert.Equal(0, bridge.Actions.Count);
        Assert.Equal(0, result.RequestsSpent);
    }

    public static void ProductionValidationRunsUnderTheActionLease()
    {
        var service = AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ExecuteAsync");
        var acquire = service.IndexOf("ActionExecutionGate.Shared.Acquire", StringComparison.Ordinal);
        var validate = service.IndexOf("beforeDispatch?.Invoke()", StringComparison.Ordinal);
        var dispatch = service.IndexOf("await DispatchAsync", StringComparison.Ordinal);
        Assert.True(acquire >= 0 && validate > acquire && dispatch > validate);
        Assert.False(service[(validate + "beforeDispatch?.Invoke()".Length)..dispatch].Contains("await"));
        var bridge = AgentSourceFixture.Read("STS2AIAgent/Agent/GameBridge.cs");
        var callback = bridge.IndexOf("beforeDispatch: validateState", StringComparison.Ordinal);
        var fresh = bridge.IndexOf("var fresh = GameStateService.BuildStatePayload()", StringComparison.Ordinal);
        var invoke = bridge.IndexOf("validateState(JsonSerializer.Serialize(fresh.agent_view", StringComparison.Ordinal);
        Assert.True(callback >= 0 && fresh > callback && invoke > fresh);
    }

    public static void SandpitMessagesFollowTheSelectedLanguage()
    {
        var previous = Loc.Current;
        try
        {
            Loc.SetLanguage("eng");
            var description = SandpitSafetyPolicy.DescribeCountdown(1);
            Assert.Contains("Sandpit countdown 1", description);
            Assert.Contains("affected player", description);
            var stop = SandpitSafetyPolicy.Reject(new(true, null), "end_turn", null);
            Assert.Contains("Autoplay stopped", stop);
            Assert.Contains("card_index=1", SandpitSafetyPolicy.Reject(new(true, 1), "play_card", 0));
            Loc.SetLanguage("zhs");
            Assert.Contains("沙坑倒计时 1", SandpitSafetyPolicy.DescribeCountdown(1));
            Assert.Contains("已停止自动游玩", SandpitSafetyPolicy.Reject(new(true, null), "end_turn", null));
        }
        finally { Loc.SetLanguage(previous); }
    }

    private sealed class Client(params LlmCompletion[] responses) : ILlmClientFactory, ILlmClient
    {
        private readonly Queue<LlmCompletion> _responses = new(responses);
        public int Calls;
        public Action? OnRequest { get; init; }
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? timeout = null) => this;
        public Task<string> PingAsync(string model, CancellationToken token) => throw new Exception("Unexpected probe");
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken token) => throw new Exception("Unexpected probe");
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken token)
        { Calls++; OnRequest?.Invoke(); return Task.FromResult(_responses.Dequeue()); }
    }

    private sealed class Bridge(string state) : IGameBridge
    {
        public string State = state;
        public List<int?> Cards = new();
        public List<string> Actions = new();
        public Action? AfterAct { get; init; }
        public Action? BeforeGate { get; set; }
        public ActionExecutionGate Gate { get; } = new();
        public bool GateHeldForValidation;
        public Task<string> GetCompactStateJsonAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(State); }
        public Task<string> GetRawStateJsonAsync(CancellationToken token) => Task.FromResult(State);
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken token) => Task.FromResult("""[{"name":"play_card","requires_index":true},{"name":"end_turn"}]""");
        public async Task<string> GetActionSnapshotJsonAsync(CancellationToken token) => "{\"state\":" + State + ",\"available_actions\":" + await GetAvailableActionsJsonAsync(token) + "}";
        public Task<string> GetScreenAsync(CancellationToken token) => Task.FromResult("COMBAT");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken token) => Task.FromResult(true);
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken token) => Task.FromResult<byte[]?>(null);
        public Task<string> GetGameDataItemJsonAsync(string collection, string id, CancellationToken token) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> ids, CancellationToken token) => Task.FromResult("[]");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> ids, CancellationToken token) => Task.FromResult("[]");
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken token, bool rawState = false, Action<string>? validateState = null)
        {
            BeforeGate?.Invoke();
            using var lease = Gate.Acquire(action);
            GateHeldForValidation = Gate.IsHeld;
            validateState?.Invoke(State);
            Actions.Add(action);
            Cards.Add(cardIndex);
            State = Safe;
            AfterAct?.Invoke();
            return Task.FromResult("{\"action\":\"" + action + "\",\"status\":\"completed\"}");
        }
    }
}
