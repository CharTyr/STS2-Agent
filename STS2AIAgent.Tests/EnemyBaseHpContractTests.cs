namespace STS2AIAgent.Tests;

/// <summary>
/// Contract for the enemy base-HP bridge.
///
/// Monster metadata (monsters.min_hp/max_hp) reports the single-player base roll, while the live
/// combat state reports MaxHp after multiplayer scaling. Without the unscaled roll in the payload the
/// two numbers look contradictory (for example FUZZY_WURM_CRAWLER 55/57 metadata vs 121/121 live).
/// GameStateService.cs is not compiled into this project, so these tests pin the source contract: the
/// base roll is emitted on both the raw and the compact enemy payload, it comes from
/// Creature.MonsterMaxHpBeforeModification, and the existing max_hp assignment is untouched.
/// </summary>
internal static class EnemyBaseHpContractTests
{
    private const string StateSourcePath = "STS2AIAgent/Game/GameStateService.cs";

    public static void RawEnemyPayloadCarriesTheBaseRoll()
    {
        var buildEnemy = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.Read(StateSourcePath), "BuildEnemyPayload"));

        // The base roll is read straight off the creature; the field is nullable, so a player, a pet or
        // a creature whose value is not yet set yields null rather than a fallback to the scaled MaxHp.
        Assert.Contains("base_max_hp=enemy.MonsterMaxHpBeforeModification", buildEnemy, StringComparison.Ordinal);

        // The pre-existing live value is unchanged: same source property, no fallback or rewrite.
        Assert.Contains("max_hp=enemy.MaxHp,", buildEnemy, StringComparison.Ordinal);
        Assert.False(
            buildEnemy.Contains(",max_hp=enemy.MonsterMaxHpBeforeModification", StringComparison.Ordinal),
            "max_hp must stay the scaled live value, not the unscaled base roll");
    }

    public static void CompactEnemyPayloadMirrorsTheBaseRoll()
    {
        var compactCombat = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.Read(StateSourcePath), "BuildAgentCombatPayload"));

        // The compact view exposes the same key name as the raw payload, forwarded verbatim.
        Assert.Contains("base_max_hp=enemy.base_max_hp", compactCombat, StringComparison.Ordinal);
        // The live hp line is unchanged.
        Assert.Contains("hp=$\"{enemy.current_hp}/{enemy.max_hp}\"", compactCombat, StringComparison.Ordinal);
    }

    public static void EnemyPayloadTypeDeclaresNullableBaseMaxHp()
    {
        var payload = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(
            AgentSourceFixture.Read(StateSourcePath), "internal sealed class CombatEnemyPayload"));

        // New field only: the base roll is a nullable int so "unset" stays distinguishable from zero.
        Assert.Contains("publicint?base_max_hp{get;init;}", payload, StringComparison.Ordinal);
        // Backward compatibility: the existing live field keeps its type and name.
        Assert.Contains("publicintmax_hp{get;init;}", payload, StringComparison.Ordinal);
    }
}
