using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class RuntimeDebugTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AgentSettings Verified()
    {
        var settings = AgentSettings.CreateDefault();
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!, true));
        return settings;
    }

    public static async Task ConcurrentTestsSendOnlyOneProbeAndCancellationAllowsRetry()
    {
        var client = new ProbeClient { HoldPing = true };
        await using var runtime = new AgentRuntime(Verified(), client);
        var id = runtime.Settings.Models[0].Id;
        using var cancellation = new CancellationTokenSource();
        var first = runtime.TestModelAsync(id, cancellation.Token);
        await client.PingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var busy = await runtime.TestModelAsync(id, CancellationToken.None);
        Assert.Contains("已有模型测试", busy);
        Assert.Equal(1, client.Pings);
        cancellation.Cancel();
        try { await first; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        client.HoldPing = false;
        await runtime.TestModelAsync(id, CancellationToken.None);
        Assert.Equal(2, client.Pings);
        Assert.Equal(1, client.Probes);
        Assert.True(FirstRunSetup.Evaluate(runtime.Settings).ReadyToInvite);
    }

    public static async Task LateProbeCannotReportEditedConfigurationReady()
    {
        var client = new ProbeClient { HoldProbe = true };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), client);
        var id = runtime.Settings.Models[0].Id;
        var test = runtime.TestModelAsync(id, CancellationToken.None);
        await client.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var changed = SettingsClone.Clone(runtime.Settings);
        changed.Endpoints[0].ApiKey = "new-key";
        runtime.SaveSettings(changed);
        client.Release.TrySetResult();
        var result = await test;
        Assert.False(FirstRunSetup.Evaluate(runtime.Settings).ReadyToInvite);
        Assert.False(runtime.IsModelVerified(id));
        Assert.Contains("已变更", result);
        Assert.False(runtime.Settings.ModelTests.Any(t => t.ModelId == id));
    }

    public static async Task UnsupportedToolsStayUnreadyAfterRealRuntimeProbe()
    {
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), new ProbeClient { ToolsSupported = false });
        var result = await runtime.TestModelAsync(runtime.Settings.Models[0].Id, CancellationToken.None);
        Assert.Contains("无法用于自动游玩", result);
        Assert.Equal("tools_unavailable", FirstRunSetup.Evaluate(runtime.Settings).Phase);
        Assert.False(runtime.Settings.Models[0].SupportsTools);
        Assert.False(runtime.Settings.HasSeenFirstRunGuide);
        Assert.Equal("unsupported", runtime.ReadPersistedSettings().ModelTests.Single().Tools);
    }

    public static async Task AuthenticationFailureDoesNotBecomeUnsupportedTools()
    {
        var client = new ProbeClient { PingError = new LlmException("HTTP 401 unauthorized", 401) };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), client);
        await runtime.TestModelAsync(runtime.Settings.Models[0].Id, CancellationToken.None);
        Assert.Equal(0, client.Probes);
        Assert.Equal("failed", FirstRunSetup.Evaluate(runtime.Settings).Phase);
        Assert.True(runtime.Settings.Models[0].SupportsTools);
        Assert.Equal("unknown", runtime.ReadPersistedSettings().ModelTests.Single().Tools);
    }

    public static async Task DisabledEndpointDiscardsOutstandingProbe()
    {
        var client = new ProbeClient { HoldProbe = true };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), client);
        var id = runtime.Settings.Models[0].Id;
        var test = runtime.TestModelAsync(id, CancellationToken.None);
        await client.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var changed = SettingsClone.Clone(runtime.Settings);
        changed.Endpoints[0].Enabled = false;
        runtime.SaveSettings(changed);
        client.Release.TrySetResult();
        Assert.Contains("已变更", await test);
        Assert.Equal(0, runtime.Settings.ModelTests.Count);
        Assert.False(FirstRunSetup.Evaluate(runtime.Settings).ReadyToInvite);
    }

    public static async Task LateProbeCannotRecreateRemovedModelRecords()
    {
        var client = new ProbeClient { HoldProbe = true };
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), client);
        var oldId = runtime.Settings.Models[0].Id;
        var test = runtime.TestModelAsync(oldId, CancellationToken.None);
        await client.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var changed = SettingsClone.Clone(runtime.Settings);
        changed.Models[0].Id = "replacement";
        changed.ConversationModelId = "replacement";
        runtime.SaveSettings(changed);
        client.Release.TrySetResult();
        await test;
        Assert.False(runtime.Settings.ModelTests.Any(t => t.ModelId == oldId));
        Assert.False(FirstRunSetup.Evaluate(runtime.Settings).ReadyToInvite);
    }

    public static async Task FaultedPausedLoopCanApplyCorrectedSettings()
    {
        await using var runtime = new AgentRuntime(Verified(), new ProbeClient());
        var fault = runtime.BeginLoop(_ => Task.FromException(new IOException("simulated prior loop failure")));
        try { await fault; } catch (IOException) { }
        Assert.Equal("paused", runtime.PlayPhase);
        var next = SettingsClone.Clone(runtime.Settings);
        next.MaxSessionRequests = 17;
        await runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(next), CancellationToken.None);
        Assert.Equal(17, runtime.ReadPersistedSettings().MaxSessionRequests);
        Assert.Equal("paused", runtime.PlayPhase);
        Assert.Equal(1, runtime.Starts);
    }

    public static async Task PauseWaitsForFailingActionAndRetainsItsOriginalFailure()
    {
        var session = new AutoPlaySession();
        var entered = Signal(); var release = Signal();
        var loop = session.TryStart(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            throw new IOException("simulated action failure");
        }, CancellationToken.None)!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pause = session.RequestPause();
        Assert.False(pause.IsCompleted);
        release.TrySetResult();
        await pause.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paused", session.Phase);
        Assert.True(loop.IsFaulted);
        try { await loop; throw new Exception("The original failure must remain observable"); }
        catch (IOException) { }
    }

    public static async Task SettingsWaitForSubmittedActionBeforeSavingAndResuming()
    {
        await using var runtime = new AgentRuntime(Verified(), new ProbeClient());
        var entered = Signal();
        var release = Signal();
        var loop = runtime.BeginLoop(async _ => { entered.TrySetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = SettingsClone.Clone(runtime.Settings);
        next.MaxSessionRequests = 19;
        var apply = runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(next), CancellationToken.None);
        try
        {
            Assert.Equal("stopping", runtime.PlayPhase);
            Assert.Equal(0, runtime.Saves);
            Assert.False(apply.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await apply.WaitAsync(TimeSpan.FromSeconds(5));
        await loop;
        Assert.Equal(19, runtime.ReadPersistedSettings().MaxSessionRequests);
        Assert.Equal(2, runtime.Starts);
        Assert.True(runtime.PlayRunning);
    }

    public static async Task CancelledSettingsDoNotApplyOrResumeAfterActionEnds()
    {
        await using var runtime = new AgentRuntime(Verified(), new ProbeClient());
        var entered = Signal(); var release = Signal();
        var loop = runtime.BeginLoop(async _ => { entered.TrySetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var next = SettingsClone.Clone(runtime.Settings); next.MaxSessionRequests = 2;
        var apply = runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(next), cancellation.Token);
        cancellation.Cancel();
        try { await apply; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        finally { release.TrySetResult(); }
        await loop;
        Assert.Equal(0, runtime.Saves);
        Assert.Equal(30, runtime.Settings.MaxSessionRequests);
        Assert.Equal(1, runtime.Starts);
    }

    public static async Task FailedPersistencePreservesOldSettingsAndLeavesLoopPaused()
    {
        await using var runtime = new AgentRuntime(Verified(), new ProbeClient());
        runtime.BeforeSave = () => throw new IOException("simulated read-only settings file");
        var next = SettingsClone.Clone(runtime.Settings); next.MaxSessionRequests = 2;
        try
        {
            await runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(next), CancellationToken.None);
            throw new Exception("Expected save failure");
        }
        catch (IOException) { }
        Assert.Equal(30, runtime.Settings.MaxSessionRequests);
        Assert.False(runtime.PlayRunning);
    }

    public static void KeyRotationPreservesExplicitProtocolAndThinkingMode()
    {
        var settings = QuickSetup.Apply(AgentSettings.CreateDefault(), 0, "old-key", "deepseek-chat", "");
        settings.Endpoints[0].ThinkingProtocol = "prompt";
        settings.Models[0].ThinkingMode = "off";
        var next = QuickSetup.Apply(settings, 0, "new-key", "deepseek-chat", "");
        Assert.Equal("prompt", next.Endpoints[0].ThinkingProtocol);
        Assert.Equal("off", next.Models[0].ThinkingMode);
        var switched = QuickSetup.Apply(settings, 1, "new-key", "gpt-5", "");
        Assert.Equal("auto", switched.Endpoints[0].ThinkingProtocol);
        Assert.Equal("auto", switched.Models[0].ThinkingMode);
    }

    private sealed class ProbeClient : ILlmClient, ILlmClientFactory
    {
        public bool HoldPing { get; set; }
        public bool HoldProbe { get; set; }
        public bool ToolsSupported { get; set; } = true;
        public Exception? PingError { get; set; }
        public int Pings { get; private set; }
        public int Probes { get; private set; }
        public TaskCompletionSource PingEntered { get; } = Signal();
        public TaskCompletionSource ProbeEntered { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("No game requests expected");
        public async Task<string> PingAsync(string model, CancellationToken cancellationToken)
        {
            Pings++; PingEntered.TrySetResult();
            if (PingError != null) throw PingError;
            if (HoldPing) await Release.Task.WaitAsync(cancellationToken);
            return "pong";
        }
        public async Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken)
        {
            Probes++; ProbeEntered.TrySetResult();
            if (HoldProbe) await Release.Task.WaitAsync(cancellationToken);
            return ToolsSupported;
        }
    }
}
