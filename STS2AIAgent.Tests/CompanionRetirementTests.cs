using STS2AIAgent.Agent;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class CompanionRetirementTests
{
    public static void DisplayRequiresLiveConnectedTeammateOutsideRoot()
    {
        Assert.True(CompanionRetirement.ShowTeamControls(false, true, true));
        Assert.False(CompanionRetirement.ShowTeamControls(true, true, true));
        Assert.False(CompanionRetirement.ShowTeamControls(false, false, true));
        Assert.False(CompanionRetirement.ShowTeamControls(false, true, false));
        Assert.False(CompanionRetirement.ShowTeamControls(true, false, false));
    }

    public static async Task CloseWaitsForExitAndSerializesConcurrentAttempts()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleared = 0;
        var first = CompanionRetirement.TryRetireAsync(gate,
            _ => Task.FromResult(CompanionCloseStart.Closing),
            _ => { waiting.SetResult(); return exited.Task; },
            () => cleared++, TimeSpan.FromSeconds(2), CancellationToken.None);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = await CompanionRetirement.TryRetireAsync(gate,
            _ => throw new Exception("busy attempt must not close"),
            _ => throw new Exception("busy attempt must not wait"),
            () => throw new Exception("busy attempt must not clear"),
            TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(CompanionRetirementResult.Busy, second);
        Assert.Equal(0, cleared);
        exited.SetResult();
        Assert.Equal(CompanionRetirementResult.Retired, await first);
        Assert.Equal(1, cleared);
    }

    public static async Task UnsafeAndTimedOutClosesNeverClearOrLaunch()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var waited = 0;
        var cleared = 0;
        foreach (var start in new[] { CompanionCloseStart.NotAtRoot, CompanionCloseStart.Failed })
        {
            var result = await CompanionRetirement.TryRetireAsync(gate,
                _ => Task.FromResult(start), _ => { waited++; return Task.CompletedTask; },
                () => cleared++, TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Equal(start == CompanionCloseStart.NotAtRoot
                ? CompanionRetirementResult.NotAtRoot : CompanionRetirementResult.Failed, result);
        }
        var timedOut = await CompanionRetirement.TryRetireAsync(gate,
            _ => Task.FromResult(CompanionCloseStart.Closing),
            token => Task.Delay(Timeout.InfiniteTimeSpan, token),
            () => cleared++, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Equal(CompanionRetirementResult.TimedOut, timedOut);
        Assert.Equal(0, waited);
        Assert.Equal(0, cleared);
        var retry = await CompanionRetirement.TryRetireAsync(gate,
            _ => Task.FromResult(CompanionCloseStart.AlreadyExited),
            _ => throw new Exception("exited child must not be awaited"),
            () => cleared++, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(CompanionRetirementResult.Retired, retry);
        Assert.Equal(1, cleared);
    }

    public static void GameWiringProtectsTheOwnedChildAndRootMenu()
    {
        var launcher = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs");
        var close = AgentSourceFixture.MethodBody(launcher, "RequestCloseAtRoot");
        Assert.Contains("IsRootMainMenu", close);
        Assert.Contains("ReferenceEquals(process, _companionProcess)", close);
        Assert.Contains("process.Id == System.Environment.ProcessId", close);
        Assert.Contains("process.CloseMainWindow()", close);
        Assert.False(close.Contains(".Kill(", StringComparison.Ordinal));
        var prepare = AgentSourceFixture.MethodBody(launcher, "PrepareForNewSessionAsync");
        Assert.Contains("LaunchGate", prepare);
        Assert.Contains("WaitForExitAsync(token)", prepare);
        Assert.Contains("InvokeAsync<CompanionCloseStart>", prepare);
        var root = AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "IsRootMainMenu");
        Assert.Contains("SubmenusOpen: false", root);
        Assert.Contains("GetOpenModal() == null", root);
        var runtime = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs"), "LaunchDualInstanceCoreAsync");
        Assert.Contains("!atRootMenu && !continueRun && LocalDualInstanceLauncher.CanReconnect", runtime);
        var coordinator = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs");
        Assert.Contains("PrepareForNewSessionAsync(cancellationToken)", coordinator);
        Assert.Contains("RequireRootMainMenu();", coordinator);
        var fastHost = AgentSourceFixture.MethodBody(coordinator, "OpenLocalFourPlayerLobbyAsync");
        Assert.Contains("catch (Exception ex) when (hostStarted && ex is not OperationCanceledException)", fastHost);
        Assert.Contains("RequireRootMainMenu();", fastHost);
        Assert.Contains("hostStarted = true;", fastHost);
        Assert.True(fastHost.IndexOf("RequireRootMainMenu();", StringComparison.Ordinal)
            < fastHost.IndexOf("hostStarted = true;", StringComparison.Ordinal));
        var fallback = AgentSourceFixture.MethodBody(coordinator, "OpenMultiplayerTestAsync");
        Assert.Contains("RequireRootMainMenu();", fallback);
        Assert.True(fallback.IndexOf("RequireRootMainMenu();", StringComparison.Ordinal)
            < fallback.IndexOf("ExecuteInternalConsoleCommandAsync", StringComparison.Ordinal));
        var refresh = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.PlayControl.cs"), "RefreshCoopPhase");
        Assert.Contains("CompanionRetirement.ShowTeamControls", refresh);
        Assert.False(refresh.Contains("PrepareForNewSessionAsync", StringComparison.Ordinal));
    }

    public static void BothMcpAndInGameReceiveCharacterGuidance()
    {
        var section = PlayPrompt.PlaybookGuidance("CHARACTER_SELECT");
        Assert.Contains("explicitly requests", section);
        Assert.Contains("do not silently substitute", section);
        Assert.Contains("already selected and unlocked", section);
        Assert.Contains(section.Trim(), PlayPrompt.ScreenPlaybooks);
    }
}
