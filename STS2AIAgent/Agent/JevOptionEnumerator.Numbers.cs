using System.Globalization;
using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// The arithmetic half of the option descriptions: what this frame's own numbers say about a card play
/// or about ending the turn.
/// </summary>
/// <remarks>
/// Split out of <c>JevOptionEnumerator.cs</c> on 2026-10-28 for the size budget, not for a second
/// concern: the enumerator still builds the options, and this file only appends the numbers to their
/// descriptions. Everything is read from the same snapshot the options come from -- the game's damage
/// preview in <c>combat.hand[].vs</c>, the threat summary in <c>combat.threat</c>, the card's own block
/// preview in <c>values[]</c> -- so nothing here infers a number the game did not preview. A card with
/// no preview, or a multi-enemy card with no chosen target, gets no numbers rather than a guess.
///
/// The purpose is narrow: the fast model is good at picking, bad at subtraction, and an option line
/// that already says "9 dmg | kill | 3 over" is one it cannot get wrong.
/// </remarks>
internal static partial class JevOptionEnumerator
{
    /// <summary>Ceiling on the appended arithmetic, so one option line cannot grow unbounded.</summary>
    private const int MaxNumbersLength = 60;

    private static string WithNumbers(string description, string? numbers)
    {
        return string.IsNullOrEmpty(numbers) ? description : $"{description} ({numbers})";
    }

    /// <summary>Joins the parts while they fit <see cref="MaxNumbersLength"/>; null when none fit.</summary>
    private static string? JoinNumbers(List<string> parts)
    {
        while (parts.Count > 0)
        {
            var text = string.Join(" | ", parts);
            if (text.Length <= MaxNumbersLength)
            {
                return text;
            }

            parts.RemoveAt(parts.Count - 1);
        }

        return null;
    }

    /// <summary>
    /// The frame's own arithmetic for one card play, or null when the frame does not carry it.
    /// </summary>
    private static string? CardNumbers(
        JsonElement card,
        int? target,
        JsonElement combat,
        Dictionary<int, int> enemyHp)
    {
        var parts = new List<string>();
        if (TryReadPreview(card, target, out var damage, out var afterBlock, out var kills))
        {
            if (damage is not { } dealt)
            {
                return null;
            }

            parts.Add($"{dealt} dmg");
            if (kills == true)
            {
                parts.Add("kill");
                if (target is { } killed
                    && afterBlock is { } remaining
                    && enemyHp.TryGetValue(killed, out var hp)
                    && remaining > hp)
                {
                    parts.Add($"{remaining - hp} over");
                }
            }
            else if (target is { } survivor
                     && afterBlock is { } left
                     && enemyHp.TryGetValue(survivor, out var survivorHp)
                     && survivorHp > left)
            {
                parts.Add($"{survivorHp - left} hp left");
            }

            return JoinNumbers(parts);
        }

        // A block card has no damage preview; it can still be priced against the incoming damage.
        var block = ReadCardBlock(card);
        if (block is not { } blockValue
            || blockValue <= 0
            || combat.ValueKind != JsonValueKind.Object
            || !TryGetProperty(combat, "threat", out var threat)
            || threat.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        parts.Add($"block {blockValue}");
        if (ReadInt(threat, "incoming") is { } incoming)
        {
            parts.Add($"incoming {incoming}");
        }

        if (ReadInt(threat, "unblocked") is { } unblocked)
        {
            parts.Add($"still {Math.Max(0, unblocked - blockValue)}");
        }

        return JoinNumbers(parts);
    }

    /// <summary>The one <c>vs</c> cell this option plays into, if the frame has an unambiguous one.</summary>
    private static bool TryReadPreview(
        JsonElement card,
        int? target,
        out int? damage,
        out int? afterBlock,
        out bool? kills)
    {
        damage = null;
        afterBlock = null;
        kills = null;
        if (!TryGetArray(card, "vs", out var previews))
        {
            return false;
        }

        JsonElement? chosen = null;
        foreach (var preview in previews.EnumerateArray())
        {
            if (preview.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (target is { } index)
            {
                if (ReadInt(preview, "enemy") == index)
                {
                    chosen = preview;
                    break;
                }

                continue;
            }

            // No target chosen: only a single-enemy preview is unambiguous.
            if (chosen.HasValue || previews.GetArrayLength() != 1)
            {
                return false;
            }

            chosen = preview;
        }

        if (chosen is not { } cell)
        {
            return false;
        }

        damage = ReadInt(cell, "damage");
        afterBlock = ReadInt(cell, "after_block");
        kills = ReadBoolOrNull(cell, "kills");
        return true;
    }

    /// <summary>The card's own block preview (<c>values[].preview</c>), when it has one.</summary>
    private static int? ReadCardBlock(JsonElement card)
    {
        if (!TryGetArray(card, "values", out var values))
        {
            return null;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object
                || ReadString(value, "name") is not { } name
                || !name.Contains("block", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var preview = ReadInt(value, "preview") ?? ReadInt(value, "base_value");
            if (preview is { } block && block > 0)
            {
                return block;
            }
        }

        return null;
    }

    /// <summary><c>end_turn (take 13 | HP 24→11)</c>, or the bare action when the frame has no threat.</summary>
    private static string DescribeEndTurn(JsonElement state, string action)
    {
        if (!TryGetProperty(state, "combat", out var combat)
            || !TryGetProperty(combat, "threat", out var threat)
            || threat.ValueKind != JsonValueKind.Object)
        {
            return action;
        }

        var parts = new List<string>();
        if (ReadBool(threat, "lethal"))
        {
            parts.Add("lethal");
        }

        if (ReadInt(threat, "unblocked") is { } unblocked)
        {
            parts.Add($"take {unblocked}");
        }

        if (ReadInt(threat, "hp_after") is { } hpAfter && ReadPlayerHp(combat) is { } hp)
        {
            parts.Add($"HP {hp}\u2192{hpAfter}");
        }

        var numbers = JoinNumbers(parts);
        return numbers == null ? action : $"{action} ({numbers})";
    }

    private static int? ReadPlayerHp(JsonElement combat)
    {
        return TryGetProperty(combat, "player", out var player) ? ReadHpText(player) : null;
    }

    /// <summary>Current HP out of the compact <c>"24/40"</c> form; null when it is not that shape.</summary>
    private static int? ReadHpText(JsonElement item)
    {
        var text = ReadString(item, "hp");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var slash = text.IndexOf('/');
        var head = slash >= 0 ? text[..slash] : text;
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp) ? hp : null;
    }

    private static Dictionary<int, int> ReadEnemyHp(JsonElement combat)
    {
        var hp = new Dictionary<int, int>();
        if (!TryGetArray(combat, "enemies", out var enemies))
        {
            return hp;
        }

        foreach (var enemy in enemies.EnumerateArray())
        {
            if (enemy.ValueKind == JsonValueKind.Object
                && TryReadIndex(enemy, out var index)
                && ReadHpText(enemy) is { } value)
            {
                hp[index] = value;
            }
        }

        return hp;
    }

    private static int? ReadInt(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static bool? ReadBoolOrNull(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
