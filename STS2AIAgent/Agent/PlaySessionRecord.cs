using System.Text.Json.Serialization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The persisted form of one run's play session: the conversation, the recent-decision memory, and
/// the dual-layer strategy, keyed by the run's seed.
/// </summary>
/// <remarks>
/// A run's "context" is not a single transcript but three things that are restored to three different
/// places -- the chat list the overlay renders, the decision sequence <see cref="ContextCompaction"/>
/// summarizes, and the <see cref="PlayStrategy"/> the Jev decider follows. This record bundles them so
/// they are saved and loaded as one unit, which is what keeps a continued run coherent: the player sees
/// the same conversation, the planner sees the same recent decisions, and Jev resumes the same posture.
///
/// All collections are optional on read so an older or partial file still loads; the store normalizes
/// nulls to empty on the way in. The record carries no secret: chat text and decision reasons are
/// redacted before they reach disk (see <see cref="PlaySessionStore"/>).
/// </remarks>
internal sealed record PlaySessionRecord
{
    /// <summary>The run seed this session belongs to; the file name. Never <c>run_unknown</c>.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The character the run is played as, for display and a sanity check on restore.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; init; }

    /// <summary>ISO-8601 timestamp of the last write; informational only.</summary>
    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    /// <summary>The conversation turns, oldest first, in the same shape the overlay renders.</summary>
    [JsonPropertyName("chat")]
    public List<ChatTurn>? Chat { get; init; }

    /// <summary>The recent decisions feeding <see cref="ContextCompaction"/>, oldest first.</summary>
    [JsonPropertyName("decisions")]
    public List<DecisionLogEntry>? Decisions { get; init; }

    /// <summary>The dual-layer play strategy, or null when the run never set one.</summary>
    [JsonPropertyName("strategy")]
    public PlayStrategy? Strategy { get; init; }
}
