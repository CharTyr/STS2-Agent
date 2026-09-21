using System.Text.Json;

namespace STS2AIAgent.Multiplayer;

/// <summary>
/// A typed teammate signal attached to a team message.
/// </summary>
/// <remarks>
/// A team message used to be prose, and prose is easy to skim past: "I am killing the left one" is
/// not something a decision loop can act on reliably, and two players repeating it cannot tell
/// whether they mean the same enemy. A signal says what it is about in fields.
///
/// The message text stays required and stays the thing a person reads. The signal is optional and
/// additive, so an older client that sends only text keeps working, and a newer one that sends both
/// is understood by either side.
/// </remarks>
internal sealed record TeamIntent
{
    /// <summary>This player is about to hit a specific enemy, so the teammate should not duplicate it.</summary>
    public const string FocusFire = "focus_fire";

    /// <summary>Which enemy this side is treating as the priority for the turn.</summary>
    public const string TargetAnnounce = "target_announce";

    /// <summary>A potion slot, who is holding it, and who it is meant for.</summary>
    public const string PotionOwnership = "potion_ownership";

    public static readonly string[] KnownTypes = { FocusFire, TargetAnnounce, PotionOwnership };

    private TeamIntent(string type)
    {
        this.type = type;
    }

    public string type { get; }

    public int? enemy_index { get; init; }

    public string? player_id { get; init; }

    public int? potion_index { get; init; }

    public string? potion_id { get; init; }

    /// <summary>
    /// Reads a signal out of a request body, or returns null when none was sent.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The signal was present but malformed. A caller that sent one is told rather than having it
    /// silently dropped, because a dropped signal looks like the teammate ignoring an instruction.
    /// </exception>
    public static TeamIntent? Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("intent", out var raw) ||
            raw.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (raw.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("intent must be an object.");
        }

        if (!raw.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("intent.type must be a string.");
        }

        var type = typeValue.GetString() ?? string.Empty;
        if (Array.IndexOf(KnownTypes, type) < 0)
        {
            throw new ArgumentException($"intent.type must be one of {string.Join(", ", KnownTypes)}.");
        }

        var intent = new TeamIntent(type)
        {
            enemy_index = ReadInt(raw, "enemy_index"),
            player_id = ReadString(raw, "player_id"),
            potion_index = ReadInt(raw, "potion_index"),
            potion_id = ReadString(raw, "potion_id")
        };

        switch (type)
        {
            case FocusFire:
            case TargetAnnounce:
                if (intent.enemy_index is null)
                {
                    throw new ArgumentException($"intent.enemy_index is required for intent.type {type}.");
                }

                break;
            case PotionOwnership:
                if (intent.potion_index is null)
                {
                    throw new ArgumentException("intent.potion_index is required for intent.type potion_ownership.");
                }

                break;
        }

        return intent;
    }

    /// <summary>
    /// One line for the decision context. Only the fields the type actually carries are named, so a
    /// model reading it cannot mistake an absent field for a zero.
    /// </summary>
    public string Describe()
    {
        switch (type)
        {
            case FocusFire:
                return $"focus_fire: this player is attacking enemy_index {enemy_index} this turn; do not duplicate it";
            case TargetAnnounce:
                return $"target_announce: this player's priority is enemy_index {enemy_index}";
            case PotionOwnership:
                var owner = string.IsNullOrWhiteSpace(player_id) ? "unspecified player" : player_id;
                var potion = string.IsNullOrWhiteSpace(potion_id) ? "a potion" : potion_id;
                return $"potion_ownership: slot {potion_index} holds {potion}, spoken for by {owner}";
            default:
                return type;
        }
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        throw new ArgumentException($"intent.{name} must be an integer.");
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"intent.{name} must be a string.");
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
