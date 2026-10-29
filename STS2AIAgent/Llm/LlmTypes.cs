using STS2AIAgent.Config;

namespace STS2AIAgent.Llm;

internal interface ILlmClient
{
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);

    Task<string> PingAsync(string model, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the model actually calls tools, not just answers chat: the caller supplies the tool
    /// and a prompt whose only sensible answer is calling it, and a true return means a tool call
    /// came back. A model that cannot play this mod fails here even when the ping passed.
    /// </summary>
    Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Capability probes a non-streaming tools ping cannot answer (W7). Implemented by the production
/// client and kept as a separate interface so a test fake that only implements <see cref="ILlmClient"/>
/// is never forced to fake a stream.
/// </summary>
internal interface IStreamingToolProbe
{
    /// <summary>
    /// Sends the same miniature play decision over the provider's streaming path and reports whether
    /// the tool arguments arrived complete. A gateway that truncates the JSON after the action name
    /// passes <see cref="ILlmClient.ProbeToolCallingAsync"/> and fails here.
    /// </summary>
    Task<StreamingToolArgsProbeResult> ProbeStreamingToolCallAsync(
        string model,
        LlmTool tool,
        string prompt,
        IReadOnlyList<string> requiredFields,
        CancellationToken cancellationToken);
}

/// <summary>How a streaming tool-argument probe ended.</summary>
internal enum StreamingToolArgsVerdict
{
    /// <summary>Not attempted (an older fake, a canceled deadline, or a transient failure).</summary>
    NotRun,

    /// <summary>The tool call arrived with every required argument.</summary>
    Complete,

    /// <summary>The tool call arrived, but required arguments were missing or malformed.</summary>
    Incomplete,

    /// <summary>The streamed reply carried no tool call at all.</summary>
    NoToolCall,

    /// <summary>The provider rejected the tools schema over the streaming path.</summary>
    Unsupported,

    /// <summary>The provider rejected the output-cap field name; the client learnt the other spelling.</summary>
    MaxTokensRejected
}

/// <summary>
/// The outcome of one streaming tool-argument probe. The raw arguments are kept for diagnostics only;
/// nothing else sends them anywhere.
/// </summary>
internal sealed record StreamingToolArgsProbeResult(
    StreamingToolArgsVerdict Verdict,
    string? ArgumentsJson = null,
    string? MaxTokensParameter = null,
    string? Error = null)
{
    public static readonly StreamingToolArgsProbeResult NotRun = new(StreamingToolArgsVerdict.NotRun);
}

internal interface ILlmClientFactory
{
    ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null);
}

internal sealed class DefaultLlmClientFactory : ILlmClientFactory
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(11)
    };

    public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null)
    {
        return new OpenAiCompatibleClient(endpoint, httpClient: SharedHttp, requestTimeout: requestTimeout);
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

    /// <summary>
    /// Output-token cap for this request; null sends no cap and leaves the provider's default. A play
    /// turn sets it from the settings so a runaway answer cannot spend the whole session budget (W7).
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// The wire field carrying <see cref="MaxTokens"/>: <c>max_tokens</c> (default) or
    /// <c>max_completion_tokens</c> when the capability record learnt that this route wants it.
    /// </summary>
    public string MaxTokensParameter { get; init; } = MaxTokensField.Standard;

    /// <summary>
    /// Called with the reasoning accumulated so far each time a streamed reply adds to it, so a
    /// thinking model's progress can be shown while the turn is still in flight instead of only once
    /// it completes. Only reasoning is reported: content and tool arguments stay with the final
    /// completion, which is what the turn's legality checks and receipts read. Null -- the default --
    /// means the caller wants no partials at all.
    /// </summary>
    public Action<string>? OnReasoningDelta { get; init; }
}

internal sealed class LlmMessage
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    public byte[]? ImageJpeg { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// The provider's own <c>reasoning_content</c> for an assistant turn that called tools. Thinking
    /// models (DeepSeek thinking mode, Kimi thinking) reject the next tool round with HTTP 400 unless
    /// it is sent back; providers that never return it never get it echoed.
    /// </summary>
    public string? Reasoning { get; init; }

    public static LlmMessage System(string content) => new() { Role = "system", Content = content };

    public static LlmMessage User(string content, byte[]? imageJpeg = null) =>
        new() { Role = "user", Content = content, ImageJpeg = imageJpeg };

    public static LlmMessage Assistant(string? content, IReadOnlyList<LlmToolCall>? toolCalls = null, string? reasoning = null) =>
        new() { Role = "assistant", Content = content, ToolCalls = toolCalls, Reasoning = reasoning };

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

    /// <summary>
    /// Prompt tokens the provider served from its prefix cache (OpenAI
    /// <c>prompt_tokens_details.cached_tokens</c>, DeepSeek <c>prompt_cache_hit_tokens</c>); 0 when
    /// the provider does not report it. The prompt is ordered static-first precisely so this grows.
    /// </summary>
    public int CachedPromptTokens { get; init; }

    public static LlmUsage Empty => new();

    public static LlmUsage operator +(LlmUsage left, LlmUsage right)
    {
        return new LlmUsage
        {
            PromptTokens = left.PromptTokens + right.PromptTokens,
            CompletionTokens = left.CompletionTokens + right.CompletionTokens,
            CachedPromptTokens = left.CachedPromptTokens + right.CachedPromptTokens,
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

    public string? FinishReason { get; init; }

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

    public LlmException(string message, Exception inner, int statusCode) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// The tool-calling probe's definitive "no": the provider rejected the tools schema with a 4xx, so
/// the model genuinely cannot call tools here. Distinct from a transient failure, which must not
/// be recorded as a missing capability.
/// </summary>
internal sealed class LlmToolProbeUnsupportedException : Exception
{
    public LlmToolProbeUnsupportedException(string message, Exception inner) : base(message, inner)
    {
    }
}
