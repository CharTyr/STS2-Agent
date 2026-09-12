using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the bounded game waits. GameActionService.cs is not part of the
/// offline compile, so the removed bare-await shapes are asserted from the source instead.
/// </summary>
internal static class GameTaskBoundingContractTests
{
    private const string SourcePath = "STS2AIAgent/Game/GameActionService.cs";

    /// <summary>
    /// A game task awaited bare, with no helper call in between: <c>await someTask;</c> or
    /// <c>await some.task;</c>. Matched by shape rather than by variable name so a rename cannot
    /// slip an unbounded wait past the contract.
    /// </summary>
    private static readonly Regex BareAwaitShape = new(
        @"\bawait\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*\s*;",
        RegexOptions.Compiled);

    public static void EveryGameTaskAwaitIsBounded()
    {
        var source = AgentSourceFixture.Read(SourcePath);

        AssertNoBareAwaitAnywhere(source);

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
        var rawBody = AgentSourceFixture.MethodBody(source, methodName);
        var body = AgentSourceFixture.WithoutWhitespace(rawBody);

        Assert.Contains("WaitForGameTaskAsync(", body, StringComparison.Ordinal);
        Assert.False(
            body.Contains(forbiddenShape, StringComparison.Ordinal),
            $"{methodName} must not await a game task without a deadline ({forbiddenShape}).");
        AssertNoBareAwait(methodName, rawBody);
    }

    /// <summary>
    /// Scans the whole file minus the intended unbounded awaits: the two fire-and-forget
    /// observers and the already-bounded <c>WaitForTaskResultAsync</c> wrapper. A per-method list
    /// alone would miss a new handler, and a fixed variable name would miss a rename.
    /// </summary>
    private static void AssertNoBareAwaitAnywhere(string source)
    {
        var scanned = source;
        foreach (var exempt in new[]
                 {
                     "WaitForTaskResultAsync",
                     "ObserveBackgroundTaskCore",
                     "ObserveBackgroundResultCore",
                 })
        {
            scanned = scanned.Replace(
                AgentSourceFixture.MethodBody(source, exempt),
                "{}",
                StringComparison.Ordinal);
        }

        AssertNoBareAwait("GameActionService.cs", scanned);
    }

    private static void AssertNoBareAwait(string methodName, string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            var match = BareAwaitShape.Match(line);
            if (match.Success)
            {
                throw new Exception(
                    $"{methodName} awaits a game task without a deadline ({match.Value.Trim()}); route it through WaitForGameTaskAsync.");
            }
        }
    }
}
