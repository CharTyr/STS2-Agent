using STS2AIAgent.Config;

namespace STS2AIAgent.Llm;

internal interface ILlmClient
{
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);

    Task<string> PingAsync(string model, CancellationToken cancellationToken);
}

internal interface ILlmClientFactory
{
    ILlmClient Create(LlmEndpoint endpoint);
}

internal sealed class DefaultLlmClientFactory : ILlmClientFactory
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public ILlmClient Create(LlmEndpoint endpoint)
    {
        return new OpenAiCompatibleClient(endpoint, httpClient: SharedHttp);
    }
}

internal sealed class LlmRequest
{
    public required string Model { get; init; }

    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    public IReadOnlyList<LlmTool>? Tools { get; init; }

    public ThinkingIntensity Thinking { get; init; } = ThinkingIntensity.Medium;

    public string ThinkingMode { get; init; } = "auto";

    public bool Stream { get; init; } = true;
}

internal sealed class LlmMessage
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    public byte[]? ImageJpeg { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    public static LlmMessage System(string content) => new() { Role = "system", Content = content };

    public static LlmMessage User(string content, byte[]? imageJpeg = null) =>
        new() { Role = "user", Content = content, ImageJpeg = imageJpeg };

    public static LlmMessage Assistant(string? content, IReadOnlyList<LlmToolCall>? toolCalls = null) =>
        new() { Role = "assistant", Content = content, ToolCalls = toolCalls };

    public static LlmMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}

internal sealed class LlmTool
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required object Parameters { get; init; }
}

internal sealed class LlmToolCall
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string ArgumentsJson { get; init; }
}

internal sealed class LlmUsage
{
    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int TotalTokens { get; init; }

    public static LlmUsage Empty => new();

    public static LlmUsage operator +(LlmUsage left, LlmUsage right)
    {
        return new LlmUsage
        {
            PromptTokens = left.PromptTokens + right.PromptTokens,
            CompletionTokens = left.CompletionTokens + right.CompletionTokens,
            TotalTokens = (left.TotalTokens > 0 || right.TotalTokens > 0)
                ? (left.TotalTokens + right.TotalTokens)
                : (left.PromptTokens + right.PromptTokens + left.CompletionTokens + right.CompletionTokens)
        };
    }

    public static LlmUsage? Combine(LlmUsage? left, LlmUsage? right)
    {
        if (left == null) return right;
        if (right == null) return left;
        return left + right;
    }

    public override string ToString() => $"Total: {TotalTokens} (Prompt: {PromptTokens}, Completion: {CompletionTokens})";
}

internal sealed class LlmCompletion
{
    public string? Content { get; init; }

    public string? Reasoning { get; init; }

    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = Array.Empty<LlmToolCall>();

    public LlmUsage? Usage { get; init; }
}

internal sealed class LlmException : Exception
{
    public int? StatusCode { get; }

    public LlmException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public LlmException(string message) : base(message)
    {
    }

    public LlmException(string message, Exception inner) : base(message, inner)
    {
    }
}
