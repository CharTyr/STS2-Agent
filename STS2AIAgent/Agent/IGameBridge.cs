namespace STS2AIAgent.Agent;

internal interface IGameBridge
{
    Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken);

    Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAvailableActionNamesAsync(CancellationToken cancellationToken);

    Task<string> GetScreenAsync(CancellationToken cancellationToken);

    Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, CancellationToken cancellationToken);

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

    public int ToolRounds { get; init; }
}

internal sealed class ChatTurn
{
    public required string Role { get; init; }

    public required string Text { get; init; }
}

internal sealed class ChatOptions
{
    public bool AttachState { get; init; } = true;

    public bool AttachScreenshot { get; init; }

    public bool AllowAct { get; init; }
}
