using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The two action surfaces answer the same question and must keep answering it the same way.
/// </summary>
/// <remarks>
/// <c>GET /state</c> reports <c>available_actions</c> from <c>BuildAvailableActionNames</c>, and
/// <c>GET /actions/available</c> reports the same actions with parameter hints from
/// <c>BuildAvailableActionsPayload</c>. They are two hand-written implementations of one decision --
/// 301 and 609 lines, consulting the same 50 <c>Can*</c> predicates to emit the same 55 action names
/// -- so every new action has to be added to both, in two different places, and nothing but this
/// test notices when only one of them is updated.
///
/// A drift here is the worst kind this codebase has already shipped once: a client is told an action
/// exists on one endpoint and not the other, or worse, <c>/state</c> advertises something the
/// executor refuses. The v0.12.4 re-cut was exactly that shape inside a single response.
///
/// This test does not remove the duplication -- see
/// <c>docs/adr/0001-single-action-surface.md</c> for why that refactor is scoped to a session that
/// can validate against the running game. What it does is make the duplication loud: the two sets
/// have to stay identical, and adding an action to one alone turns this red with the name of the
/// action that was forgotten.
///
/// Emission *order* is deliberately not compared. The two methods already emit in different orders
/// today and no client depends on it: the play skill and <c>state-invariants</c> both test
/// membership. Pinning order would only record an accident.
/// </remarks>
internal static class ActionSurfaceContractTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";

    private const string NamesDeclaration = "private static string[] BuildAvailableActionNames(";
    private const string DescriptorsDeclaration =
        "public static AvailableActionsPayload BuildAvailableActionsPayload()";

    private static readonly Regex Predicate = new(@"\b(Can[A-Z]\w+)\(", RegexOptions.Compiled);
    private static readonly Regex EmittedName = new(@"names\.Add\(""([a-z_]+)""\)", RegexOptions.Compiled);
    private static readonly Regex DescribedName = new(@"name = ""([a-z_]+)""", RegexOptions.Compiled);

    // A rewrite that defeats the extraction must fail loudly instead of reading as "nothing to
    // compare". Both surfaces have carried more than forty actions since v0.10.
    private const int MinimumActions = 40;
    private const int MinimumPredicates = 40;

    public static void BothSurfacesOfferTheSameActions()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var names = AgentSourceFixture.DeclarationBody(source, NamesDeclaration);
        var descriptors = AgentSourceFixture.DeclarationBody(source, DescriptorsDeclaration);

        var emitted = Matches(EmittedName, names);
        var described = Matches(DescribedName, descriptors);

        Assert.True(
            emitted.Count >= MinimumActions,
            $"BuildAvailableActionNames yielded {emitted.Count} action names, below the "
            + $"{MinimumActions} expected. The extraction in this test no longer matches the method; "
            + "fix it before trusting this contract.");
        Assert.True(
            described.Count >= MinimumActions,
            $"BuildAvailableActionsPayload yielded {described.Count} action names, below the "
            + $"{MinimumActions} expected. The extraction in this test no longer matches the method; "
            + "fix it before trusting this contract.");

        AssertSameSet(
            emitted,
            described,
            "action",
            "GET /state's available_actions",
            "GET /actions/available's descriptors");
    }

    public static void BothSurfacesAskTheSamePredicates()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var names = AgentSourceFixture.DeclarationBody(source, NamesDeclaration);
        var descriptors = AgentSourceFixture.DeclarationBody(source, DescriptorsDeclaration);

        var asked = Matches(Predicate, names);
        var describedAsk = Matches(Predicate, descriptors);

        Assert.True(
            asked.Count >= MinimumPredicates,
            $"BuildAvailableActionNames consults {asked.Count} Can* predicates, below the "
            + $"{MinimumPredicates} expected. The extraction in this test no longer matches the "
            + "method; fix it before trusting this contract.");

        AssertSameSet(
            asked,
            describedAsk,
            "Can* predicate",
            "BuildAvailableActionNames",
            "BuildAvailableActionsPayload");
    }

    private static SortedSet<string> Matches(Regex pattern, string body)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in pattern.Matches(body))
        {
            found.Add(match.Groups[1].Value);
        }

        return found;
    }

    private static void AssertSameSet(
        SortedSet<string> left,
        SortedSet<string> right,
        string subject,
        string leftName,
        string rightName)
    {
        var onlyLeft = left.Except(right, StringComparer.Ordinal).ToArray();
        var onlyRight = right.Except(left, StringComparer.Ordinal).ToArray();

        Assert.True(
            onlyLeft.Length == 0,
            $"{leftName} offers {subject}(s) {rightName} does not: {string.Join(", ", onlyLeft)}. "
            + "The two surfaces are one decision written twice; add it to both.");
        Assert.True(
            onlyRight.Length == 0,
            $"{rightName} offers {subject}(s) {leftName} does not: {string.Join(", ", onlyRight)}. "
            + "The two surfaces are one decision written twice; add it to both.");
    }
}
