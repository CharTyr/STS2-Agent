using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>One executed step of a whole-turn plan, for the decision log.</summary>
internal sealed record PlanStepReceipt(string Action, string? Reason, string? Fingerprint);

/// <summary>
/// The executor of a whole-turn plan (W3): after the model's first act succeeds, the rest of the turn
/// it put in <c>then</c> runs here, one step at a time, without another model request.
/// </summary>
/// <remarks>
/// Before every step: a fresh compact read, a queued player instruction stops the plan (the player's
/// words must reach a model), <see cref="TurnPlanPolicy.Diverged"/> compares against the state the
/// model planned on, and <see cref="TurnPlanPolicy.Resolve"/> turns the step's card id into today's
/// hand position. The step then goes through <see cref="ExecuteActAsync"/> -- every legality, index,
/// Sandpit and dispatch-time check a model act gets. The first step that does not complete ends the
/// plan; nothing is retried, and a step whose outcome is unknown after the game accepted it is never
/// sent again. The receipt describes the last executed step (so the no-progress guard judges the
/// final frame) and lists every step, so the log can show each one while only the first carries the
/// request's cost.
/// </remarks>
internal sealed partial class AgentLoop
{
    private async Task<AgentTurnResult> ContinuePlanAsync(
        AgentTurnResult first,
        string argumentsJson,
        string planStateJson,
        CancellationToken token,
        Action<string>? checkState,
        Action<string>? reportPhase)
    {
        var plan = TurnPlanPolicy.Parse(argumentsJson);
        if (plan.Then.Count == 0 || string.IsNullOrWhiteSpace(first.Acted))
        {
            return first;
        }

        var turnId = Guid.NewGuid().ToString("N")[..12];
        var steps = new List<PlanStepReceipt> { new(first.Acted!, plan.First?.Reason ?? first.Reasoning, first.StateFingerprint) };
        var lastAction = first.Acted;
        var lastResult = first.ActResultJson;
        var lastFingerprint = first.StateFingerprint;
        var lastUnsettled = first.ExecutedUnsettled;

        AgentTurnResult Receipt(string stop) => new()
        {
            AssistantText = first.AssistantText, Reasoning = first.Reasoning,
            Acted = lastAction, ActResultJson = lastResult, StateFingerprint = lastFingerprint,
            ExecutedUnsettled = lastUnsettled, ToolRounds = first.ToolRounds, ReadToolRounds = first.ReadToolRounds,
            StreamDemoted = first.StreamDemoted, Usage = first.Usage, RequestsSpent = first.RequestsSpent,
            PlanSteps = steps, PlanStopReason = stop, TurnId = turnId, PlanSize = plan.Then.Count + 1
        };

        if (!_settings().TurnPlansEnabled)
        {
            return Receipt(TurnPlanStopReasons.Disabled);
        }

        if (first.ExecutedUnsettled)
        {
            return Receipt(TurnPlanStopReasons.Unsettled);
        }

        var stop = TurnPlanStopReasons.Completed;
        try
        {
            foreach (var step in plan.Then)
            {
                token.ThrowIfCancellationRequested();
                var current = await _bridge.GetCompactStateJsonAsync(token);
                checkState?.Invoke(current);
                if (_peekPlayInstruction?.Invoke(current) != null)
                {
                    stop = TurnPlanStopReasons.PlayerInstruction;
                    break;
                }

                if (TurnPlanPolicy.Diverged(planStateJson, current) is { } diverged)
                {
                    stop = diverged;
                    break;
                }

                var resolved = TurnPlanPolicy.Resolve(step, current, requireCid: true);
                if (!resolved.Ok)
                {
                    stop = resolved.StopReason!;
                    break;
                }

                var args = JsonSerializer.Serialize(new
                {
                    action = resolved.Action,
                    card_index = resolved.CardIndex,
                    target_index = resolved.TargetIndex,
                    option_index = resolved.OptionIndex,
                    reason = step.Reason
                }, JsonOptions);
                reportPhase?.Invoke(PlayPhases.ExecutingAction);
                string? accepted = null;
                var outcome = await ExecuteActAsync(args, token, checkState, (action, response) =>
                {
                    accepted = action;
                    // Recorded the moment the game takes it: a later failure must still show it ran.
                    lastAction = action;
                    lastResult = response;
                    lastUnsettled = true;
                });
                if (outcome.Error != null)
                {
                    if (accepted != null)
                    {
                        steps.Add(new PlanStepReceipt(accepted, step.Reason, null));
                        stop = TurnPlanStopReasons.OutcomeUnknown;
                    }
                    else
                    {
                        stop = TurnPlanStopReasons.Rejected;
                    }

                    break;
                }

                lastAction = outcome.Action ?? accepted ?? lastAction;
                lastResult = outcome.ResultJson;
                lastFingerprint = outcome.Fingerprint;
                lastUnsettled = outcome.Unsettled;
                steps.Add(new PlanStepReceipt(lastAction!, step.Reason, outcome.Fingerprint));
                if (outcome.Unsettled)
                {
                    stop = TurnPlanStopReasons.Unsettled;
                    break;
                }
            }
        }
        catch (NonCombatOnlyYieldException)
        {
            stop = TurnPlanStopReasons.ScreenChanged;
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested && ex is not AgentTurnCanceledException)
        {
            throw new AgentTurnCanceledException(Receipt(TurnPlanStopReasons.Canceled), ex, token);
        }
        catch (AutoPlayStoppedException ex)
        {
            ex.Receipt ??= Receipt(TurnPlanStopReasons.Rejected);
            throw;
        }

        return Receipt(stop);
    }
}
