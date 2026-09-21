using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// Reads the optional decision metadata an MCP client attaches to <c>POST /action</c>.
/// </summary>
/// <remarks>
/// A pure rule rather than a Router detail: the same <c>client_context.decision_reason</c> shape is
/// what the Python sidecar sends, and keeping the parse here means the contract is testable without
/// the HTTP listener.
/// </remarks>
internal static class DecisionContext
{
    /// <summary>
    /// The trimmed rationale, or null when the caller sent none, sent a blank one, or used a
    /// non-string value. A malformed context must never be reported as a reason.
    /// </summary>
    public static string? ReadReason(object? clientContext)
    {
        if (clientContext is not JsonElement { ValueKind: JsonValueKind.Object } context ||
            !context.TryGetProperty("decision_reason", out var reason) ||
            reason.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = reason.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
