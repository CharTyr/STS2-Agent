using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Multiplayer;

internal sealed class TeamConversation
{
    public const int MaxMessageLength = 2000;
    private readonly object _gate = new();
    private readonly List<ChatTurn> _turns = new();

    public IReadOnlyList<ChatTurn> Snapshot()
    {
        lock (_gate) return _turns.ToArray();
    }

    public void Add(string role, string text)
    {
        if (role is not ("user" or "assistant")) throw new ArgumentException("Invalid team speaker.");
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaxMessageLength)
            throw new ArgumentException($"队伍消息需要包含 1–{MaxMessageLength} 个字符。");
        lock (_gate)
        {
            _turns.Add(new ChatTurn { Role = role, Text = text });
            if (_turns.Count > 12) _turns.RemoveRange(0, _turns.Count - 12);
        }
    }

    public string? BuildDecisionContext()
    {
        var turns = Snapshot();
        return turns.Count == 0 ? null : JsonSerializer.Serialize(turns.Select(turn => new
        {
            speaker = turn.Role == "user" ? "human_teammate" : "ai_teammate",
            message = turn.Text
        }));
    }

    public void Clear()
    {
        lock (_gate) _turns.Clear();
    }
}
