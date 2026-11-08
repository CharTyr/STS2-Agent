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

/// <summary>
/// What the host can honestly say about a message it is still waiting on.
/// </summary>
/// <remarks>
/// The host knows two facts while a message is in flight: how long it has been waiting, and -- from
/// the companion's own <c>/state</c> -- whether that character is in combat. It does not know when
/// the companion dequeued the message or started answering, so the line reports the wait and the one
/// fact that explains a slow reply instead of inventing a phase.
///
/// Pure and Godot-free: the elapsed arithmetic, the clock-jump clamp and the wording are covered by
/// <c>TeamMessageProgressTests</c> without a game, a teammate or a real clock.
/// </remarks>
internal static class TeamMessageProgress
{
    /// <summary>Ceiling for a printed wait, so a wall-clock jump cannot claim hours of waiting.</summary>
    public const int MaxSeconds = 999;

    /// <summary>
    /// Whole seconds between the send and <paramref name="nowMs"/>. A missing start (0) or a clock
    /// that ran backwards reads as 0 rather than as a huge wait.
    /// </summary>
    public static int ElapsedSeconds(long startedAtMs, long nowMs)
    {
        if (startedAtMs <= 0)
        {
            return 0;
        }

        var elapsed = nowMs - startedAtMs;
        if (elapsed <= 0)
        {
            return 0;
        }

        var seconds = elapsed / 1000;
        return seconds >= MaxSeconds ? MaxSeconds : (int)seconds;
    }

    /// <summary>
    /// The status line while a message is pending. <paramref name="teammateInCombat"/> comes from the
    /// teammate's own state: the host can be on the map while the teammate is still fighting, and the
    /// host's screen says nothing about whether the message is waiting behind a turn.
    /// </summary>
    public static string DescribeWaiting(bool teammateInCombat, int elapsedSeconds)
    {
        var seconds = elapsedSeconds < 0 ? 0 : elapsedSeconds > MaxSeconds ? MaxSeconds : elapsedSeconds;
        return teammateInCombat
            ? Loc.T("队友正在战斗，消息会在它这次行动结束后处理（已等待 {0} 秒）", seconds)
            : Loc.T("消息已送达，等待队友回复（已等待 {0} 秒）", seconds);
    }
}
