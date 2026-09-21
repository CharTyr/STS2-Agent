namespace STS2AIAgent.Tests;

/// <summary>
/// A ratchet on file size, so the two monoliths stop growing.
/// </summary>
/// <remarks>
/// Two files once held 49% of this mod's C# -- <c>GameStateService.cs</c> at 8.5k lines and
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
        // 8,559 lines until ADR 0001 collapsed the two action surfaces into one walk, then 8,295
        // until the compact agent_view moved to its own file, then 5,730 until the availability
        // predicates moved to GameStateService.Predicates.cs, then 1,336 when the raw /state
        // builders were split by screen on 2026-09-20. The budget came down every time, which is
        // what the ratchet is for. What is left here is the payload entry point, the availability
        // walk, screen resolution, and the node/text helpers more than one screen file reads.
        ["STS2AIAgent/Game/GameStateService.cs"] = 1400,
        // The 60 payload types of GET /state: the wire format, as declarations. They grow with the
        // API and are checked against docs/api.md by the api-facts gate, so the budget here is
        // about noticing, not about stopping them.
        ["STS2AIAgent/Game/GameStateService.Payloads.cs"] = 1300,
        // The compact agent_view rewrite, split out of the file above. It is a projection of the
        // raw payloads, so it grows when they do -- which is the reason to watch it separately
        // rather than let it grow inside a file already too big to notice.
        ["STS2AIAgent/Game/GameStateService.AgentView.cs"] = 1400,
        // 7,061 lines until it was split by room on 2026-09-17. What is left in the base file is
        // the dispatch switch and the helpers more than one room reaches; six of the eight room
        // files came in under the default budget and so have no entry at all, which is the shape
        // to aim for.
        ["STS2AIAgent/Game/GameActionService.cs"] = 1300,
        // Chests, events, rest sites, the crystal sphere, capstones and bundles. The largest room
        // because it is really six small ones that share their settle-and-proceed helpers; if it
        // grows again, it splits rather than the number going up.
        ["STS2AIAgent/Game/GameActionService.Rooms.cs"] = 1250,
        // 1,829 lines until the tab construction moved to AgentOverlayHost.Tabs.cs, then 1,117 when
        // the page bodies and the single 220-line refresh pass moved to AgentOverlayHost.Pages.cs on
        // 2026-10-01. The budget came down with the file both times, which is the ratchet working:
        // the seventh tab and the next dashboard row land with the other pages instead of here.
        ["STS2AIAgent/Ui/AgentOverlayHost.cs"] = 1150,
        // 1,492 lines until the AI-teammate surface moved to AgentRuntime.Team.cs on 2026-09-20. The
        // budget came down with the file rather than being raised for the feature that pushed it
        // over, which is the same move the overlay made for its sixth tab.
        // Receipt accounting moved to AgentRuntime.Accounting.cs; keep the reduced base budget.
        ["STS2AIAgent/Agent/AgentRuntime.cs"] = 1350,
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
