namespace STS2AIAgent.Agent;

/// <summary>
/// When the agent may speak on its own. Pure logic: no Godot, game or IO types, so the
/// C# core test executable can cover it without launching the game.
/// </summary>
internal enum ProactiveChatMoment
{
    None = 0,
    CombatStart = 1,
    CombatEnd = 2
}

internal readonly record struct ProactiveChatInput(
    bool Enabled,
    bool PlayRunning,
    string? BudgetBlock,
    int MessagesSent,
    TimeSpan? SinceLastMessage,
    ProactiveChatMoment Moment);

internal readonly record struct ProactiveChatDecision(bool Send, string? Reason);

internal static class ProactiveChatPolicy
{
    public const string CombatSituationKey = "COMBAT";
    public const int MaxMessagesPerSession = 6;
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(75);

    public static string SituationKey(string? screen, bool inCombat) =>
        inCombat ? CombatSituationKey : (screen ?? string.Empty).Trim().ToUpperInvariant();

    public static ProactiveChatMoment Observe(string? previousKey, string? currentKey)
    {
        if (string.Equals(previousKey, currentKey, StringComparison.Ordinal))
        {
            return ProactiveChatMoment.None;
        }

        if (string.Equals(currentKey, CombatSituationKey, StringComparison.Ordinal))
        {
            return ProactiveChatMoment.CombatStart;
        }

        return string.Equals(previousKey, CombatSituationKey, StringComparison.Ordinal)
            ? ProactiveChatMoment.CombatEnd
            : ProactiveChatMoment.None;
    }

    // Gate order is fixed: opt-in, moment, play state, budget, session cap, minimum interval.
    public static ProactiveChatDecision Decide(ProactiveChatInput input)
    {
        if (!input.Enabled)
        {
            return new ProactiveChatDecision(false, "opt-in off");
        }

        if (input.Moment == ProactiveChatMoment.None)
        {
            return new ProactiveChatDecision(false, "no moment");
        }

        if (!input.PlayRunning)
        {
            return new ProactiveChatDecision(false, "not playing");
        }

        if (!string.IsNullOrWhiteSpace(input.BudgetBlock))
        {
            return new ProactiveChatDecision(false, "budget");
        }

        if (input.MessagesSent >= MaxMessagesPerSession)
        {
            return new ProactiveChatDecision(false, "session cap");
        }

        if (input.SinceLastMessage is { } since && since < MinInterval)
        {
            return new ProactiveChatDecision(false, "min interval");
        }

        return new ProactiveChatDecision(true, null);
    }

    public static string BuildPrompt(ProactiveChatMoment moment) => moment switch
    {
        ProactiveChatMoment.CombatStart =>
            "A fight just started. Say one short line to your teammate about how you want to handle it.",
        ProactiveChatMoment.CombatEnd =>
            "The fight just ended. Say one short line to your teammate about what it cost and what is next.",
        _ => "Say one short line to your teammate about the current situation."
    };
}

/// <summary>
/// Per-session bookkeeping for proactive speech. It owns the message counter and the
/// last-send timestamp, so the runtime cannot drift from the policy's volume bounds:
/// deciding a send and recording it are the same call.
/// </summary>
/// <remarks>
/// A slot is consumed when a send is approved, not when it succeeds, so the cap bounds
/// model calls rather than delivered sentences.
/// </remarks>
internal sealed class ProactiveChatSession
{
    private int _messagesSent;
    private DateTimeOffset? _lastSentAt;

    public int MessagesSent => _messagesSent;

    public DateTimeOffset? LastSentAt => _lastSentAt;

    public ProactiveChatDecision Decide(
        bool enabled,
        bool playRunning,
        string? budgetBlock,
        ProactiveChatMoment moment,
        DateTimeOffset now)
    {
        var since = _lastSentAt is { } last ? now - last : (TimeSpan?)null;
        var decision = ProactiveChatPolicy.Decide(new ProactiveChatInput(
            enabled,
            playRunning,
            budgetBlock,
            _messagesSent,
            since,
            moment));
        if (decision.Send)
        {
            _messagesSent++;
            _lastSentAt = now;
        }

        return decision;
    }

    public void Reset()
    {
        _messagesSent = 0;
        _lastSentAt = null;
    }
}

internal static class ProactiveChatTones
{
    public const string Friendly = "friendly";
    public const string Calm = "calm";
    public const string Terse = "terse";
    public const string Default = Friendly;

    private const string SharedRules =
        "Proactive message rules: one short sentence at most; every claim must come from the current state; " +
        "never invent actions, card or target indexes, or outcomes; do not mention being prompted, this being " +
        "automatic, or the message being proactive; never offer to play for the player.";

    public static IReadOnlyList<(string Id, string Label)> Options { get; } = new[]
    {
        (Friendly, "轻松搭档"),
        (Calm, "沉稳参谋"),
        (Terse, "简短简报")
    };

    public static string Normalize(string? tone)
    {
        if (string.IsNullOrWhiteSpace(tone))
        {
            return Default;
        }

        var id = tone.Trim().ToLowerInvariant();
        foreach (var option in Options)
        {
            if (string.Equals(option.Id, id, StringComparison.Ordinal))
            {
                return option.Id;
            }
        }

        return Default;
    }

    public static string Label(string? tone)
    {
        var id = Normalize(tone);
        foreach (var option in Options)
        {
            if (string.Equals(option.Id, id, StringComparison.Ordinal))
            {
                return option.Label;
            }
        }

        return id;
    }

    public static string BuildSystemInstruction(string? tone)
    {
        var voice = Normalize(tone) switch
        {
            Calm => "Voice: steady, unhurried advisor. Name the risk first, then the plan.",
            Terse => "Voice: clipped field report. No pleasantries and no filler words.",
            _ => "Voice: warm co-op partner. Encouraging, first person, and still concrete."
        };
        return voice + "\n" + SharedRules;
    }
}

