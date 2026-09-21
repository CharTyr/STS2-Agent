using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

internal interface IGameBridge
{
    Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken);

    Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken);

    Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The compact state and the action descriptors for one decision, from a single state read.
    /// JSON shape: <c>{"state": {...compact state...}, "available_actions": [descriptors]}</c>.
    /// The action names a legality check needs are the compact state's own <c>available_actions</c>.
    /// </summary>
    Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken);

    Task<string> GetScreenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Execute one action and return its response. The state in that response is the compact
    /// <c>agent_view</c> unless <paramref name="rawState"/> asks for the full payload instead.
    /// </summary>
    Task<string> ActAsync(
        string action,
        int? cardIndex,
        int? targetIndex,
        int? optionIndex,
        int? x,
        int? y,
        string? tool,
        CancellationToken cancellationToken,
        bool rawState = false);

    Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken);

    Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken);

    Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken);

    Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken);

    Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken);
}

internal sealed class AgentTurnResult
{
    public string? AssistantText { get; init; }

    public string? Reasoning { get; init; }

    public string? Acted { get; init; }

    public string? ActResultJson { get; init; }

    public string? Error { get; init; }

    public bool WaitingForGame { get; init; }

    /// <summary>
    /// The turn executed an action but the game never reported a settled result inside the wait
    /// window. The action response stays "pending", so the retry policy must not treat this as a
    /// failure -- it only stops a session after a run of them.
    /// </summary>
    public bool ExecutedUnsettled { get; init; }

    /// <summary>
    /// Fingerprint of the compact state observed right after the action, for the no-progress guard.
    /// Null when the turn executed nothing or no state could be read.
    /// </summary>
    public string? StateFingerprint { get; init; }

    /// <summary>
    /// The turn stopped to let the human player act — the companion map-vote path yields to the
    /// player's node choice — so the overlay reports a player wait instead of a stall.
    /// </summary>
    public bool WaitingForPlayer { get; init; }

    public bool RequiresConfiguration { get; init; }

    public int ToolRounds { get; init; }

    public LlmUsage? Usage { get; init; }

    public int RequestsSpent { get; init; }
}

internal sealed class ChatTurn
{
    public required string Role { get; init; }

    public required string Text { get; init; }
}

internal sealed class ChatOptions
{
    // Team conversation must stay read-only even when its text asks to play.
    public bool TeammateConversation { get; init; }
    public bool AttachState { get; init; } = true;

    public bool AttachScreenshot { get; init; }

    public bool AllowAct { get; init; }

    // A read-only chat must not act even when AllowAct or the message text asks for play.
    public bool ReadOnly { get; init; }

    // Extra system instruction for this turn, e.g. the selected proactive-chat tone.
    public string? ExtraSystemInstruction { get; init; }
}
