using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class RewardChoicePolicyTests
{
    public static void ExplicitIndexPicksThatOption()
    {
        var resolution = RewardChoicePolicy.Resolve(1, 3);

        Assert.Equal(RewardChoiceKind.Pick, resolution.Kind);
        Assert.True(resolution.IsValid);
        Assert.Equal(1, resolution.Index);
        Assert.Null(resolution.Reason);
    }

    public static void ExplicitOutOfRangeIndexIsInvalid()
    {
        var above = RewardChoicePolicy.Resolve(3, 3);
        Assert.Equal(RewardChoiceKind.Pick, above.Kind);
        Assert.False(above.IsValid);
        Assert.NotNull(above.Reason);

        // An explicit negative index other than the documented -1 skip must not
        // silently degrade into "pick the first card".
        var below = RewardChoicePolicy.Resolve(-5, 3);
        Assert.Equal(RewardChoiceKind.Pick, below.Kind);
        Assert.False(below.IsValid);
        Assert.NotNull(below.Reason);
    }

    public static void MissingIndexKeepsFirstCardBehavior()
    {
        var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.AutoChoice, 2);

        Assert.Equal(RewardChoiceKind.Auto, resolution.Kind);
        Assert.True(resolution.IsValid);
        Assert.Equal(0, resolution.Index);
    }

    public static void SkipIsValidWithoutOptions()
    {
        foreach (var optionCount in new[] { 0, 3 })
        {
            var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.SkipChoice, optionCount);
            Assert.Equal(RewardChoiceKind.Skip, resolution.Kind);
            Assert.True(resolution.IsValid);
        }
    }

    public static void AutoWithoutOptionsIsNotAPick()
    {
        var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.AutoChoice, 0);

        Assert.Equal(RewardChoiceKind.Auto, resolution.Kind);
        Assert.False(resolution.IsValid);
        Assert.Contains("no card reward options", resolution.Reason);
    }
}
