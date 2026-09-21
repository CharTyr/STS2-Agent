using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// A crystal-sphere item's identity is what a divination buys, so the payload withholds it until the
/// item is actually revealed. These contracts read the files that decide that: the payload type
/// (nullability), the builder (the gate), the compact view (which must not re-add it) and the docs
/// (the promise a client plans against).
/// </summary>
/// <remarks>
/// <c>BuildCrystalSpherePayload</c> computed <c>revealed</c> for every item and then serialized
/// <c>kind = item.GetType().Name...</c> and <c>is_good = item.IsGood</c> unconditionally, so an item
/// whose cells were all still hidden shipped its identity next to a flag saying it was hidden. The
/// compact <c>agent_view</c> passes <c>crystal_sphere</c> through untouched, so both the raw
/// <c>/state</c> payload and the default MCP read carried it: the reward/curse layout was readable at
/// zero divination cost, which contradicts the reveal-gated semantics <c>screen-playbooks.md</c>
/// describes. Occupancy fields stay ungated on purpose -- they are the board a client plans clicks
/// against, and gating them would change what the minigame exposes rather than what it protects.
/// </remarks>
internal static class CrystalSphereRevealGatingContractTests
{
    private const string PayloadsFile = "STS2AIAgent/Game/GameStateService.Payloads.cs";
    private const string RoomsFile = "STS2AIAgent/Game/GameStateService.Rooms.cs";
    private const string AgentViewFile = "STS2AIAgent/Game/GameStateService.AgentView.cs";
    private const string ApiDocFile = "docs/api.md";
    private const string PlaybookFile = "skills/sts2-mcp-player/references/screen-playbooks.md";

    /// <summary>
    /// Both identity fields are nullable, and only they: geometry and occupancy stay non-nullable
    /// because the client plans against them whether or not the item is revealed.
    /// </summary>
    public static void ItemIdentityFieldsAreNullable()
    {
        var payload = ClassBody(PayloadsFile, "internal sealed class CrystalSphereItemPayload");

        AssertContains(
            "publicstring?kind{get;init;}",
            payload,
            "CrystalSphereItemPayload.kind must be a nullable string, or a hidden item has to invent "
            + "an identity to satisfy the type.");
        AssertContains(
            "publicbool?is_good{get;init;}",
            payload,
            "CrystalSphereItemPayload.is_good must be a nullable bool for the hidden case to exist.");

        AssertContains("publicstring?kind", payload, "kind is the withheld field.");
        AssertContains(
            "publicintx{get;init;}",
            payload,
            "x must stay non-nullable: the occupied cells are board occupancy, not a divination.");
        AssertContains(
            "publicboolrevealed{get;init;}",
            payload,
            "revealed must stay non-nullable: it is the flag the gate reads.");
        AssertContains(
            "publicint[][]hidden_cells{get;init;}",
            payload,
            "hidden_cells must stay visible: it is what the client spends divinations on.");
    }

    /// <summary>
    /// The builder computes <c>revealed</c> once, before it creates the payload, and both identity
    /// fields are assigned through that flag with a null for the hidden branch.
    /// </summary>
    public static void IdentityIsWithheldUntilTheItemIsRevealed()
    {
        var body = Body(RoomsFile, "BuildCrystalSpherePayload");

        var revealed = body.IndexOf("varrevealed=occupied.All(c=>!c.IsHidden);", StringComparison.Ordinal);
        var payload = body.IndexOf("items.Add(newCrystalSphereItemPayload", StringComparison.Ordinal);

        Assert.True(
            revealed >= 0,
            "BuildCrystalSpherePayload must compute `var revealed = occupied.All(c => !c.IsHidden)` "
            + "once, and assign the same value to the payload's `revealed` field.");
        Assert.True(
            payload > revealed,
            "`revealed` has to be computed before the payload is created, so both identity fields can "
            + "be gated on it instead of being written unconditionally.");
        AssertContains(
            "revealed=revealed,",
            body,
            "the payload's own `revealed` flag must be the value the gate used, not a second walk.");

        AssertContains(
            "kind=revealed?item.GetType().Name.Replace(\"CrystalSphere\",string.Empty):null",
            body,
            "kind must be null while the item is hidden, or the board leaks what each item is.");
        AssertContains(
            "is_good=revealed?SafeReadBool(()=>item.IsGood):null",
            body,
            "is_good must be null while the item is hidden, or a curse is visible before it costs a "
            + "divination to find.");

        AssertContains(
            "x=occupied.Min(c=>c.X),",
            body,
            "x stays ungated: the item's cell is visible whether or not its identity is.");
        AssertContains(
            "cells=occupied.Select(c=>new[]{c.X,c.Y}).ToArray(),",
            body,
            "cells stays ungated: it is the occupancy the client plans against.");
        AssertContains(
            "hidden_cells=occupied.Where(c=>c.IsHidden).Select(c=>new[]{c.X,c.Y}).ToArray()",
            body,
            "hidden_cells stays ungated: it is what remains to be bought.");
    }

    /// <summary>
    /// The compact view hands the raw payload through instead of rebuilding it, so the gate cannot be
    /// sidestepped by the projection the default MCP read goes through.
    /// </summary>
    public static void CompactViewPassesTheGatedBoardThrough()
    {
        var agentView = AgentSourceFixture.Read(AgentViewFile);
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(agentView, "BuildAgentViewPayload"));

        AssertContains(
            "crystal_sphere=crystalSphere,",
            body,
            "the compact view must reuse the raw crystal_sphere payload; a rebuild here would be a "
            + "second place the reveal gate has to be written, and it already passes the raw one.");
        Assert.False(
            body.Contains("is_good", StringComparison.Ordinal),
            "the compact view must not mint an `is_good` of its own -- the field it exposes is the "
            + "gated one it was handed.");
    }

    /// <summary>The documentation a client reads states the same gate the code implements.</summary>
    public static void DocsSayIdentityArrivesWithTheReveal()
    {
        var api = AgentSourceFixture.Read(ApiDocFile);

        AssertContains(
            "| `kind` | string \\| null |",
            api,
            "docs/api.md must document `kind` as a nullable string on crystal_sphere.items[].");
        AssertContains(
            "| `is_good` | boolean \\| null |",
            api,
            "docs/api.md must document `is_good` as a nullable boolean on crystal_sphere.items[].");
        AssertContains(
            "在 `revealed` 变为 `true` 之前，`kind` 与 `is_good`",
            api,
            "docs/api.md must say the identity fields are null until the item is revealed, not merely "
            + "that they exist.");
        AssertContains(
            "键仍在，值不泄露身份",
            api,
            "docs/api.md must keep the key-presence promise: null, never a missing key.");

        var playbook = AgentSourceFixture.Read(PlaybookFile);
        AssertContains(
            "`kind` and `is_good` are `null`",
            playbook,
            "screen-playbooks.md must tell the agent that an unrevealed item's identity is null.");
        AssertContains(
            "occupancy, not identity",
            playbook,
            "screen-playbooks.md must say what the board does reveal, so the rule reads as semantics "
            + "instead of a missing field.");
    }

    /// <summary>
    /// The withheld identity stays on the wire as <c>null</c> rather than disappearing: this fix may
    /// not turn a documented key into a missing one, so the serializer the raw payload goes through
    /// has to keep writing nulls.
    /// </summary>
    public static void HiddenIdentityStaysOnTheWireAsNull()
    {
        var json = JsonHelper.Serialize(new { kind = (string?)null, is_good = (bool?)null });

        Assert.Contains("\"kind\":null", json);
        Assert.Contains("\"is_good\":null", json);

        var helper = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Server/JsonHelper.cs"));
        Assert.False(
            helper.Contains("DefaultIgnoreCondition", StringComparison.Ordinal),
            "GET /state is serialized by JsonHelper. An ignore-null condition there would silently drop "
            + "the two hidden fields instead of reporting them as null, which is the compatibility "
            + "promise docs/api.md makes.");
    }

    private static string ClassBody(string relativePath, string declaration)
    {
        return AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(AgentSourceFixture.Read(relativePath), declaration));
    }

    private static string Body(string relativePath, string methodName)
    {
        return AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.Read(relativePath), methodName));
    }

    private static void AssertContains(string expected, string actual, string message)
    {
        Assert.True(
            actual.Contains(expected, StringComparison.Ordinal),
            message + "\n  expected the source to contain: " + expected);
    }
}
