using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// Turns a bare LlmCompletion into something an operator can act on. Thinking models — the ones now
/// in normal use — put their internal monologue on <c>reasoning_content</c> and can legitimately
/// leave <c>content</c> empty; a bare "no action" message hides that decision so the player cannot
/// tell "the model was silent" from "the model spent the whole turn thinking".
/// </summary>
internal static class CompletionErrors
{
    public static string? Empty(LlmCompletion completion, string? lastReasoning, int toolRounds)
    {
        if (completion.ToolCalls.Count > 0)
        {
            return null;
        }

        var reasoningLength = (completion.Reasoning ?? lastReasoning ?? string.Empty).Length;
        var reasoningNote = reasoningLength > 0
            ? $" The model spent {reasoningLength} characters on reasoning_content without producing content, so the assistant message was empty rather than the model being idle."
            : string.Empty;
        var lengthNote = string.Equals(completion.FinishReason, "length", StringComparison.OrdinalIgnoreCase)
            ? $" The request finished with finish_reason=length, so for a thinking model the completion budget was spent on reasoning before any content could be written.{reasoningNote}"
            : reasoningNote;
        if (lengthNote.Length > 0)
        {
            return $"The model returned an empty assistant message.{(lengthNote.StartsWith(" ", StringComparison.Ordinal) ? lengthNote : " " + lengthNote)}";
        }

        return toolRounds > 1
            ? $"The model returned an empty assistant message after {toolRounds} tool rounds."
            : "The model returned an empty assistant message.";
    }
}
