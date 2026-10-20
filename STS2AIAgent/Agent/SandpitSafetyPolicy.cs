using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>A deterministic response to the local player's live Sandpit countdown.</summary>
internal static class SandpitSafetyPolicy
{
    internal readonly record struct Decision(bool Critical, int? EscapeIndex);

    public static Decision Read(string stateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("screen", out var screen) || screen.GetString() != "COMBAT"
                || !root.TryGetProperty("combat", out var combat) || combat.ValueKind != JsonValueKind.Object
                || !combat.TryGetProperty("lethal_risks", out var risks) || risks.ValueKind != JsonValueKind.Array)
                return default;

            var critical = risks.EnumerateArray().Any(risk =>
                risk.TryGetProperty("risk_id", out var id) && id.GetString() == "sandpit_countdown"
                && risk.TryGetProperty("will_kill_player", out var kills) && kills.ValueKind == JsonValueKind.True
                && risk.TryGetProperty("power_amount", out var count) && count.TryGetInt32(out var amount) && amount <= 1);
            if (!critical) return default;
            if (!combat.TryGetProperty("hand", out var hand) || hand.ValueKind != JsonValueKind.Array)
                return new(true, null);

            // Only cards the native game declares playable may be selected. Current cost matters:
            // each use increases this card's cost, and effects can also make it free.
            int? selected = null;
            var bestCost = int.MaxValue;
            foreach (var card in hand.EnumerateArray())
            {
                if (!card.TryGetProperty("card_id", out var id) || id.GetString() != "FRANTIC_ESCAPE"
                    || !card.TryGetProperty("playable", out var playable) || playable.ValueKind != JsonValueKind.True
                    || !card.TryGetProperty("i", out var index) || !index.TryGetInt32(out var i) || i < 0
                    || !card.TryGetProperty("energy_cost", out var cost) || !cost.TryGetInt32(out var energy) || energy < 0)
                    continue;
                if (energy < bestCost) { selected = i; bestCost = energy; }
            }
            return new(true, selected);
        }
        catch (JsonException) { return default; }
    }

    public static string? Reject(Decision decision, string action, int? cardIndex)
    {
        if (!decision.Critical) return null;
        if (action == "play_card" && cardIndex == decision.EscapeIndex && cardIndex.HasValue) return null;
        if (decision.EscapeIndex is int escape)
            return Loc.T("沙坑将在下个敌方回合杀死本地玩家。请先打出 card_index={0} 的 FRANTIC_ESCAPE，再执行其他动作。", escape);
        return action == "end_turn"
            ? Loc.T("沙坑即将归零，结束回合会被吞噬；当前没有可打出的狂乱逃离。已停止自动游玩，请检查抽牌、药水或其他保命手段。")
            : null;
    }

    public static string DescribeCountdown(int? amount) => Loc.T(
        "沙坑倒计时 {0}：敌方回合开始时减1，归零会被吞噬并直接死亡。狂乱逃离增加计数，可延后死亡；格挡和缓冲不能抵消吞噬。", amount);
}
