using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

internal sealed class TeamConversation
{
    public const int MaxMessageLength = 2000;
    private readonly object _gate = new();
    private readonly List<Turn> _turns = new();

    private readonly record struct Turn(string Role, string Text, TeamIntent? Intent);

    public IReadOnlyList<ChatTurn> Snapshot()
    {
        lock (_gate) return _turns.Select(turn => new ChatTurn { Role = turn.Role, Text = turn.Text }).ToArray();
    }

    public void Add(string role, string text, TeamIntent? intent = null)
    {
        if (role is not ("user" or "assistant")) throw new ArgumentException("Invalid team speaker.");
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaxMessageLength)
            throw new ArgumentException(Loc.T("队伍消息需要包含 1–{0} 个字符。", MaxMessageLength));
        lock (_gate)
        {
            _turns.Add(new Turn(role, text, intent));
            if (_turns.Count > 12) _turns.RemoveRange(0, _turns.Count - 12);
        }
    }

    /// <summary>
    /// The teammate conversation as the decision loop sees it.
    /// </summary>
    /// <remarks>
    /// A signal is emitted as its own `signal` line next to the message it came with, because the
    /// two answer different questions: the text is what a person chose to say, and the signal is what
    /// the sender wants the other side to do about it. Folding one into the other is how a typed
    /// instruction ends up being read as conversation.
    /// </remarks>
    public string? BuildDecisionContext()
    {
        var turns = SnapshotWithIntents();
        return turns.Count == 0 ? null : JsonSerializer.Serialize(turns.Select(turn => new
        {
            speaker = turn.Role == "user" ? "human_teammate" : "ai_teammate",
            message = turn.Text,
            signal = turn.Intent?.Describe()
        }));
    }

    /// <summary>
    /// The newest signal of one type, or null when none was sent. Used by the focus-fire rule, which
    /// needs the last word on which enemy is taken rather than the whole history.
    /// </summary>
    public TeamIntent? LatestIntent(string type)
    {
        lock (_gate)
        {
            for (var index = _turns.Count - 1; index >= 0; index--)
            {
                if (_turns[index].Intent is { } intent && string.Equals(intent.type, type, StringComparison.Ordinal))
                {
                    return intent;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The newest signal that names an enemy this side is committed to, whichever of the two forms it
    /// arrived in. A later announcement on a different enemy supersedes an earlier one, so the answer
    /// is the newest rather than the first.
    /// </summary>
    public TeamIntent? LatestFocusFire()
    {
        lock (_gate)
        {
            for (var index = _turns.Count - 1; index >= 0; index--)
            {
                if (_turns[index].Intent is { } intent &&
                    (string.Equals(intent.type, TeamIntent.FocusFire, StringComparison.Ordinal) ||
                     string.Equals(intent.type, TeamIntent.TargetAnnounce, StringComparison.Ordinal)))
                {
                    return intent;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The team context the decision loop receives: the conversation, plus the focus-fire constraint
    /// when one applies.
    /// </summary>
    public string? BuildTeamContext()
    {
        var context = BuildDecisionContext();
        var instruction = FocusFireInstruction();
        if (context == null)
        {
            return instruction;
        }

        return instruction == null ? context : context + "\n" + instruction;
    }

    /// <summary>
    /// What the teammate's announced target means for this turn, or null when nothing was announced.
    /// </summary>
    /// <remarks>
    /// This is a stated constraint in the prompt, not a hard override of the chosen action. The model
    /// can see the enemy's health and the lethal lines in the live state, so it can tell "do not
    /// duplicate this" from "finish it"; refusing the action in code would take that judgement away
    /// and could strand a kill the team already paid for.
    /// </remarks>
    public string? FocusFireInstruction()
    {
        var intent = LatestFocusFire();
        if (intent?.enemy_index is not { } enemyIndex)
        {
            return null;
        }

        return Loc.T(
            "队友本回合在打 enemy_index {0}。除非那个敌人已经必死或只剩最后一击，不要把伤害再倾泻在它身上；优先选另一个目标，避免两人重复集火把伤害溢出掉。",
            enemyIndex);
    }

    public void Clear()
    {
        lock (_gate) _turns.Clear();
    }

    private IReadOnlyList<Turn> SnapshotWithIntents()
    {
        lock (_gate) return _turns.ToArray();
    }
}
