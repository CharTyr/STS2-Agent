namespace STS2AIAgent.Tests;

/// <summary>Fail closed when card semantics are not proven; never reuse another fight's threat.</summary>
internal static class CombatMathSafetyContractTests
{
    public static void CacheAndTargetingAreConservative()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.CombatMath.cs");
        var build = AgentSourceFixture.MethodBody(source, "BuildCombatMath");
        Assert.Contains("GetProcessFrames()", build);
        Assert.Contains("GetHashCode(combatState)", build);
        Assert.Contains("Serialize(combat)", build);
        Assert.Contains("_combatMathFrame = null", build);
        var preview = AgentSourceFixture.MethodBody(source, "PreviewCardAgainstEnemies");
        Assert.Contains("TargetType.AnyEnemy or TargetType.AllEnemies", preview);
        var hits = AgentSourceFixture.MethodBody(source, "ReadHitCount");
        Assert.False(hits.Contains("? null : 1", StringComparison.Ordinal), "Unknown cards must not silently become one hit.");
        Assert.Contains("? 1 : null", hits);
    }
}
