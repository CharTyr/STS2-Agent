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

    public static PropertyInfo? Property(Type declaringType, string name, bool isStatic) =>
        Guarded(() => declaringType.GetProperty(name, Flags(isStatic)));

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
