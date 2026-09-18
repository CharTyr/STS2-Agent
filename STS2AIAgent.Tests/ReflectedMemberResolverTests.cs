using System.Reflection;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// The registry finds the member its entry names, on the type its entry names, and never throws.
/// </summary>
/// <remarks>
/// The fakes reproduce the shape that failed on the first live run: a scene declaring a private
/// <c>Disconnect(NetError)</c> on top of Godot's public <c>GodotObject.Disconnect(StringName,
/// Callable)</c>. Searching base types made that lookup ambiguous, the exception escaped into the
/// registry's <c>Lazy</c>, and the mod lost its probe, <c>/health</c> and every registry-backed action.
/// </remarks>
internal static class ReflectedMemberResolverTests
{
    private class FakeGodotObject
    {
        public void Disconnect(string signal, Action callable)
        {
        }

        protected void Close()
        {
        }
    }

    private sealed class FakeLobbyScene : FakeGodotObject
    {
        private static double _longPressDuration = 0.5;
        private int _players;

        private bool IsReady => _players > 0;

        private void Disconnect(int reason) => _players = reason;

        private void Twice(int value) => _players = value;

        private void Twice(string value) => _players = value.Length + (int)_longPressDuration + (IsReady ? 1 : 0);
    }

    public static void ADeclaredMethodIsFoundDespiteAPublicBaseMethodOfTheSameName()
    {
        // The lookup the registry used to make. It is here so the failure it guards against stays
        // demonstrated rather than remembered.
        var threw = false;
        try
        {
            typeof(FakeLobbyScene).GetMethod(
                "Disconnect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            threw = true;
        }

        Assert.True(threw, "Searching base types is expected to find two Disconnect methods and throw.");

        var method = ReflectedMemberResolver.Method(typeof(FakeLobbyScene), "Disconnect", isStatic: false);
        Assert.True(
            method != null && method.DeclaringType == typeof(FakeLobbyScene) &&
            method.GetParameters().Single().ParameterType == typeof(int),
            "The resolver must find the scene's own private Disconnect(int), not collide with the base's.");
    }

    public static void AmbiguityAndInheritanceResolveToNothingInsteadOfThrowing()
    {
        Assert.True(
            ReflectedMemberResolver.Method(typeof(FakeLobbyScene), "Twice", isStatic: false) == null,
            "Overloads within the declaring type must resolve to null -- a missing member the probe "
            + "reports -- not throw into the registry's Lazy, which would cache the exception.");
        Assert.True(
            ReflectedMemberResolver.Method(typeof(FakeLobbyScene), "Close", isStatic: false) == null,
            "A member declared on a base type is not the member the entry names; the entry has the "
            + "wrong declaring type and the probe should say so.");
        Assert.True(
            ReflectedMemberResolver.Field(typeof(FakeLobbyScene), "_noSuchField", isStatic: false) == null,
            "An absent member resolves to null.");
    }

    public static void StaticnessIsPartOfTheLookup()
    {
        Assert.True(
            ReflectedMemberResolver.Field(typeof(FakeLobbyScene), "_longPressDuration", isStatic: true) != null,
            "A static field is found when the entry says static.");
        Assert.True(
            ReflectedMemberResolver.Field(typeof(FakeLobbyScene), "_longPressDuration", isStatic: false) == null,
            "...and not when it does not: that mismatch is how end_turn read 0.45 instead of 0.5.");
        Assert.True(
            ReflectedMemberResolver.Field(typeof(FakeLobbyScene), "_players", isStatic: false) != null &&
            ReflectedMemberResolver.Property(typeof(FakeLobbyScene), "IsReady", isStatic: false) != null,
            "Private instance fields and properties are found on their declaring type.");
    }

    public static void TheRegistryResolvesThroughTheResolver()
    {
        var resolve = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(
                AgentSourceFixture.Read("STS2AIAgent/Game/ReflectedGameMembers.cs"),
                "private static MemberInfo? Resolve(Entry entry)"));
        foreach (var kind in new[] { "Field", "Method", "Property" })
        {
            Assert.True(
                resolve.Contains($"MemberKind.{kind}=>ReflectedMemberResolver.{kind}(entry.DeclaringType,entry.MemberName,entry.Static)", StringComparison.Ordinal),
                $"ReflectedGameMembers must resolve {kind} entries through ReflectedMemberResolver.{kind}.");
        }

        Assert.False(
            resolve.Contains(".GetField(", StringComparison.Ordinal) ||
            resolve.Contains(".GetMethod(", StringComparison.Ordinal) ||
            resolve.Contains(".GetProperty(", StringComparison.Ordinal),
            "ReflectedGameMembers must not look members up itself; the resolver's flags and its "
            + "no-throw guard are the ones the live run proved necessary.");
    }
}
