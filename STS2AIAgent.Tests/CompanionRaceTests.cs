using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class CompanionRaceTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AgentSettings Verified()
    {
        var settings = AgentSettings.CreateDefault();
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!, true));
        return settings;
    }

    public static async Task PauseDuringSettingsUpdateDoesNotRestartTheLoop()
    {
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        var entered = Signal(); var release = Signal();
        var loop = runtime.BeginLoop(async _ => { entered.TrySetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var settings = SettingsClone.Clone(runtime.Settings); settings.MaxSessionRequests = 17;
        var apply = runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(settings), CancellationToken.None);
        var pause = runtime.SetCompanionRunningAsync(false, CancellationToken.None);
        Assert.False(pause.IsCompleted);
        release.TrySetResult();
        await apply.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paused", await pause.WaitAsync(TimeSpan.FromSeconds(5)));
        await loop;
        Assert.Equal(1, runtime.Starts, "An explicit pause must not allow an intermediate automatic restart.");
        Assert.Equal(17, runtime.ReadPersistedSettings().MaxSessionRequests);
    }

    public static async Task AStoppingSessionIsNotResumedBySavingSettings()
    {
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        var entered = Signal(); var release = Signal();
        var loop = runtime.BeginLoop(async _ => { entered.TrySetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pause = runtime.RequestPause();
        var settings = SettingsClone.Clone(runtime.Settings); settings.MaxSessionRequests = 18;
        var apply = runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(settings), CancellationToken.None);
        release.TrySetResult();
        await apply.WaitAsync(TimeSpan.FromSeconds(5));
        await pause; await loop;
        Assert.Equal(1, runtime.Starts);
        Assert.Equal("paused", runtime.PlayPhase);
    }

    public static async Task CancellationDuringPersistenceDoesNotRestartTheLoop()
    {
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        var entered = Signal();
        var loop = runtime.BeginLoop(async token => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        runtime.BeforeSave = cancellation.Cancel;
        var settings = SettingsClone.Clone(runtime.Settings); settings.MaxSessionRequests = 19;
        try
        {
            await runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(settings), cancellation.Token);
            throw new Exception("Expected cancellation after persistence, before any automatic restart");
        }
        catch (OperationCanceledException) { }
        await loop;
        Assert.Equal(19, runtime.ReadPersistedSettings().MaxSessionRequests);
        Assert.Equal(1, runtime.Starts);
        Assert.Equal("paused", runtime.PlayPhase);
    }

    public static async Task CaseInsensitiveBindingsWorkOnBothHostAndCompanion()
    {
        var settings = Verified();
        settings.Models[0].EndpointId = settings.Endpoints[0].Id.ToUpperInvariant();
        settings.ConversationModelId = settings.Models[0].Id.ToUpperInvariant();
        Assert.True(FirstRunSetup.Evaluate(settings).ReadyToInvite);
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        await runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(settings), CancellationToken.None);
        Assert.True(FirstRunSetup.Evaluate(runtime.ReadPersistedSettings()).ReadyToInvite);
    }

    public static async Task UnverifiedModelCanBePausedButCannotResume()
    {
        await using var runtime = new AgentRuntime(AgentSettings.CreateDefault(), new NoModelRequests());
        var entered = Signal();
        var loop = runtime.BeginLoop(async token => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paused", await runtime.SetCompanionRunningAsync(false, CancellationToken.None));
        await loop;
        try
        {
            await runtime.SetCompanionRunningAsync(true, CancellationToken.None);
            throw new Exception("An unverified model must not resume");
        }
        catch (InvalidOperationException) { }
        Assert.Equal(1, runtime.Starts);
    }

    public static async Task ExplicitResumeWorksAfterPauseAndSettingsUpdate()
    {
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        Assert.Equal("paused", await runtime.SetCompanionRunningAsync(false, CancellationToken.None));
        var settings = SettingsClone.Clone(runtime.Settings); settings.MaxSessionRequests = 21;
        await runtime.ApplyCompanionSettingsAsync(CompanionSettingsPatch.From(settings), CancellationToken.None);
        Assert.Equal(0, runtime.Starts);
        Assert.Equal("running", await runtime.SetCompanionRunningAsync(true, CancellationToken.None));
        Assert.Equal(1, runtime.Starts);
        Assert.Equal("paused", await runtime.SetCompanionRunningAsync(false, CancellationToken.None));
    }

    public static async Task AlreadyCancelledPauseDoesNotChangeRunningState()
    {
        await using var runtime = new AgentRuntime(Verified(), new NoModelRequests());
        var entered = Signal();
        _ = runtime.BeginLoop(async token => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try
        {
            await runtime.SetCompanionRunningAsync(false, cancellation.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Assert.Equal("running", runtime.PlayPhase);
        Assert.False(runtime.AutoStartSuppressed);
    }

    private sealed class NoModelRequests : ILlmClientFactory
    {
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) =>
            throw new Exception("Settings and pause controls must not call a model.");
    }
}
