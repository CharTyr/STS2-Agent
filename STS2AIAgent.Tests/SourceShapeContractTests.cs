namespace STS2AIAgent.Tests;

/// <summary>
/// A ratchet on file size, so the two monoliths stop growing.
/// </summary>
/// <remarks>
/// Two files hold 49% of this mod's C# -- <c>GameStateService.cs</c> at 8.5k lines and
/// <c>GameActionService.cs</c> at 7k, against 31.6k in total across 82 files. Neither got there by a
/// decision; each grew by one more screen, one more action, one more predicate, and every one of
/// those additions was individually reasonable. That is how a codebase stops being navigable: not
/// through a bad commit, but through a thousand good ones with nothing counting.
///
/// This test counts. Every mod source file has a budget: <see cref="DefaultBudget"/> unless it
/// appears in <see cref="Budgets"/>, which lists the files already past it.
///
/// **Budgets go down, never up.** When a change needs more room than the budget allows, the answer
/// is to move something out of that file first, not to raise the number. Raising one is a
/// deliberate act that shows up in the diff and needs a reason in the commit message -- which is the
/// whole point, because nobody ever decided these two files should be this large.
///
/// Adding a *new* file to <see cref="Budgets"/> means declaring it a monolith. Prefer not to.
/// </remarks>
internal static class SourceShapeContractTests
{
    /// <summary>Any file not listed below stays under this.</summary>
    private const int DefaultBudget = 1000;

    /// <summary>
    /// Files already past <see cref="DefaultBudget"/>, with the headroom they are allowed. The
    /// headroom is deliberately small: enough for a fix, not enough for a feature.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Budgets = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // 318 methods in one 7.3k-line class. Three separable concerns are fused here: the raw
        // /state payload builders, the compact agent_view builders (BuildAgent*, ~690 lines), and
        // the action-availability surface. See docs/adr/0001-single-action-surface.md.
        ["STS2AIAgent/Game/GameStateService.cs"] = 8650,
        // 60 Execute* handlers and 62 WaitFor* stabilizers. Unlike the file above this one is not
        // tangled -- it is one clear pattern repeated sixty times -- so splitting it by room
        // (combat / map / shop / co-op / menus) is mechanical whenever someone wants the room.
        ["STS2AIAgent/Game/GameActionService.cs"] = 7100,
        ["STS2AIAgent/Ui/AgentOverlayHost.cs"] = 1900,
        ["STS2AIAgent/Agent/AgentRuntime.cs"] = 1450,
    };

    public static void NoSourceFileGrowsPastItsBudget()
    {
        var root = AgentSourceFixture.Root;
        var offenders = new List<string>();
        var counted = 0;

        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path).Length;
            counted++;

            var budget = Budgets.TryGetValue(relative, out var explicitBudget) ? explicitBudget : DefaultBudget;
            if (lines > budget)
            {
                offenders.Add($"{relative} is {lines} lines, over its {budget}-line budget");
            }
        }

        // A broken enumeration would pass this test while checking nothing.
        Assert.True(
            counted >= 50,
            $"Only {counted} mod source files were counted, which is too few to be the whole tree. "
            + "AgentSourceFixture.SourceFiles() no longer walks the mod; fix it before trusting this "
            + "contract.");

        Assert.True(
            offenders.Count == 0,
            "These files are over budget:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nMove something out rather than raising the budget. Half of this mod's C# already "
            + "lives in two files, and every line of that arrived one reasonable addition at a time.");
    }

    /// <summary>
    /// A budget that no longer binds is not a ratchet. When a file shrinks well past its entry, the
    /// entry has to come down with it, or the room it leaves behind quietly becomes room to regrow.
    /// </summary>
    public static void BudgetsStayCloseToTheFilesTheyGuard()
    {
        var root = AgentSourceFixture.Root;
        var slack = new List<string>();

        foreach (var (relative, budget) in Budgets)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relative} has a size budget but no longer exists. Remove the entry.");

            var lines = File.ReadAllLines(path).Length;
            if (lines <= DefaultBudget)
            {
                slack.Add($"{relative} is down to {lines} lines and fits the {DefaultBudget}-line default; drop its entry");
            }
            else if (budget - lines > 500)
            {
                slack.Add($"{relative} is {lines} lines against a {budget}-line budget; lower the budget to match");
            }
        }

        Assert.True(
            slack.Count == 0,
            "These budgets have drifted away from the files they guard:\n  " + string.Join("\n  ", slack));
    }
}
