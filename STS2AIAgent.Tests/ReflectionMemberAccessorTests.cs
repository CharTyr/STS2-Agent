using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

#pragma warning disable CS0414 // Fixture fields are intentionally read only through reflection.

internal static class ReflectionMemberAccessorTests
{
    public static void ReadsPrivateBaseFieldFromDerivedInstance()
    {
        var instance = new DerivedFixture();

        var value = ReflectionMemberAccessor.TryGetValue(
            instance, "_baseField", out var declaringType);

        Assert.Equal("base-field", value as string);
        Assert.Equal(typeof(BaseFixture), declaringType);
    }

    public static void ReadsPrivateBasePropertyFromDerivedInstance()
    {
        var instance = new DerivedFixture();

        var value = ReflectionMemberAccessor.TryGetValue(
            instance, "BaseProperty", out var declaringType);

        Assert.Equal("base-property", value as string);
        Assert.Equal(typeof(BaseFixture), declaringType);
    }

    public static void PrefersDerivedMemberWithSameName()
    {
        var instance = new DerivedFixture();

        var value = ReflectionMemberAccessor.TryGetValue(
            instance, "_shadowed", out var declaringType);

        Assert.Equal("derived", value as string);
        Assert.Equal(typeof(DerivedFixture), declaringType);
    }

    private class BaseFixture
    {
        private readonly string _baseField = "base-field";
        private readonly string _shadowed = "base";
        private string BaseProperty => "base-property";
    }

    private sealed class DerivedFixture : BaseFixture
    {
        private readonly string _shadowed = "derived";
    }
}

#pragma warning restore CS0414
