using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the bounded game waits. The game-facing files are not part of the
/// offline compile, so the rule is read from the source instead: a task the game hands back has to be
/// raced against a deadline before anything awaits it.
/// </summary>
internal static class GameTaskBoundingContractTests
{
    private const string SourcePath = "STS2AIAgent/Game/GameActionService.cs";

    /// <summary>
    /// Files that drive game objects from an action or request path. Each one is scanned whole: a game
    /// task awaited here can wedge the request, and none of these files compile offline.
    /// </summary>
    private static readonly string[] GameDrivingPaths =
    {
        "STS2AIAgent/Game/GameActionService.cs",
        "STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs",
        "STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs",
        "STS2AIAgent/Ui/AgentOverlayHost.cs",
    };

    /// <summary>
    /// The fire-and-forget observers await their task to completion on purpose: they are off the
    /// request path, and that await is how the result and any exception are consumed. There is no
    /// other exemption — a call site cannot opt out by looking like one of these.
    /// </summary>
    private static readonly string[] AwaitToCompletionByDesign =
    {
        "ObserveBackgroundTaskCore",
        "ObserveBackgroundResultCore",
    };

    /// <summary>
    /// The bounded wait, plus the race it is built from. A task that came out of one of these has
    /// already been raced against its deadline, so awaiting it afterwards is what call sites do.
    /// </summary>
    private static readonly string[] BoundingCalls =
    {
        "WaitForGameTaskAsync",
        "Task.WhenAny",
    };

    public static void EveryGameTaskAwaitIsBounded()
    {
        var source = AgentSourceFixture.Read(SourcePath);

        AssertBounded(source, "ExecuteSaveAndQuitAsync", "awaitcloseTask");
        AssertBounded(source, "ExecuteCrystalClearCellAsync", "awaitminigame.CellClicked");
        AssertBounded(source, "ExecuteChooseEventOptionAsync", "awaitNEventRoom.Proceed");
        AssertBounded(source, "ExecuteChooseRestOptionAsync", "awaitchooseTask");
        AssertBounded(source, "ExecuteBuyCardAsync", "awaitentry.OnTryPurchaseWrapper");
        AssertBounded(source, "ExecuteBuyRelicAsync", "awaitentry.OnTryPurchaseWrapper");
        AssertBounded(source, "ExecuteBuyPotionAsync", "awaitentry.OnTryPurchaseWrapper");
        AssertBounded(source, "ExecuteHostMultiplayerLobbyAsync", "awaitstartHostTask");
        AssertBounded(source, "ExecuteJoinMultiplayerLobbyAsync", "awaitscene.JoinToHost");
        AssertBounded(source, "ExecuteConsoleCommandCoreAsync", "awaitresult.task");
        AssertBounded(source, "InvokeFastHostAsync", "awaittask;");

        var declared = DeclaredMemberNames();
        foreach (var path in GameDrivingPaths)
        {
            AssertEveryAwaitIsBounded(path, declared);
        }
    }

    public static void TheBackgroundObserverStaysUnbounded()
    {
        var source = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read(SourcePath));

        // The fire-and-forget observer is not on a request path; it must still await its task
        // to completion so the result and any exception are consumed.
        Assert.Contains("varsuccess=awaittask;", source, StringComparison.Ordinal);
    }

    public static void TheBoundedWaitHandsTheTaskBackForObservation()
    {
        var source = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read(SourcePath));

        Assert.Contains("WaitForGameTaskAsync<T>", source, StringComparison.Ordinal);
        // Both overloads hand the task back, and both treat a task that already finished as
        // completed even when the timeout delay won the Task.WhenAny race: reporting a finished
        // task as a timeout would also make the call sites dereference a null task.
        Assert.Contains("if(completedTask==task){returntask;}", source, StringComparison.Ordinal);
        Assert.Contains("returntask.IsCompleted?task:null;", source, StringComparison.Ordinal);
        Assert.Contains("GameTaskWaitPolicy.DescribeTimeout(", source, StringComparison.Ordinal);
    }

    private static void AssertBounded(string source, string methodName, string forbiddenShape)
    {
        var body = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, methodName));

        Assert.Contains("WaitForGameTaskAsync(", body, StringComparison.Ordinal);
        Assert.False(
            body.Contains(forbiddenShape, StringComparison.Ordinal),
            $"{methodName} must not await a game task without a deadline ({forbiddenShape}).");
    }

    /// <summary>
    /// Walks every await in a game-driving file. An await passes when it targets one of our own
    /// members — those carry their own deadline — or a task a bounding call handed back. It fails
    /// when it targets a game API directly, which is the shape a bare
    /// <c>await entry.OnTryPurchaseWrapper(...)</c> takes, and when it targets a task variable nobody
    /// bounded, which is the older <c>await closeTask;</c> shape. Matching on the awaited expression
    /// rather than on a line means argument lists, generics, nested parentheses and multi-line call
    /// sites cannot hide behind their spelling.
    /// </summary>
    private static void AssertEveryAwaitIsBounded(string path, HashSet<string> declared)
    {
        var root = CSharpSyntaxTree.ParseText(AgentSourceFixture.Read(path), path: path).GetRoot();
        var byDesign = AwaitToCompletionByDesign.ToHashSet(StringComparer.Ordinal);

        foreach (var await in root.DescendantNodes().OfType<AwaitExpressionSyntax>())
        {
            if (await.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .Any(method => byDesign.Contains(method.Identifier.ValueText)))
            {
                continue;
            }

            var target = Unwrap(await.Expression);
            if (target is InvocationExpressionSyntax invocation)
            {
                var callee = CalleeName(invocation);
                if (callee != null &&
                    (declared.Contains(callee) ||
                     IsFrameworkTaskCall(invocation) ||
                     PassesACancellationToken(invocation)))
                {
                    continue;
                }

                throw new Exception(
                    $"{path}:{Line(await)} awaits '{target}' with no deadline: the callee is not a mod "
                    + "member, so this is a game task awaited directly. Route it through "
                    + "WaitForGameTaskAsync, or pass a live CancellationToken if the call already has a "
                    + "way out.");
            }

            var awaitedName = LastName(target);
            if (awaitedName != null && WasProducedByABoundingCall(root, awaitedName))
            {
                continue;
            }

            throw new Exception(
                $"{path}:{Line(await)} awaits '{target}' with no deadline: no bounding call produced it.");
        }
    }

    /// <summary>
    /// True when the awaited name is bound to the result of a bounding call, by declaration
    /// (<c>var completedTask = await WaitForGameTaskAsync(task, timeout);</c>) or by assignment in a
    /// loop. This is the only reason a task value may be awaited bare.
    /// </summary>
    private static bool WasProducedByABoundingCall(SyntaxNode root, string name)
    {
        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Identifier.ValueText == name &&
                MentionsBoundingCall(declarator.Initializer?.Value))
            {
                return true;
            }
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left.ToString() == name && MentionsBoundingCall(assignment.Right))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsBoundingCall(SyntaxNode? expression)
    {
        if (expression is null)
        {
            return false;
        }

        var text = expression.ToString();
        return BoundingCalls.Any(call => text.Contains(call, StringComparison.Ordinal));
    }

    /// <summary>Every member name the mod declares, so a call to one of our own reads as bounded.</summary>
    private static HashSet<string> DeclaredMemberNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case MethodDeclarationSyntax method:
                        names.Add(method.Identifier.ValueText);
                        break;
                    case LocalFunctionStatementSyntax local:
                        names.Add(local.Identifier.ValueText);
                        break;
                    case PropertyDeclarationSyntax property:
                        names.Add(property.Identifier.ValueText);
                        break;
                }
            }
        }

        return names;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        PostfixUnaryExpressionSyntax postfix => Unwrap(postfix.Operand),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        _ => expression,
    };

    private static string? CalleeName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name switch
        {
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => null,
        },
        _ => null,
    };

    private static string? LastName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// <c>Task.WhenAny</c>, <c>Task.Delay</c> and friends: framework plumbing with its own clock,
    /// never a game task.
    /// </summary>
    private static bool IsFrameworkTaskCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member &&
        member.Expression.ToString() is "Task" or "ValueTask" or "System.Threading.Tasks.Task";

    /// <summary>
    /// A call that receives a live token carries its own way out — a semaphore wait, an HTTP GET on a
    /// client whose caller owns the deadline. <c>CancellationToken.None</c> does not count: that is a
    /// statement that nothing will ever cancel the call.
    /// </summary>
    private static bool PassesACancellationToken(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Any(argument =>
        {
            var text = argument.Expression.ToString();
            return text.Contains("cancellationToken", StringComparison.Ordinal) &&
                   !text.Contains(".None", StringComparison.Ordinal);
        });

    private static int Line(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
