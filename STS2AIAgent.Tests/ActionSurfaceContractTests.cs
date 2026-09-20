using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// One walk decides what the executor will accept, and both action surfaces report it.
/// </summary>
/// <remarks>
/// <c>GET /state</c> reports <c>available_actions</c> and <c>GET /actions/available</c> reports the
/// same actions with their parameter hints. Until ADR 0001 they were two hand-written
/// implementations of that one decision -- 301 and 609 lines consulting the same 50 <c>Can*</c>
/// predicates to emit the same 55 action names -- so every new action had to be added twice and
/// nothing but a test noticed when only one was updated.
///
/// They now both read <c>EnumerateAvailableActions</c>. This contract keeps it that way: the
/// duplication cannot come back by someone adding a predicate to one surface, because neither
/// surface has anywhere to put one.
///
/// What this replaces is worth remembering. The earlier version of this file asserted that two
/// independent implementations agreed, which is a strictly weaker promise -- it could only catch a
/// divergence after someone wrote it, and only for the predicates and names it knew to compare.
/// </remarks>
internal static class ActionSurfaceContractTests
{

    private const string WalkerDeclaration =
        "private static List<ActionDescriptor> EnumerateAvailableActions(";
    private const string NamesDeclaration = "private static string[] BuildAvailableActionNames(";
    private const string DescriptorsDeclaration =
        "public static AvailableActionsPayload BuildAvailableActionsPayload()";

    private static readonly Regex Predicate = new(@"\bCan[A-Z]\w+\(", RegexOptions.Compiled);
    private static readonly Regex ActionName = new(@"name = ""([a-z_]+)""", RegexOptions.Compiled);
    private static readonly Regex RequiresTargetAssignment = new(
        @"requires_target\s*=\s*(true|false)\s*[,;]",
        RegexOptions.Compiled);

    // A rewrite that defeats the extraction must fail loudly instead of reading as "nothing to
    // check". Both surfaces have carried more than forty actions since v0.10.
    private const int MinimumActions = 40;
    private const int MinimumPredicates = 40;

    public static void OneWalkDecidesWhatIsOffered()
    {
        var source = AgentSourceFixture.ReadStateService();
        var walker = AgentSourceFixture.DeclarationBody(source, WalkerDeclaration);

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in ActionName.Matches(walker))
        {
            names.Add(match.Groups[1].Value);
        }

        Assert.True(
            names.Count >= MinimumActions,
            $"EnumerateAvailableActions yielded {names.Count} action names, below the "
            + $"{MinimumActions} expected. The extraction in this test no longer matches the method; "
            + "fix it before trusting this contract.");
        Assert.True(
            Predicate.Matches(walker).Count >= MinimumPredicates,
            $"EnumerateAvailableActions consults {Predicate.Matches(walker).Count} Can* predicates, "
            + $"below the {MinimumPredicates} expected. The extraction in this test no longer matches "
            + "the method; fix it before trusting this contract.");
    }

    public static void NeitherSurfaceDecidesForItself()
    {
        var source = AgentSourceFixture.ReadStateService();

        foreach (var (declaration, surface) in new[]
                 {
                     (NamesDeclaration, "GET /state's available_actions"),
                     (DescriptorsDeclaration, "GET /actions/available"),
                 })
        {
            var body = AgentSourceFixture.DeclarationBody(source, declaration);

            Assert.True(
                body.Contains("EnumerateAvailableActions(", StringComparison.Ordinal),
                $"{surface} must report the actions EnumerateAvailableActions found. Reading the game "
                + "state a second way is how the two surfaces came to be 910 lines answering one "
                + "question twice.");

            var ownPredicate = Predicate.Match(body);
            Assert.False(
                ownPredicate.Success,
                $"{surface} consults {ownPredicate.Value} itself instead of reporting the shared walk. "
                + "Availability belongs in EnumerateAvailableActions, where both surfaces see it.");

            Assert.False(
                ActionName.IsMatch(body),
                $"{surface} names an action itself instead of reporting the shared walk. An action "
                + "added here would appear on one endpoint and not the other.");
        }
    }

    /// <summary>
    /// Descriptor <c>requires_target</c> is a static call-shape constant, and docs/api.md says so.
    /// </summary>
    /// <remarks>
    /// A 2,346-sample live pass (2026-09-17) found the flag false on every descriptor across 34
    /// action names and could not tell a dead branch from design. It is design: no action
    /// unconditionally takes <c>target_index</c>. Three actions take it conditionally --
    /// <c>play_card</c>, <c>use_potion</c>, and multiplayer rest options -- and each advertises
    /// that per item (<c>combat.hand[].requires_target</c>, <c>run.potions[].requires_target</c>,
    /// the rest option's own flag), which is the adjudication api.md's descriptor note records.
    /// If the flag ever becomes dynamic, this test failing is the reminder to change api.md's
    /// note and this contract in the same commit.
    /// </remarks>
    public static void DescriptorTargetIsDocumentedConstant()
    {
        var source = AgentSourceFixture.ReadStateService();
        var walker = AgentSourceFixture.DeclarationBody(source, WalkerDeclaration);

        var assignments = RequiresTargetAssignment.Matches(walker);
        Assert.True(
            assignments.Count >= MinimumActions,
            $"EnumerateAvailableActions carries {assignments.Count} requires_target assignments, "
            + $"below the {MinimumActions} expected. The extraction no longer matches the method; "
            + "fix it before trusting this contract.");

        foreach (Match assignment in assignments)
        {
            Assert.True(
                string.Equals(assignment.Groups[1].Value, "false", StringComparison.Ordinal),
                "EnumerateAvailableActions assigns a non-false requires_target. The descriptor flag "
                + "is documented in docs/api.md as a static constant whose per-item truth lives on "
                + "hand cards, potions and rest options; if you are making it dynamic, update that "
                + "note and this contract in the same change.");
        }
    }
}
