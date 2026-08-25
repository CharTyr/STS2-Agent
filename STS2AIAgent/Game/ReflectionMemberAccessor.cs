using System.Collections.Concurrent;
using System.Reflection;

namespace STS2AIAgent.Game;

internal static class ReflectionMemberAccessor
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly ConcurrentDictionary<(Type RuntimeType, string MemberName), MemberLookup>
        MemberCache = new();

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
        if (string.IsNullOrEmpty(memberName))
        {
            return null;
        }

        var lookup = MemberCache.GetOrAdd(
            (instance.GetType(), memberName),
            static key => FindMember(key.RuntimeType, key.MemberName));
        declaringType = lookup.DeclaringType;
        if (lookup.Member == null)
        {
            return null;
        }

        try
        {
            return lookup.Member switch
            {
                PropertyInfo property => property.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => null
            };
        }
        catch
        {
            // Once the most-derived matching member is found, do not fall back to a
            // shadowed base member: a failed read means this member is unavailable.
            return null;
        }
    }

    private static MemberLookup FindMember(Type runtimeType, string memberName)
    {
        for (var type = runtimeType; type != null; type = type.BaseType)
        {
            try
            {
                var property = type.GetProperty(memberName, DeclaredInstanceMembers);
                if (property != null)
                {
                    return new MemberLookup(property, type);
                }
            }
            catch
            {
                // An ambiguous or otherwise unreadable most-derived declaration must not
                // expose a shadowed base member as if it were the requested member.
                return new MemberLookup(null, type);
            }

            try
            {
                var field = type.GetField(memberName, DeclaredInstanceMembers);
                if (field != null)
                {
                    return new MemberLookup(field, type);
                }
            }
            catch
            {
                return new MemberLookup(null, type);
            }
        }

        return new MemberLookup(null, null);
    }

    private sealed record MemberLookup(MemberInfo? Member, Type? DeclaringType);
}
