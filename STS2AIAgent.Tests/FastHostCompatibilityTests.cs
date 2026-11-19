using System.Reflection;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class FastHostCompatibilityTests
{
    private enum Mode { Daily, Standard }

    public static void NewGameHostsStandardNonCustomOnce()
    {
        var menu = new CurrentMenu();
        Assert.True(ReflectedMemberResolver.TryInvokeStandardFastHost(typeof(CurrentMenu), menu, Mode.Standard, out var result));
        Assert.Equal(1, menu.Calls);
        Assert.Equal(Mode.Standard, menu.LastMode);
        Assert.False(menu.LastCustom);
        Assert.Null(result); // The actual v0.113.1 method returns void.
    }

    public static void OldGameHostsStandardOnce()
    {
        var menu = new LegacyMenu();
        Assert.True(ReflectedMemberResolver.TryInvokeStandardFastHost(typeof(LegacyMenu), menu, Mode.Standard, out var result));
        Assert.Equal(1, menu.Calls);
        Assert.Equal(Mode.Standard, menu.LastMode);
        Assert.True(ReferenceEquals(menu.Completion, result), "A returned task must reach the existing bounded wait.");
    }

    public static void UnknownShapesDoNotDispatch()
    {
        var menu = new UnrecognizedMenu();
        Assert.False(ReflectedMemberResolver.TryInvokeStandardFastHost(typeof(UnrecognizedMenu), menu, Mode.Standard, out var result));
        Assert.Null(result);
    }

    public static void SelectedOverloadIsNotRetriedAfterDispatchFailure()
    {
        var menu = new OverloadedMenu();
        try
        {
            ReflectedMemberResolver.TryInvokeStandardFastHost(typeof(OverloadedMenu), menu, Mode.Standard, out _);
            throw new Exception("The newer overload must be invoked, and its error must be propagated.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            Assert.Equal(1, menu.NewCalls);
            Assert.Equal(0, menu.OldCalls);
        }
    }

    public static void LocalLobbyInvokesTheCompatibilitySeam()
    {
        var source = AgentSourceFixture.ReadActionService();
        var host = AgentSourceFixture.MethodBody(source, "InvokeFastHostAsync");
        Assert.Contains("ReflectedMemberResolver.TryInvokeStandardFastHost", host);
        Assert.Contains("GameMode.Standard", host);
    }

    private sealed class CurrentMenu
    {
        public int Calls { get; private set; }
        public Mode LastMode { get; private set; }
        public bool LastCustom { get; private set; } = true;

        public void FastHost(Mode mode, bool isCustom)
        {
            Calls++;
            LastMode = mode;
            LastCustom = isCustom;
        }
    }

    private sealed class LegacyMenu
    {
        public int Calls { get; private set; }
        public Mode LastMode { get; private set; }
        public Task Completion { get; } = Task.FromResult(42);

        public Task FastHost(Mode mode)
        {
            Calls++;
            LastMode = mode;
            return Completion;
        }
    }

    private sealed class UnrecognizedMenu
    {
        public void FastHost(object mode, bool isCustom) => throw new Exception("An assignable but inexact overload must not run.");
        public void FastHost(Mode mode, int flags) => throw new Exception("An incompatible overload must not run.");
        public void FastHost<T>(Mode mode, bool isCustom) => throw new Exception("An open generic overload must not run.");
        public static void FastHost(Mode mode) => throw new Exception("A static overload must not run.");
    }

    private sealed class OverloadedMenu
    {
        public int NewCalls { get; private set; }
        public int OldCalls { get; private set; }

        public void FastHost(Mode mode) => OldCalls++;

        public void FastHost(Mode mode, bool isCustom)
        {
            NewCalls++;
            throw new InvalidOperationException("Already dispatched.");
        }
    }
}
