using System.Net;
using System.Text;
using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// W7 provider capabilities: the per-model test persists what a route can stream, the play loop reads
/// it back, a live truncation writes the same verdict once, and the waiting clock restarts on evidence
/// instead of stopping a busy game. Everything here is offline: injected handlers and fakes, no
/// provider calls.
/// </summary>
internal static class ProviderCapabilityTests
{
    // --- Persistence and invalidation ---------------------------------------------------------

    public static void CapabilityPersistsPerRouteAndInvalidatesOnEdit()
    {
        var settings = AgentSettings.CreateDefault();
        var resolved = settings.TryResolvePlayModel()!;
        Assert.Equal(ProviderCapability.Unknown, ProviderCapability.Find(settings, resolved)?.StreamToolArgs ?? ProviderCapability.Unknown);
        Assert.False(ProviderCapability.StreamToolArgsUnreliable(settings, resolved));

        Assert.True(ProviderCapability.MarkStreamToolArgsUnsupported(settings, resolved));
        Assert.True(ProviderCapability.StreamToolArgsUnreliable(settings, resolved));
        var record = ProviderCapability.Find(settings, resolved)!;
        Assert.Equal(ProviderCapability.Unsupported, record.StreamToolArgs);
        Assert.Equal(ProviderCapability.ProbeVersion, record.ProbeVersion);
        Assert.NotNull(record.TestedAt);
        Assert.Equal("unverified", record.Status, "a learned stream limit must not promote an untested route");

        // One write, not one per turn.
        Assert.False(ProviderCapability.MarkStreamToolArgsUnsupported(settings, resolved));

        // The fingerprint covers the key: editing the route must not keep a stale verdict.
        settings.Endpoints[0].ApiKey = "different-key";
        var edited = settings.TryResolvePlayModel()!;
        Assert.Null(ProviderCapability.Find(settings, edited));
        Assert.False(ProviderCapability.StreamToolArgsUnreliable(settings, edited));

        // The output-cap field is read from the same record.
        record = new ModelTestRecord
        {
            ModelId = edited.Model.Id,
            Fingerprint = ModelRoleProbe.Fingerprint(edited),
            StreamToolArgs = ProviderCapability.Supported,
            MaxTokensField = MaxTokensField.CompletionTokens,
            ProbeVersion = ProviderCapability.ProbeVersion
        };
        settings.ModelTests.RemoveAll(item => item.ModelId == edited.Model.Id);
        settings.ModelTests.Add(record);
        Assert.Equal(MaxTokensField.CompletionTokens, ProviderCapability.MaxTokensFieldFor(settings, edited));
        Assert.False(ProviderCapability.StreamToolArgsUnreliable(settings, edited));

        // Junk read from a hand-edited file is normalised, and 0/negative output caps mean "not sent".
        record.StreamToolArgs = " Sometimes ";
        record.MaxTokensField = "max_new_tokens";
        record.ProbeVersion = -3;
        settings.MaxOutputTokens = 0;
        settings.EnsureValidShape();
        Assert.Equal(ProviderCapability.Unknown, record.StreamToolArgs);
        Assert.Null(record.MaxTokensField);
        Assert.Equal(0, record.ProbeVersion);
        Assert.Null(settings.MaxOutputTokens);
        Assert.Equal(MaxTokensField.Standard, ProviderCapability.MaxTokensFieldFor(settings, edited));
    }

    public static void CapabilitySurvivesTheSettingsFile()
    {
        // A repo-relative store, so this test still runs where the sandbox denies the system temp
        // directory: it is about serialization, not about the file system's permission model.
        var directory = Path.Combine(AppContext.BaseDirectory, "capability-store-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = AgentSettings.CreateDefault();
            settings.MaxOutputTokens = 4096;
            var resolved = settings.TryResolvePlayModel()!;
            ProviderCapability.MarkStreamToolArgsUnsupported(settings, resolved);
            var record = ProviderCapability.Find(settings, resolved)!;
            record.MaxTokensField = MaxTokensField.CompletionTokens;

            new SettingsStore(path).Save(settings);
            var loaded = new SettingsStore(path).Load();
            var persisted = ProviderCapability.Find(loaded, loaded.TryResolvePlayModel()!)!;
            Assert.Equal(ProviderCapability.Unsupported, persisted.StreamToolArgs);
            Assert.Equal(MaxTokensField.CompletionTokens, persisted.MaxTokensField);
            Assert.Equal(ProviderCapability.ProbeVersion, persisted.ProbeVersion);
            Assert.Equal(4096, loaded.MaxOutputTokens);
            Assert.True(ProviderCapability.StreamToolArgsUnreliable(loaded, loaded.TryResolvePlayModel()!));
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static void CloneAndCompanionPayloadCarryTheCapabilityAndOutputCap()
    {
        var source = AgentSettings.CreateDefault();
        source.MaxOutputTokens = 2048;
        var resolved = source.TryResolvePlayModel()!;
        source.ModelTests.Add(new ModelTestRecord
        {
            ModelId = resolved.Model.Id,
            Fingerprint = ModelRoleProbe.Fingerprint(resolved),
            Status = "verified",
            Tools = "supported",
            StreamToolArgs = ProviderCapability.Unsupported,
            MaxTokensField = MaxTokensField.CompletionTokens,
            ProbeVersion = ProviderCapability.ProbeVersion,
            TestedAt = "2026-10-29T00:00:00Z"
        });

        var clone = SettingsClone.Clone(source);
        Assert.Equal(2048, clone.MaxOutputTokens);
        var cloned = clone.ModelTests.Single();
        Assert.Equal(ProviderCapability.Unsupported, cloned.StreamToolArgs);
        Assert.Equal(MaxTokensField.CompletionTokens, cloned.MaxTokensField);
        Assert.Equal(ProviderCapability.ProbeVersion, cloned.ProbeVersion);

        // The teammate plays by the same output cap and the same learned route verdict.
        var wire = JsonSerializer.Serialize(CompanionPlaySettings.From(source));
        var received = JsonSerializer.Deserialize<CompanionPlaySettings>(wire)!;
        Assert.True(received.IsValid);
        var companion = AgentSettings.CreateDefault();
        received.ApplyTo(companion);
        Assert.Equal(2048, companion.MaxOutputTokens);
        Assert.Equal(ProviderCapability.Unsupported, companion.ModelTests.Single().StreamToolArgs);
        Assert.Equal(MaxTokensField.CompletionTokens, companion.ModelTests.Single().MaxTokensField);
    }

    // --- The streaming probe ------------------------------------------------------------------

    public static void ProbeArgumentsCompleteOnlyWithEveryIndex()
    {
        var tool = ProbeTool();
        var complete = ToolProbeArgs.Evaluate(
            Completion("""{"action":"play_card","card_index":0,"target_index":0}"""),
            tool.Name,
            new[] { "action", "card_index", "target_index" });
        Assert.Equal(StreamingToolArgsVerdict.Complete, complete.Verdict);

        var truncated = ToolProbeArgs.Evaluate(
            Completion("""{"action":"play_card"}"""),
            tool.Name,
            new[] { "action", "card_index", "target_index" });
        Assert.Equal(StreamingToolArgsVerdict.Incomplete, truncated.Verdict);
        Assert.Contains("card_index", truncated.Error!);

        // A zero index is a real value; only null/blank counts as missing.
        var zero = ToolProbeArgs.Evaluate(
            Completion("""{"action":"play_card","card_index":0,"target_index":false}"""),
            tool.Name,
            new[] { "action", "card_index", "target_index" });
        Assert.Equal(StreamingToolArgsVerdict.Complete, zero.Verdict);

        var noCall = ToolProbeArgs.Evaluate(new LlmCompletion(), tool.Name, new[] { "action" });
        Assert.Equal(StreamingToolArgsVerdict.NoToolCall, noCall.Verdict);

        var broken = ToolProbeArgs.Evaluate(
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "x", Name = "act", ArgumentsJson = "{\"action\":" } } },
            tool.Name,
            new[] { "action" });
        Assert.Equal(StreamingToolArgsVerdict.Incomplete, broken.Verdict);
    }

    public static async Task StreamingProbeReadsTruncationOffTheWire()
    {
        var truncated = await ProbeOverSseAsync(
            SseToolArgs("""{"action":"play_card"}""") + "data: [DONE]\n");
        Assert.Equal(StreamingToolArgsVerdict.Incomplete, truncated.Verdict);
        Assert.Contains("play_card", truncated.ArgumentsJson!);
        Assert.Equal(MaxTokensField.Standard, truncated.MaxTokensParameter);

        // The same arguments split across two deltas must accumulate, not overwrite.
        var complete = await ProbeOverSseAsync(
            SseToolArgs("""{"action":"play_card",""")
            + SseToolArgs("\"card_index\":0,\"target_index\":0}")
            + "data: [DONE]\n");
        Assert.Equal(StreamingToolArgsVerdict.Complete, complete.Verdict);
    }

    public static async Task StreamingProbeKeepsTheRequestOnTheStreamPath()
    {
        var handler = new CapturingHandler(
            SseToolArgs("""{"action":"play_card"}""") + "data: [DONE]\n");
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleClient(new LlmEndpoint { BaseUrl = "https://example.test/v1" }, httpClient: http);
        await client.ProbeStreamingToolCallAsync(
            "m", ProbeTool(), "Call act.", new[] { "action", "card_index" }, CancellationToken.None);

        Assert.Equal(1, handler.Bodies.Count);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean(), "the probe must measure the stream path");
        Assert.Equal("required", body.RootElement.GetProperty("tool_choice").GetString());
        Assert.Equal(300, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    // --- Output cap on a play request ---------------------------------------------------------

    public static async Task OutputCapUsesTheCapabilityFieldName()
    {
        var handler = new CapturingHandler(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleClient(new LlmEndpoint { BaseUrl = "https://example.test/v1" }, httpClient: http);

        await client.CompleteAsync(new LlmRequest
        {
            Model = "m",
            Messages = new[] { LlmMessage.User("hi") },
            Stream = false,
            MaxTokens = 8192,
            MaxTokensParameter = MaxTokensField.CompletionTokens
        }, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(8192, body.RootElement.GetProperty(MaxTokensField.CompletionTokens).GetInt32());
        Assert.False(body.RootElement.TryGetProperty(MaxTokensField.Standard, out _),
            "the probe already learnt which spelling this route accepts");

        // Null means "send no cap", which is how a provider with its own default is left alone.
        await client.CompleteAsync(new LlmRequest
        {
            Model = "m",
            Messages = new[] { LlmMessage.User("hi") },
            Stream = false
        }, CancellationToken.None);
        using var uncapped = JsonDocument.Parse(handler.Bodies[^1]);
        Assert.False(uncapped.RootElement.TryGetProperty(MaxTokensField.Standard, out _));
        Assert.False(uncapped.RootElement.TryGetProperty(MaxTokensField.CompletionTokens, out _));
    }

    // --- The model test records the verdict ---------------------------------------------------

    public static async Task ModelTestPersistsTheStreamVerdict()
    {
        var client = new ProbeClient
        {
            StreamResult = new StreamingToolArgsProbeResult(
                StreamingToolArgsVerdict.Incomplete,
                """{"action":"play_card"}""",
                MaxTokensField.Standard,
                "streamed arguments are missing card_index")
        };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), new FixedFactory(client));
        var id = runtime.Settings.Models[0].Id;

        var message = await runtime.TestModelAsync(id, CancellationToken.None);
        Assert.Contains("非流式", message);

        // The live record is written before the save, so a sandbox that denies the temp settings file
        // still proves the verdict itself; the persisted copy is the second assertion below.
        var live = runtime.Settings.ModelTests.Single();
        Assert.Equal(ProviderCapability.Unsupported, live.StreamToolArgs);
        Assert.Equal(ProviderCapability.ProbeVersion, live.ProbeVersion);

        var persisted = runtime.ReadPersistedSettings().ModelTests.Single();
        Assert.Equal("verified", persisted.Status);
        Assert.Equal("supported", persisted.Tools);
        Assert.Equal(ProviderCapability.Unsupported, persisted.StreamToolArgs);
        Assert.Equal(ProviderCapability.ProbeVersion, persisted.ProbeVersion);
        Assert.Equal(MaxTokensField.Standard, persisted.MaxTokensField);
        Assert.True(runtime.IsModelVerified(id));
    }

    public static async Task ModelTestLeavesTheCapabilityUnknownWhenTheProbeCannotRun()
    {
        // A fake that only implements ILlmClient: the streaming probe is not attempted, so nothing is
        // claimed. This is the shape every older test fake has.
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), new FixedFactory(new PlainProbeClient()));
        await runtime.TestModelAsync(runtime.Settings.Models[0].Id, CancellationToken.None);
        var persisted = runtime.ReadPersistedSettings().ModelTests.Single();
        Assert.Equal(ProviderCapability.Unknown, persisted.StreamToolArgs);
        Assert.Equal(0, persisted.ProbeVersion);
    }

    public static async Task ModelTestRecordsTheCompletionTokensField()
    {
        var client = new ProbeClient
        {
            StreamResult = new StreamingToolArgsProbeResult(
                StreamingToolArgsVerdict.MaxTokensRejected,
                MaxTokensParameter: MaxTokensField.CompletionTokens,
                Error: "Unsupported parameter: 'max_tokens'")
        };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), new FixedFactory(client));
        await runtime.TestModelAsync(runtime.Settings.Models[0].Id, CancellationToken.None);

        var persisted = runtime.ReadPersistedSettings().ModelTests.Single();
        Assert.Equal(ProviderCapability.Unknown, persisted.StreamToolArgs);
        Assert.Equal(MaxTokensField.CompletionTokens, persisted.MaxTokensField);
        Assert.Equal(ProviderCapability.ProbeVersion, persisted.ProbeVersion);
    }

    // --- The loop reads the persisted verdict -------------------------------------------------

    public static void LoopReadsThePersistedVerdict()
    {
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(new UnusedBridge(), new ThrowingFactory(), () => settings);
        var resolved = settings.TryResolvePlayModel()!;
        Assert.False(loop.StreamToolArgsUnreliable(resolved));
        Assert.Equal(MaxTokensField.Standard, loop.MaxTokensParameter(resolved));

        var record = new ModelTestRecord
        {
            ModelId = resolved.Model.Id,
            Fingerprint = ModelRoleProbe.Fingerprint(resolved),
            StreamToolArgs = ProviderCapability.Unsupported,
            MaxTokensField = MaxTokensField.CompletionTokens,
            ProbeVersion = ProviderCapability.ProbeVersion
        };
        settings.ModelTests.Add(record);
        Assert.True(loop.StreamToolArgsUnreliable(resolved),
            "a probed route must not pay for one truncated stream per turn again");
        Assert.Equal(MaxTokensField.CompletionTokens, loop.MaxTokensParameter(resolved));
    }

    /// <summary>
    /// A route whose tool arguments were cut even without streaming starts its act turns as reply JSON
    /// in the next session; chat on the same route keeps its tools; and a fresh model test (a new
    /// record) gives a fixed gateway its tools back.
    /// </summary>
    public static void LoopReadsThePersistedToolArgsVerdict()
    {
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(new UnusedBridge(), new ThrowingFactory(), () => settings);
        var resolved = settings.TryResolvePlayModel()!;
        Assert.Equal(ActChannel.StreamedTools, loop.ActChannelFor(resolved, allowAct: true));

        Assert.True(ProviderCapability.MarkStreamToolArgsUnsupported(settings, resolved));
        Assert.Equal(ActChannel.Tools, loop.ActChannelFor(resolved, allowAct: true));

        Assert.True(ProviderCapability.MarkToolArgsUnsupported(settings, resolved));
        Assert.False(ProviderCapability.MarkToolArgsUnsupported(settings, resolved), "the same fact is not written twice");
        Assert.Equal(ActChannel.ReplyJson, loop.ActChannelFor(resolved, allowAct: true));
        Assert.Equal(ActChannel.StreamedTools, loop.ActChannelFor(resolved, allowAct: false),
            "only acts are cut short; chat keeps its read tools");
        Assert.Equal(ProviderCapability.Unsupported, SettingsClone.Clone(settings).ModelTests.Single().ToolArgs);

        settings.ModelTests.Clear();
        settings.ModelTests.Add(new ModelTestRecord
        {
            ModelId = resolved.Model.Id,
            Fingerprint = ModelRoleProbe.Fingerprint(resolved),
            Status = "verified",
            Tools = "supported"
        });
        Assert.Equal(ActChannel.StreamedTools, loop.ActChannelFor(resolved, allowAct: true));

        var noTools = AgentSettings.CreateDefault();
        noTools.Models[0].SupportsTools = false;
        var plain = new AgentLoop(new UnusedBridge(), new ThrowingFactory(), () => noTools);
        Assert.Equal(ActChannel.ReplyJson, plain.ActChannelFor(noTools.TryResolvePlayModel()!, allowAct: true));
    }

    // --- Evidence clock -----------------------------------------------------------------------

    public static void WaitingClockRestartsOnNewEvidence()
    {
        var clock = typeof(AutoPlayRecovery).GetField("_waitingForGameSince",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var longAgo = DateTimeOffset.UtcNow - NoProgressPolicy.WaitingForGameLimit - TimeSpan.FromSeconds(1);
        var waitingA = new AgentTurnResult { WaitingForGame = true, StateFingerprint = "screen-A" };
        var waitingB = new AgentTurnResult { WaitingForGame = true, StateFingerprint = "screen-B" };

        // Unchanged evidence keeps the old clock: a real hang still stops.
        var stuck = new AutoPlayRecovery();
        Assert.Null(stuck.Observe(waitingA).StopReason);
        clock.SetValue(stuck, longAgo);
        Assert.NotNull(stuck.Observe(waitingA).StopReason);

        // New evidence is progress: the screen/phase/fingerprint moved, so the 150 s window restarts.
        var healthy = new AutoPlayRecovery();
        Assert.Null(healthy.Observe(waitingA).StopReason);
        clock.SetValue(healthy, longAgo);
        Assert.Null(healthy.Observe(waitingB).StopReason);
        var restarted = (DateTimeOffset)clock.GetValue(healthy)!;
        Assert.True(DateTimeOffset.UtcNow - restarted < TimeSpan.FromSeconds(5),
            "a new fingerprint must restart the stuck clock");

        // The restarted window still ends if the game then stops producing evidence.
        clock.SetValue(healthy, longAgo);
        Assert.NotNull(healthy.Observe(waitingB).StopReason);
    }

    // --- Fakes --------------------------------------------------------------------------------

    private static LlmTool ProbeTool() => new()
    {
        Name = "act",
        Description = "Do one game action.",
        Parameters = new { type = "object" }
    };

    private static LlmCompletion Completion(string arguments) => new()
    {
        ToolCalls = new[] { new LlmToolCall { Id = "c", Name = "act", ArgumentsJson = arguments } }
    };

    /// <summary>One SSE delta carrying an <c>act</c> tool-call arguments fragment.</summary>
    private static string SseToolArgs(string fragment) =>
        "data: " + JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    delta = new
                    {
                        tool_calls = new[]
                        {
                            new
                            {
                                index = 0,
                                id = "c1",
                                function = new { name = "act", arguments = fragment }
                            }
                        }
                    }
                }
            }
        }) + "\n";

    private static async Task<StreamingToolArgsProbeResult> ProbeOverSseAsync(string sse)
    {
        using var http = new HttpClient(new CapturingHandler(sse));
        IStreamingToolProbe client = new OpenAiCompatibleClient(
            new LlmEndpoint { BaseUrl = "https://example.test/v1" }, httpClient: http);
        return await client.ProbeStreamingToolCallAsync(
            "m", ProbeTool(), "Call act.", new[] { "action", "card_index", "target_index" }, CancellationToken.None);
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class FixedFactory(ILlmClient client) : ILlmClientFactory
    {
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => client;
    }

    private sealed class ThrowingFactory : ILlmClientFactory
    {
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) =>
            throw new InvalidOperationException("This test must not dispatch a model request.");
    }

    private sealed class ProbeClient : ILlmClient, IStreamingToolProbe
    {
        public StreamingToolArgsProbeResult StreamResult { get; init; } =
            new(StreamingToolArgsVerdict.Complete, """{"action":"end_turn"}""", MaxTokensField.Standard);

        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");

        public Task<bool> ProbeToolCallingAsync(
            string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A capability test must not play.");

        public Task<StreamingToolArgsProbeResult> ProbeStreamingToolCallAsync(
            string model,
            LlmTool tool,
            string prompt,
            IReadOnlyList<string> requiredFields,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamResult);
    }

    /// <summary>
    /// A fake that only speaks the older <see cref="ILlmClient"/> contract: used to prove that a client
    /// without the streaming probe records nothing rather than a made-up verdict.
    /// </summary>
    private sealed class PlainProbeClient : ILlmClient
    {
        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");

        public Task<bool> ProbeToolCallingAsync(
            string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class UnusedBridge : IGameBridge
    {
        private static Exception Unused() => new InvalidOperationException("This test must not read the game.");

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex,
            int? x, int? y, string? tool, CancellationToken cancellationToken, bool rawState = false,
            Action<string>? validateState = null) => throw Unused();
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => throw Unused();
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => throw Unused();
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => throw Unused();
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => throw Unused();
    }
}
