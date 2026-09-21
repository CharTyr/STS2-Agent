namespace STS2AIAgent.Llm;

/// <summary>
/// Decides which request field carries the output-token cap.
/// </summary>
/// <remarks>
/// OpenAI-compatible endpoints disagree about this name. The older field, <c>max_tokens</c>, is what
/// nearly every clone and local runtime accepts; the reasoning models on the official API reject it
/// with HTTP 400 and name <c>max_completion_tokens</c> instead. There is no reliable way to tell
/// which an endpoint wants from its URL or model name -- a proxy in front of either accepts the same
/// configuration -- so the difference is resolved from the error the server actually returns rather
/// than from a guess that would be wrong for half the providers.
///
/// This is the same shape as the stream demotion beside it in <see cref="OpenAiCompatibleClient"/>:
/// send the broadly-compatible form first, and change one thing only when the server explains why.
/// The policy is pure so the detection can be tested without an HTTP server, because the strings
/// below are the whole contract and a provider that words its rejection differently is
/// indistinguishable from one that never rejects.
/// </remarks>
internal static class MaxTokensField
{
    public const string Standard = "max_tokens";
    public const string CompletionTokens = "max_completion_tokens";

    /// <summary>
    /// Whether this failure is the endpoint refusing <c>max_tokens</c> as a parameter name.
    /// </summary>
    /// <remarks>
    /// A 400 that merely mentions tokens is not enough: a context-length error also arrives as 400
    /// and also names max_tokens, and renaming the field for it would turn one clear failure into
    /// two confusing ones. The message has to say the parameter is not accepted.
    /// </remarks>
    public static bool IsUnsupportedParameterError(LlmException exception)
    {
        if (exception.StatusCode is not null and not 400 and not 422)
        {
            return false;
        }

        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.Contains(CompletionTokens, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!message.Contains(Standard, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unknown parameter", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid parameter", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves the cap from <c>max_tokens</c> to <c>max_completion_tokens</c> in a request body.
    /// </summary>
    /// <returns>
    /// False when the body carries no <c>max_tokens</c> value to move, which means the rejection
    /// was not about this field and the caller must surface the original error rather than retry
    /// an unchanged request forever.
    /// </returns>
    public static bool RenameToCompletionTokens(IDictionary<string, object?> body)
    {
        if (!body.TryGetValue(Standard, out var value))
        {
            return false;
        }

        body.Remove(Standard);
        body[CompletionTokens] = value;
        return true;
    }
}
