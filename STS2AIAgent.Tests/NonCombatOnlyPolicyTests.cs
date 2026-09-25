using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class NonCombatOnlyPolicyTests
{
    public static void DetectsCompactAndActionSnapshotCombat()
    {
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot(
            """{"screen":"COMBAT","in_combat":false}"""));
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot(
            """{"state":{"screen":"REWARD","in_combat":true},"available_actions":[]}"""));
        Assert.False(NonCombatOnlyPolicy.IsCombatSnapshot(
            """{"state":{"screen":"REWARD","in_combat":false},"available_actions":[]}"""));
    }

    public static void UnknownStateFailsClosed()
    {
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot(null));
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot("not json"));
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot("[]"));
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot("{}"));
        Assert.True(NonCombatOnlyPolicy.IsCombatSnapshot(
            """{"screen":"REWARD","in_combat":"unknown"}"""));
    }
}
