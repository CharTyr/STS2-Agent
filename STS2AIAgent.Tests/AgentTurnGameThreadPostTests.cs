using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace STS2AIAgent.Tests;

/// <summary>
/// Every game-thread post made by the in-game agent's turn must carry the caller's token (and, where
/// the turn cannot wait forever, a deadline). The token-less <c>GameThread.InvokeAsync(action)</c>
/// overloads only observe cancellation from inside the callback, so when the game thread stops
/// pumping the callback never runs, pause never lands, and the loop wedges in "stopping" until the
/// game restarts. 9df1819 bounded the state reads; the act, game-data, screenshot, turn snapshot and
/// companion posts were left behind and fixed on 2026-09-23. This keeps the next one from slipping.
/// </summary>
internal static class AgentTurnGameThreadPostTests
{
    private static readonly string[] TurnFiles =
    {
        "STS2AIAgent/Agent/GameBridge.cs",
        "STS2AIAgent/Agent/AgentRuntime.Session.cs",
    };

    public static void EveryAgentBridgePostCarriesTheToken()
    {
        var offenders = new List<string>();
        foreach (var file in TurnFiles)
        {
            offenders.AddRange(UnboundedPosts(file, _ => true));
        }

        // AgentRuntime.cs also hosts overlay/chat posts that are not on the play turn; only the auto-play
        // turn's own methods are held to the rule.
        offenders.AddRange(UnboundedPosts(
            "STS2AIAgent/Agent/AgentRuntime.cs",
            method => method is "AutoPlayLoopAsync" or "TryCompanionImmediateAsync"));

        Assert.True(
            offenders.Count == 0,
            "Agent-turn game-thread posts must pass the turn's CancellationToken (and a budget where "
            + "the turn cannot wait forever):\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> UnboundedPosts(string relativePath, Func<string, bool> methodFilter)
    {
        var path = Path.Combine(AgentSourceFixture.Root, relativePath);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "GameThread" },
                    Name.Identifier.Text: "InvokeAsync"
                })
            {
                continue;
            }

            var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
            if (!methodFilter(method))
            {
                continue;
            }

            if (invocation.ArgumentList.Arguments.Count < 2)
            {
                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                yield return $"{relativePath}:{line} ({method})";
            }
        }
    }
}
