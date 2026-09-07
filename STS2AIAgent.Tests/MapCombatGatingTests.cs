using System;
namespace STS2AIAgent.Tests;

internal static class MapCombatGatingTests
{
    public static void ChooseMapNodeHiddenWhileCombatInProgress()
    {
        var source = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs"));
        var start = source.IndexOf("publicstaticboolCanChooseMapNode", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = source.Substring(start, Math.Min(400, source.Length - start));
        Assert.Contains("if(CombatManager.Instance.IsInProgress){returnfalse;}", method, StringComparison.Ordinal);
    }
}
