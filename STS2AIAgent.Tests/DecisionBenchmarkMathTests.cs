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

            var cards = new List<CardMath>();
            foreach (var card in combat.GetProperty("hand").EnumerateArray())
            {
                var vsList = new List<CardVsEnemy>();
                if (card.TryGetProperty("vs", out var cells) && cells.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in cells.EnumerateArray())
                    {
                        var enemy = enemies.Single(value => value.Index == N(cell, "enemy"));
                        var damage = N(cell, "per_hit") * N(cell, "hits");
                        Assert.Equal(N(cell, "damage"), damage);
                        var result = CombatMathPolicy.Cell(new CardVsEnemy(enemy.Index, damage,
                            cell.GetProperty("ignores_block").GetBoolean()), enemy);
                        Assert.Equal(N(cell, "after_block"), result.AfterBlock);
                        Assert.Equal(cell.GetProperty("kills").GetBoolean(), result.Kills);
                        vsList.Add(new CardVsEnemy(enemy.Index, damage, cell.GetProperty("ignores_block").GetBoolean()));
                    }
                }

                var blockVal = 0;
                if (card.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        if (v.TryGetProperty("name", out var vn) && vn.GetString() == "Block"
                            && v.TryGetProperty("preview", out var vp) && vp.TryGetInt32(out var previewBlock))
                        {
                            blockVal = previewBlock;
                        }
                    }
                }

                cards.Add(new CardMath(
                    Index: N(card, "i"),
                    Cid: card.TryGetProperty("cid", out var cidProp) && cidProp.TryGetUInt32(out var cidVal) ? cidVal : null,
                    EnergyCost: card.TryGetProperty("energy_cost", out var ec) && ec.TryGetInt32(out var ecVal) ? ecVal : 0,
                    CostsX: card.TryGetProperty("costs_x", out var cx) && cx.GetBoolean(),
                    Playable: !card.TryGetProperty("playable", out var pl) || pl.GetBoolean(),
                    Block: blockVal,
                    Vs: vsList,
                    StarCost: card.TryGetProperty("star_cost", out var sc) && sc.TryGetInt32(out var scVal) ? scVal : 0,
                    StarCostsX: card.TryGetProperty("star_costs_x", out var scx) && scx.GetBoolean()));
            }

            var playerHp = ReadPlayerHp(player);
            var orbBlock = threat.TryGetProperty("end_of_turn_orb_block", out var ob) ? ob.GetInt32() : 0;
            var orbDamage = threat.TryGetProperty("end_of_turn_orb_damage", out var od) ? od.GetInt32() : 0;
            var stars = player.TryGetProperty("stars", out var st) && st.TryGetInt32(out var sv) ? sv : 0;
            var arrows = player.TryGetProperty("available_arrows", out var ar) && ar.TryGetInt32(out var av) ? av : 0;
            var input = new CombatMathInput(
                playerHp,
                N(player, "block"),
                N(player, "energy"),
                N(threat, "end_of_turn_self_damage"),
                enemies,
                cards,
                Stars: stars,
                AvailableArrows: arrows,
                EndOfTurnOrbBlock: orbBlock,
                EndOfTurnOrbDamage: orbDamage);
            var actual = CombatMathPolicy.Threat(input);
            Assert.Equal(N(threat, "incoming"), actual.Incoming);
            Assert.Equal(N(threat, "unblocked"), actual.Unblocked);
            Assert.Equal(N(threat, "hp_after"), actual.HpAfter);
            Assert.Equal(threat.GetProperty("lethal").GetBoolean(), actual.Lethal);
            Assert.Equal(orbBlock, actual.EndOfTurnOrbBlock);
            Assert.Equal(orbDamage, actual.EndOfTurnOrbDamage);
            if (threat.TryGetProperty("block_needed", out var bn))
            {
                Assert.Equal(bn.GetInt32(), actual.BlockNeeded);
            }
        }
        Assert.True(count >= 10, $"Expected at least 10 shared arithmetic snapshots, got {count}.");
    }

    private static int ReadPlayerHp(JsonElement player)
    {
        if (player.TryGetProperty("hp", out var hpProp))
        {
            if (hpProp.ValueKind == JsonValueKind.Number && hpProp.TryGetInt32(out var num))
            {
                return num;
            }

            if (hpProp.ValueKind == JsonValueKind.String)
            {
                var text = hpProp.GetString() ?? string.Empty;
                var slash = text.IndexOf('/');
                var head = slash > 0 ? text[..slash] : text;
                if (int.TryParse(head.Trim(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return 50;
    }

    private static int N(JsonElement value, string field) => value.GetProperty(field).GetInt32();
}
