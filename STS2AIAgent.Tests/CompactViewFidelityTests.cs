namespace STS2AIAgent.Tests;

/// <summary>
/// Source contracts for the compact agent view.
///
/// GameStateService.cs is not part of this test project (it depends on the live game assemblies),
/// so the compact payload cannot be built here. These tests pin the two things that can drift
/// silently and that a live run would only reveal late:
///
/// 1. the decision-critical keys really are emitted by the compact builders, and
/// 2. every new key mirrors a field name the raw payload already uses, so the compact view is not
///    inventing a parallel vocabulary.
///
/// Each assertion is written to fail if its field is deleted from the compact builder.
/// </summary>
internal static class CompactViewFidelityTests
{
    private const string StateSourcePath = "STS2AIAgent/Game/GameStateService.cs";

    public static void PowersReachTheCompactCombatView()
    {
        var source = ReadStateSource();
        var compactCombat = Body(source, "BuildAgentCombatPayload");
        var powerLine = Body(source, "FormatPowerLine");

        // Strength scales every hit, Vulnerable/Weak shift the numbers, Thorns punishes multi-hit
        // lines. Without powers the compact view had to guess all of that from memory.
        Assert.Contains(
            "powers=combat.player.powers.Select(power=>FormatPowerLine(power)).ToArray()",
            compactCombat,
            StringComparison.Ordinal);
        Assert.Contains(
            "powers=enemy.powers.Select(power=>FormatPowerLine(power)).ToArray()",
            compactCombat,
            StringComparison.Ordinal);

        // The raw payload owns the same key, so the compact view mirrors raw instead of inventing one.
        Assert.Contains("powers", ClassBody(source, "internal sealed class CombatPlayerPayload"), StringComparison.Ordinal);
        Assert.Contains("powers", ClassBody(source, "internal sealed class CombatEnemyPayload"), StringComparison.Ordinal);

        // Compact stays a short line built from the raw power identity and amount.
        Assert.Contains("power.power_id", powerLine, StringComparison.Ordinal);
        Assert.Contains("power.amount", powerLine, StringComparison.Ordinal);
        Assert.False(
            compactCombat.Contains("is_debuff", StringComparison.Ordinal),
            "compact powers must stay short lines, not the raw power objects");

        // Anti-dump: the compact combat body still omits raw-only detail.
        foreach (var rawOnly in new[] { "orb_capacity", "empty_orb_slots", "dynamic_values" })
        {
            Assert.False(
                compactCombat.Contains(rawOnly, StringComparison.Ordinal),
                "compact combat view must not inline raw field " + rawOnly);
        }
    }

    public static void IntentNumbersReachTheCompactCombatView()
    {
        var source = ReadStateSource();
        var compactCombat = Body(source, "BuildAgentCombatPayload");
        var rawIntent = ClassBody(source, "internal sealed class CombatEnemyIntentPayload");

        // Numbers only: the type, the label the UI shows, and the damage math behind it.
        Assert.Contains(
            "intents=enemy.intents.Select(intent=>new{i=intent.index,intent_type=intent.intent_type,label=intent.label,damage=intent.damage,hits=intent.hits,total_damage=intent.total_damage,status_card_count=intent.status_card_count}).ToArray(),",
            compactCombat,
            StringComparison.Ordinal);

        foreach (var numeric in new[] { "intent_type", "label", "damage", "hits", "total_damage", "status_card_count" })
        {
            Assert.Contains(numeric, rawIntent, StringComparison.Ordinal);
        }

        // Anti-dump: intent math does not drag the raw card-text fields along.
        Assert.False(
            compactCombat.Contains("resolved_rules_text", StringComparison.Ordinal),
            "compact combat view must not inline raw card text fields");
    }

    public static void CardAndRelicIdsReachTheCompactViews()
    {
        var source = ReadStateSource();

        // Metadata lookups match on id, so an id-less compact card or relic makes the game-data tools
        // unusable and forces the model back onto memory.
        Assert.Contains("card_id=card.card_id", Body(source, "BuildAgentHandCardPayload"), StringComparison.Ordinal);
        Assert.Contains("card_id=cardId", Body(source, "BuildAgentChoiceCardPayload"), StringComparison.Ordinal);
        Assert.Contains("card_id=cardId", Body(source, "BuildAgentPricedCardPayload"), StringComparison.Ordinal);
        Assert.Contains(
            "card_ids=group.Select(descriptor=>descriptor.card_id)",
            Body(source, "BuildAgentCardStacks"),
            StringComparison.Ordinal);
        Assert.Contains(
            "relic_ids=run.relics.Select(relic=>relic.relic_id).ToArray()",
            Body(source, "BuildAgentRunPayload"),
            StringComparison.Ordinal);
        Assert.Contains("relic_id=relic.relic_id", Body(source, "BuildAgentChestPayload"), StringComparison.Ordinal);

        // A merged deck/pile stack reports which cards it stands for, without widening the grouping
        // key, so the "*N" line keeps its meaning.
        var descriptor = ClassBody(source, "internal readonly record struct AgentCardDescriptor");
        // A positional record keeps its fields in the parameter list, so check the declaration text
        // rather than the braced body (which only holds GroupKey).
        Assert.Contains(
            "string[]mods,stringcard_id)",
            AgentSourceFixture.WithoutWhitespace(source),
            StringComparison.Ordinal);
        var groupKeyStart = descriptor.IndexOf("publicstringGroupKey=>", StringComparison.Ordinal);
        Assert.True(groupKeyStart >= 0, "AgentCardDescriptor must keep its GroupKey definition.");
        Assert.False(
            descriptor[groupKeyStart..].Contains("card_id", StringComparison.Ordinal),
            "card_id must be reported on the stack row, not folded into the grouping key");

        var modelBuilder = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(
            source, "private static AgentCardDescriptor BuildAgentCardDescriptor(CardModel card"));
        Assert.Contains("card.Id.Entry", modelBuilder, StringComparison.Ordinal);
        var deckBuilder = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(
            source, "private static AgentCardDescriptor BuildAgentCardDescriptor(DeckCardPayload card"));
        Assert.Contains("card.card_id", deckBuilder, StringComparison.Ordinal);

        // Raw side names, so the compact keys are traced back to an existing contract.
        foreach (var declaration in new[]
                 {
                     "internal sealed class CombatHandCardPayload",
                     "internal sealed class SelectionCardPayload",
                     "internal sealed class RewardCardOptionPayload",
                     "internal sealed class ShopCardPayload",
                     "internal sealed class DeckCardPayload"
                 })
        {
            Assert.Contains("card_id", ClassBody(source, declaration), StringComparison.Ordinal);
        }

        foreach (var declaration in new[]
                 {
                     "internal sealed class RunRelicPayload",
                     "internal sealed class ChestRelicOptionPayload"
                 })
        {
            Assert.Contains("relic_id", ClassBody(source, declaration), StringComparison.Ordinal);
        }
    }

    public static void OverlayAndPartyReachTheCompactView()
    {
        var source = ReadStateSource();
        var compactView = Body(source, "BuildAgentViewPayload");
        var compactCombat = Body(source, "BuildAgentCombatPayload");
        var compactRun = Body(source, "BuildAgentRunPayload");
        var compactModal = Body(source, "BuildAgentModalPayload");

        // "Resolve overlays before room flow" needs to know which room the overlay is covering.
        Assert.Contains("underlying_screen=modal.underlying_screen", compactModal, StringComparison.Ordinal);
        Assert.Contains(
            "underlying_screen",
            ClassBody(source, "internal sealed class ModalPayload"),
            StringComparison.Ordinal);

        // The unlock block reaches the compact top level with the raw payload's keys.
        Assert.Contains(
            "unlock=unlock==null?null:new{unlock_type=unlock.unlock_type,items=unlock.items,can_confirm=unlock.can_confirm}",
            compactView,
            StringComparison.Ordinal);
        Assert.Contains("unlock", ClassBody(source, "internal sealed class GameStatePayload"), StringComparison.Ordinal);
        var rawUnlock = ClassBody(source, "internal sealed class UnlockPayload");
        foreach (var key in new[] { "unlock_type", "items", "can_confirm" })
        {
            Assert.Contains(key, rawUnlock, StringComparison.Ordinal);
        }

        // Every party member's health line is visible, in combat and out of it.
        Assert.Contains(
            "players=combat.players.Select(other=>new{player_id=other.player_id,",
            compactCombat,
            StringComparison.Ordinal);
        Assert.Contains(
            "players=run.players.Select(other=>new{player_id=other.player_id,",
            compactRun,
            StringComparison.Ordinal);
        Assert.Contains("current_hp=other.current_hp", compactCombat, StringComparison.Ordinal);
        Assert.Contains("current_hp=other.current_hp", compactRun, StringComparison.Ordinal);

        foreach (var key in new[] { "slot_index", "is_local", "is_connected", "current_hp", "max_hp", "is_alive" })
        {
            Assert.Contains(key, ClassBody(source, "internal sealed class CombatPlayerSummaryPayload"), StringComparison.Ordinal);
            Assert.Contains(key, ClassBody(source, "internal sealed class RunPlayerSummaryPayload"), StringComparison.Ordinal);
        }

        // Anti-dump: the compact top level still omits the raw envelope.
        foreach (var rawOnly in new[] { "state_version", "in_combat" })
        {
            Assert.False(
                compactView.Contains(rawOnly, StringComparison.Ordinal),
                "compact agent_view must not inline raw field " + rawOnly);
        }
    }

    private static string ReadStateSource()
    {
        return AgentSourceFixture.Read(StateSourcePath);
    }

    private static string Body(string source, string methodName)
    {
        return AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, methodName));
    }

    private static string ClassBody(string source, string declaration)
    {
        return AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(source, declaration));
    }
}
