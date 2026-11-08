using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>Display-only intent captured from the same fresh state that validates an action.</summary>
internal sealed record LiveIntent(
    string Action, uint? CardCid, string? CardName, int? TargetIndex, string? TargetName,
    string? Reason, string Screen, DateTimeOffset UpdatedAt)
{
    public string PlanPreview { get; init; } = "";

    public static LiveIntent Create(string argumentsJson, string compactJson, DateTimeOffset now)
    {
        using var args = JsonDocument.Parse(argumentsJson);
        using var state = JsonDocument.Parse(compactJson);
        var a = args.RootElement;
        var s = state.RootElement;
        var action = Text(a, "action") ?? "";
        var cid = Number(a, "card_cid");
        var index = Number(a, "card_index");
        var target = Number(a, "target_index");
        string? cardName = null, targetName = null;
        var enemyTarget = false;
        if (s.TryGetProperty("combat", out var combat) && combat.ValueKind == JsonValueKind.Object)
        {
            if (combat.TryGetProperty("hand", out var hand) && hand.ValueKind == JsonValueKind.Array)
                foreach (var card in hand.EnumerateArray())
                    if (cid != null ? Number(card, "cid") == cid : index != null && Number(card, "i") == index)
                    {
                        cid = Number(card, "cid");
                        cardName = Text(card, "name") ?? Text(card, "line") ?? Text(card, "card_id");
                        enemyTarget = Text(card, "target") is "enemy" or "enemies";
                        break;
                    }
            // Only describe an enemy when this card's target explicitly belongs to the enemy index space.
            if (enemyTarget && target != null && combat.TryGetProperty("enemies", out var enemies) && enemies.ValueKind == JsonValueKind.Array)
                foreach (var enemy in enemies.EnumerateArray())
                    if (Number(enemy, "i") == target)
                    { targetName = Text(enemy, "name") ?? Text(enemy, "id"); break; }
        }
        var plan = TurnPlanPolicy.Parse(argumentsJson);
        var preview = plan.Then.Count == 0 ? "" : string.Join(" → ", new[] { action }
            .Concat(plan.Then.Select(step => step.Action)).Select(PlayerLabels.Action));
        return new(action, cid is >= 0 ? (uint)cid.Value : null, cardName, target, targetName,
            Clip(Text(a, "reason"), 120), Text(s, "screen") ?? "UNKNOWN", now) { PlanPreview = preview };
    }

    internal static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    internal static int? Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;
    private static string? Clip(string? text, int max)
    {
        if (text is null) return null;
        var safe = DiagnosticExport.Redact(text.Replace('\n', ' ').Replace('\r', ' '));
        return safe[..Math.Min(max, safe.Length)];
    }
}

internal static class HudModel
{
    public static bool ShowIntent(LiveIntent? intent, string screen, bool running, DateTimeOffset now) =>
        running && intent != null && intent.Screen == screen && now - intent.UpdatedAt < TimeSpan.FromSeconds(4);

    public static string IntentText(LiveIntent intent) =>
        string.Join(" → ", new[] { intent.CardName ?? PlayerLabels.Action(intent.Action), intent.TargetName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
