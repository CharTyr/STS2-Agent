using System.Text.Json;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class DecisionBenchmarkMathTests
{
    public static void ReproducesEveryArithmeticFixture()
    {
        using var doc = JsonDocument.Parse(AgentSourceFixture.Read("docs/decision-benchmark.json"));
        var count = 0;
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!item.TryGetProperty("calculator", out var marker) || marker.GetString() != "combat_math") continue;
            count++;
            var combat = item.GetProperty("state").GetProperty("combat");
            var player = combat.GetProperty("player");
            var threat = combat.GetProperty("threat");
            var enemies = combat.GetProperty("enemies").EnumerateArray().Select(enemy => new EnemyMath(
                N(enemy, "i"), N(enemy, "hp"), N(enemy, "block"), enemy.GetProperty("alive").GetBoolean(),
                enemy.GetProperty("hittable").GetBoolean(), enemy.GetProperty("intents").EnumerateArray()
                    .Select(intent => new IntentMath(N(intent, "total_damage"), N(intent, "hits"), false)).ToArray())).ToArray();
            var input = new CombatMathInput(50, N(player, "block"), N(player, "energy"),
                N(threat, "end_of_turn_self_damage"), enemies, Array.Empty<CardMath>());
            var actual = CombatMathPolicy.Threat(input);
            Assert.Equal(N(threat, "incoming"), actual.Incoming);
            Assert.Equal(N(threat, "unblocked"), actual.Unblocked);
            Assert.Equal(N(threat, "hp_after"), actual.HpAfter);
            Assert.Equal(threat.GetProperty("lethal").GetBoolean(), actual.Lethal);
            foreach (var card in combat.GetProperty("hand").EnumerateArray())
            {
                if (!card.TryGetProperty("vs", out var cells) || cells.ValueKind != JsonValueKind.Array) continue;
                foreach (var cell in cells.EnumerateArray())
                {
                    var enemy = enemies.Single(value => value.Index == N(cell, "enemy"));
                    var damage = N(cell, "per_hit") * N(cell, "hits");
                    Assert.Equal(N(cell, "damage"), damage);
                    var result = CombatMathPolicy.Cell(new CardVsEnemy(enemy.Index, damage,
                        cell.GetProperty("ignores_block").GetBoolean()), enemy);
                    Assert.Equal(N(cell, "after_block"), result.AfterBlock);
                    Assert.Equal(cell.GetProperty("kills").GetBoolean(), result.Kills);
                }
            }
        }
        Assert.True(count >= 8, "At least eight shared arithmetic snapshots must remain exercised.");
    }

    private static int N(JsonElement value, string field) => value.GetProperty(field).GetInt32();
}
