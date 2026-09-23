using System.Text.Json.Serialization;

namespace STS2AIAgent.Llm;

/// <summary>
/// Request body for the TypeSafe Jev <c>POST {BaseUrl}/v1/systemone</c> endpoint.
/// </summary>
/// <remarks>
/// The state travels as a <see cref="System.Text.Json.Nodes.JsonNode"/> rather than as a typed
/// payload because the Jev layer sits beside the LLM layer rather than inside it. The caller
/// (<c>AgentLoop</c>) already holds the compact <c>agent_view</c> as JSON, and a second C# shape for
/// it would have to be kept in step with <c>GameStateService</c> for no gain: Jev only ever forwards
/// it. A JSON node also keeps the request valid when the state contains a screen the DTOs never
/// modelled.
/// </remarks>
internal sealed class JevRequest
{
    /// <summary>The game state the questions are asked about. Forwarded verbatim to Jev.</summary>
    [JsonPropertyName("state")]
    public System.Text.Json.Nodes.JsonNode? State { get; init; }

    /// <summary>Jev model id or alias, for example <c>jev-latest</c>. A blank value falls back to the configured model.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>Questions keyed by a caller-chosen id. The response answers use the same ids.</summary>
    [JsonPropertyName("questions")]
    public Dictionary<string, JevQuestion> Questions { get; init; } = new();
}

/// <summary>One question handed to Jev, in the shape the wire format expects.</summary>
/// <remarks>
/// The three Jev question types are <c>choice</c>, <c>score</c> and <c>noul</c>. What else a
/// question carries depends on the type, and Jev reads <c>instructions</c> as either a plain string
/// or an object, so both are typed loosely here: a strongly typed <c>criteria</c> would force callers
/// to build a different shape per question type for the benefit of a compiler that cannot know which
/// one a given question is.
/// </remarks>
internal sealed class JevQuestion
{
    /// <summary>One of <c>choice</c>, <c>score</c> or <c>noul</c>. Sent verbatim; Jev rejects anything else.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>What is being asked. A string, or an object for structured guidance.</summary>
    [JsonPropertyName("instructions")]
    public required object Instructions { get; init; }

    /// <summary>
    /// For <c>choice</c>, a map of option id to the description of that option; for <c>score</c>, an
    /// ordered array of 2-10 level strings; absent for <c>noul</c>.
    /// </summary>
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Criteria { get; init; }
}

/// <summary>Response body from <c>POST {BaseUrl}/v1/systemone</c>.</summary>
internal sealed class JevResponse
{
    /// <summary>The model that answered: the resolved version, for example <c>jev-1.13.0</c>, not the alias the request sent.</summary>
    /// <remarks>
    /// Verified against the live API: a request for <c>jev-latest</c> comes back naming the version it
    /// resolved to. It is therefore a fact about the answer, not a copy of the request, and must not be
    /// compared against the configured alias to decide whether the call succeeded.
    /// </remarks>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Answers keyed by the same ids the request used. Never null once <see cref="JevClient"/> has parsed a response.</summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, JevAnswer>? Answers { get; init; }

    /// <summary>Token accounting for the call. Absent when the server did not report it.</summary>
    [JsonPropertyName("usage")]
    public JevUsage? Usage { get; init; }
}

/// <summary>
/// One answer. Only the members its <see cref="Type"/> names are populated; the rest stay null so a
/// caller cannot mistake a missing field for a zero.
/// </summary>
/// <remarks>
/// The per-type shapes, all verified against the live API on 2026-10: a <c>choice</c> answer carries
/// <c>choice</c>, <c>probabilities</c> and <c>confidence</c>; a <c>score</c> answer carries
/// <c>score</c>, <c>legend</c>, <c>probabilities</c> and <c>confidence</c>; a <c>noul</c> answer
/// carries only <c>noul</c>. A caller should read the members its type promises and ignore the rest
/// rather than testing <c>Confidence.HasValue</c> to tell the types apart.
/// </remarks>
internal sealed class JevAnswer
{
    /// <summary>One of <c>choice</c>, <c>score</c> or <c>noul</c>, echoing the question.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The chosen option id, for a <c>choice</c> answer.</summary>
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    /// <summary>The distribution over option ids, for a <c>choice</c> answer.</summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double>? Probabilities { get; init; }

    /// <summary>Confidence in the answer, 0..1. Present on <c>choice</c> and <c>score</c> answers; absent on <c>noul</c>, which carries only a value.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    /// <summary>
    /// Index into the question's ordered level list, for a <c>score</c> answer. A real number
    /// rather than an integer: Jev's score is a weighted position between two levels, verified
    /// against the live API as 1.82 on a five-level question. Rounding it here would throw away
    /// the part that tells the two adjacent levels apart.
    /// </summary>
    [JsonPropertyName("score")]
    public double? Score { get; init; }

    /// <summary>
    /// The level labels a <c>score</c> answer was given, keyed by index, exactly as the server
    /// echoed them back. Kept on the answer rather than looked up from the question's own criteria
    /// because Jev is what decided the levels, and the caller's copy is only what it asked for.
    /// </summary>
    [JsonPropertyName("legend")]
    public Dictionary<string, string>? Legend { get; init; }

    /// <summary>The yes/no value of a <c>noul</c> answer, 0..1. It deliberately carries no confidence.</summary>
    [JsonPropertyName("noul")]
    public double? Noul { get; init; }
}

/// <summary>Token accounting Jev reports per call, for the session budget ledger.</summary>
internal sealed class JevUsage
{
    /// <summary>Tokens charged for the request, state included.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    /// <summary>Tokens charged for the answer.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>
/// Why a Jev call failed. Callers classify on the kind, not on the message: <see cref="Config"/> means
/// "fix the endpoint or the key", <see cref="RateLimited"/> means "the provider asked us to slow down
/// and would again", and <see cref="Server"/> means "the remote end refused us".
/// </summary>
internal enum JevExceptionKind
{
    Config,
    Network,
    RateLimited,
    Server,
    Canceled
}

/// <summary>
/// Failure raised by <see cref="JevClient"/>, carrying the classification and the HTTP status when
/// there was one.
/// </summary>
/// <remarks>
/// Mirrors <see cref="LlmException"/> for the same reason: the callers of both layers want to tell a
/// configuration problem from a provider hiccup without pattern-matching a message. Two deliberate
/// differences: a caller's own cancellation is <em>not</em> wrapped here (it surfaces as an
/// <see cref="System.OperationCanceledException"/> so the ordinary cancellation plumbing still sees it),
/// and no constructor takes an API key, so none can be leaked through <see cref="Exception.Message"/>.
/// </remarks>
internal sealed class JevException : Exception
{
    /// <summary>The classification the client assigned. This is what callers switch on.</summary>
    public JevExceptionKind Kind { get; }

    /// <summary>The HTTP status, when the failure came back with one.</summary>
    public int? StatusCode { get; }

    /// <summary>Bounded whole-second retry hint, only present for a 429 response.</summary>
    public int? RetryAfterSeconds { get; }

    public JevException(string message, JevExceptionKind kind, int? statusCode = null, int? retryAfterSeconds = null)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public JevException(string message, JevExceptionKind kind, Exception inner)
        : base(message, inner)
    {
        Kind = kind;
    }
}
