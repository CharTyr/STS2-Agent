using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Server;

/// <summary>
/// Native MCP's whole-turn orchestration. Uses the same pure rules as the in-game loop and Python,
/// but keeps transport ownership here: every step goes through ActOnceAsync, never a second action
/// dispatch surface. A non-completed result is terminal, including pending and outcome_unknown.
/// </summary>
internal sealed partial class NativeMcpServer
{
    private async Task<string> AppendPlanAsync(JsonElement arguments, string firstResult, string planState, CancellationToken cancellationToken)
    {
        // Named reader makes the optional argument visible to the two-MCP parameter contract.
        var then = ReadArray(arguments, "then");
        if (then.ValueKind != JsonValueKind.Array || then.GetArrayLength() == 0) return firstResult;
        var plan = TurnPlanPolicy.Parse(arguments.GetRawText());
        var results = new List<JsonElement> { DeserializeOrEmpty(firstResult) };
        var stop = PlanResultStop(results[0]);
        if (stop == null && ReadString(DeserializeOrEmpty(planState), "screen") != "COMBAT")
            stop = TurnPlanStopReasons.IllegalAction;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            foreach (var step in plan.Then)
            {
                if (stop != null) break;
                deadline.Token.ThrowIfCancellationRequested();
                // Let the previous card resolve first: a frame read mid-animation lacks play_card and
                // would stop every plan after one step (the in-game loop hit this live on 2026-10-29).
                var ready = await _bridge.WaitUntilActionableWithStateAsync(AgentLoop.PlanStepSettleBudget, deadline.Token);
                var current = ready.CompactJson;
                stop = TurnPlanPolicy.Diverged(planState, current);
                if (stop == null && !ready.Actionable) stop = TurnPlanStopReasons.Unsettled;
                if (stop != null) break;
                // Follow-ups are deliberately combat actions only, never menu/debug operations.
                if (step.Action is not ("play_card" or "end_turn" or "use_potion" or "discard_potion"))
                {
                    stop = TurnPlanStopReasons.IllegalAction;
                    break;
                }
                var resolved = TurnPlanPolicy.Resolve(step, current, requireCid: true);
                if (!resolved.Ok) { stop = resolved.StopReason; break; }
                var (result, _) = await ActOnceAsync(resolved.Action, resolved.CardIndex, step.CardCid is { } cid && cid <= int.MaxValue ? (int)cid : null,
                    resolved.TargetIndex, resolved.OptionIndex, null, null, null, step.Reason,
                    rawState: false, deadline.Token);
                var payload = DeserializeOrEmpty(result);
                results.Add(payload);
                stop = PlanResultStop(payload);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stop = TurnPlanStopReasons.OutcomeUnknown;
        }
        // Keep the original single-act fields unchanged. Clients opting into then inspect plan;
        // final_state is additive and the last step's own state, never a speculative reconciliation.
        var output = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(firstResult) ?? new();
        output["plan"] = JsonSerializer.SerializeToElement(new
        {
            steps = results,
            executed = results.Count,
            stop_reason = stop ?? TurnPlanStopReasons.Completed,
            warnings = plan.Warnings
        });
        if (results[^1].TryGetProperty("state", out var finalState)) output["final_state"] = finalState.Clone();
        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static string? PlanResultStop(JsonElement result)
    {
        if (result.TryGetProperty("error", out _)) return TurnPlanStopReasons.Rejected;
        var status = ReadString(result, "status");
        if (status == "outcome_unknown") return TurnPlanStopReasons.OutcomeUnknown;
        if (status != "completed" || (result.TryGetProperty("stable", out var stable) && stable.ValueKind == JsonValueKind.False))
            return TurnPlanStopReasons.Unsettled;
        return null;
    }

    private static JsonElement ReadArray(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : default;
}
