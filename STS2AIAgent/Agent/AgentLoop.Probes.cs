using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// The loop's client-creation and read-only probe surface: the per-request timeout plumbing every
/// completion goes through, and the strategy planner's state read.
/// </summary>
/// <remarks>
/// Its own partial because of what these members touch: the client factory and the settings'
/// timeout field. The turn logic in the base file calls <see cref="CreateClient"/> but never defines
/// it, and the planner's state read is an entry point nothing in the turn logic calls.
/// </remarks>
internal sealed partial class AgentLoop
{
    internal async Task<string?> GetCurrentRunIdAsync(CancellationToken token)
    {
        var json = await _bridge.GetCompactStateJsonAsync(token);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("run_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() : null;
    }

    /// <summary>
    /// Creates a client for the endpoint with the configured per-request timeout applied. The
    /// settings value is read live so a change takes effect on the next request. Internal rather
    /// than private: the per-model test and the planner create their clients through the same path
    /// a play request uses.
    /// </summary>
    internal ILlmClient CreateClient(LlmEndpoint endpoint)
    {
        var seconds = _settings().LlmRequestTimeoutSeconds;
        var timeout = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : (TimeSpan?)null;
        return _factory.Create(endpoint, timeout);
    }

    /// <summary>
    /// A compact state read for the strategy planner: the same view the play decision sees, trimmed
    /// to a size a planning prompt can carry. Read-only; never acts. The trim is JSON-aware, so the
    /// planner always receives a parseable document (a character cut used to produce invalid JSON on
    /// nearly every combat frame, which left plan scope empty and the planner reading garbage).
    /// </summary>
    public async Task<string> DescribeCurrentStateForPlanningAsync(CancellationToken cancellationToken)
    {
        var json = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        const int max = 4000;
        return PlanningSummary.Trim(json, max);
    }
}
