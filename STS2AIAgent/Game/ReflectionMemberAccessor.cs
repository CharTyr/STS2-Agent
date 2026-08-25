using System.Reflection;

namespace STS2AIAgent.Game;

internal static class ReflectionMemberAccessor
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Reads an instance property or field, including private members declared on a base class.
    /// Type.GetField/GetProperty on a derived runtime type does not return inherited private
    /// members, so each level must be inspected explicitly.
    /// </summary>
    public static object? TryGetValue(object instance, string memberName)
    {
        return TryGetValue(instance, memberName, out _);
    }

    public static object? TryGetValue(object instance, string memberName, out Type? declaringType)
    {
        declaringType = null;

        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            try
            {
                var property = type.GetProperty(memberName, DeclaredInstanceMembers);
                if (property != null)
                {
                    var value = property.GetValue(instance);
                    declaringType = type;
                    return value;
                }
            }
            catch
            {
                // Reflection is best-effort. Keep walking in case a base field is usable.
            }

            try
            {
                var field = type.GetField(memberName, DeclaredInstanceMembers);
                if (field != null)
                {
                    var value = field.GetValue(instance);
                    declaringType = type;
                    return value;
                }
            }
            catch
            {
                // A game update may make a member unreadable; callers already handle null.
            }
        }

        return null;
    }
}
