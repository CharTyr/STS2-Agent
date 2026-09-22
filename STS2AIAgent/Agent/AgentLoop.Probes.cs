using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The loop's client-creation and read-only probe surface: the per-request timeout plumbing every
/// completion goes through, the per-model test's client, and the strategy planner's state read.
/// </summary>
/// <remarks>
/// Split from <c>AgentLoop.cs</c> because that file is at its size budget: these members belong to
/// the loop (they read its settings and its factory) but not to its turn logic, and the turn logic
/// is what the base file's budget is watching.
/// </remarks>
internal sealed partial class AgentLoop
{
    /// <summary>
    /// Creates a client for the endpoint with the configured per-request timeout applied. The
    /// settings value is read live so a change takes effect on the next request.
    /// </summary>
    private ILlmClient CreateClient(LlmEndpoint endpoint)
    {
        var seconds = _settings().LlmRequestTimeoutSeconds;
        var timeout = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : (TimeSpan?)null;
        return _factory.Create(endpoint, timeout);
    }

    /// <summary>The per-model test's client: same factory and timeout as a play request.</summary>
    public ILlmClient CreateProbeClient(LlmEndpoint endpoint)
    {
        return CreateClient(endpoint);
    }

    /// <summary>
    /// A compact state read for the strategy planner: the same view the play decision sees, trimmed
    /// to a size a planning prompt can carry. Read-only; never acts.
    /// </summary>
    public async Task<string> DescribeCurrentStateForPlanningAsync(CancellationToken cancellationToken)
    {
        var json = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        const int max = 4000;
        return json.Length <= max ? json : json[..max] + "…";
    }
}
