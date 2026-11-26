using System.Reflection;

namespace STS2AIAgent.Game;

/// <summary>How <see cref="ReflectedGameMembers"/> finds one member by name, and never throws doing it.</summary>
/// <remarks>
/// Kept apart from the registry because the registry names game types and cannot compile offline.
/// This cannot fail the same way, so the rule that broke on the first live run is tested here.
///
/// **Declared only.** An entry names the type that declares the member; that is what the
/// <c>typeof</c> in it means. Searching base types as well let a public base-class method with
/// the same name collide with the one meant: <c>NMultiplayerTest</c> declares a private
/// <c>Disconnect(NetError)</c>, Godot's <c>GodotObject</c> a public <c>Disconnect(StringName,
/// Callable)</c>, and <c>GetMethod</c> answered with <c>AmbiguousMatchException</c>.
///
/// **Never throws.** The registry resolves every entry in one <c>Lazy</c>, which caches an
/// exception and rethrows it on every access. One bad entry therefore took down the probe at mod
/// load, <c>GET /health</c>, and every request path that asks the registry for anything -- including
/// <c>open_character_select</c>, which has nothing to do with the lobby. A member that cannot be
/// resolved is a missing member: the probe names it and the rest keeps working.
/// </remarks>
internal static class ReflectedMemberResolver
{
    public static FieldInfo? Field(Type declaringType, string name, bool isStatic) =>
        Guarded(() => declaringType.GetField(name, Flags(isStatic)));

    public static MethodInfo? Method(Type declaringType, string name, bool isStatic) =>
        Guarded(() => declaringType.GetMethod(name, Flags(isStatic)));

    public static MethodInfo? Method(Type declaringType, string name, bool isStatic, Type[] parameterTypes) =>
        Guarded(() => declaringType.GetMethod(
            name, genericParameterCount: 0, Flags(isStatic) | BindingFlags.ExactBinding,
            binder: null, types: parameterTypes, modifiers: null));

    public static PropertyInfo? Property(Type declaringType, string name, bool isStatic) =>
        Guarded(() => declaringType.GetProperty(name, Flags(isStatic)));

    public const string CardUpgradeSignatures = "CardModel.UpgradeInternal(bool) or CardModel.UpgradeInternal()";

    public static Action<TCard>? ResolveCardUpgrader<TCard>() where TCard : class
    {
        var model = typeof(TCard);
        const string name = "UpgradeInternal";
        // Keep the original default when v0.114.0's optional silent flag is present.
        var current = Method(model, name, isStatic: false, [typeof(bool)]);
        if (current is { IsPublic: true } && current.ReturnType == typeof(void))
        {
            var upgrade = current.CreateDelegate<Action<TCard, bool>>();
            return card => upgrade(card, false);
        }

        var legacy = Method(model, name, isStatic: false, Type.EmptyTypes);
        return legacy is { IsPublic: true } && legacy.ReturnType == typeof(void)
            ? legacy.CreateDelegate<Action<TCard>>()
            : null;
    }

    /// <summary>Invokes one known public FastHost signature; false means no compatible method was found.</summary>
    /// <remarks>Invocation failures propagate. Once hosting has been attempted, do not try another overload.</remarks>
    public static bool TryInvokeStandardFastHost<TMode>(Type declaringType, object target, TMode standard, out object? result)
        where TMode : struct, Enum
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.ExactBinding;
        var current = Guarded(() => declaringType.GetMethod("FastHost", genericParameterCount: 0, flags, binder: null,
            types: [typeof(TMode), typeof(bool)], modifiers: null));
        if (current != null)
        {
            result = current.Invoke(target, [standard, false]); // v0.113.1: isCustom = false.
            return true;
        }

        var legacy = Guarded(() => declaringType.GetMethod("FastHost", genericParameterCount: 0, flags, binder: null,
            types: [typeof(TMode)], modifiers: null));
        result = legacy?.Invoke(target, [standard]);
        return legacy != null;
    }

    private static BindingFlags Flags(bool isStatic) =>
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
        (isStatic ? BindingFlags.Static : BindingFlags.Instance);

    private static T? Guarded<T>(Func<T?> lookup)
        where T : MemberInfo
    {
        try
        {
            return lookup();
        }
        catch (Exception ex) when (ex is AmbiguousMatchException or TypeLoadException or ArgumentException)
        {
            // Overloads within the declaring type itself, or a type whose metadata will not load.
            // Either way the mod cannot say which member it means, so it has none.
            return null;
        }
    }
}
