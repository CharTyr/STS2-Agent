using System.Text.Json;
using STS2AIAgent.Config;

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
    /// <summary>How long a follow-up step waits for the previous card to resolve.</summary>
    internal static readonly TimeSpan PlanStepSettleBudget = TimeSpan.FromSeconds(8);

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
            PlanSteps = steps, PlanStopReason = stop, TurnId = turnId, PlanSize = Math.Max(plan.Then.Count + 1, steps.Count)
        };

        if (!_settings().TurnPlansEnabled)
        {
            return Receipt(TurnPlanStopReasons.Disabled);
        }

        if (first.ExecutedUnsettled)
        {
            return Receipt(TurnPlanStopReasons.Unsettled);
        }

        using var baseline = JsonDocument.Parse(planStateJson);
        if (!baseline.RootElement.TryGetProperty("screen", out var screen) || screen.GetString() != "COMBAT")
            return Receipt(TurnPlanStopReasons.IllegalAction);
        var stop = TurnPlanStopReasons.Completed;
        try
        {
            foreach (var step in plan.Then)
            {
                token.ThrowIfCancellationRequested();
                // Wait (bounded) for the previous card to finish resolving. Reading at once caught the
                // frame mid-animation, whose available_actions briefly lacks play_card, and every live
                // plan stopped after its first step as an illegal action (observed 2026-10-29).
                var ready = await _bridge.WaitUntilActionableWithStateAsync(PlanStepSettleBudget, token);
                var current = ready.CompactJson;
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

                if (!ready.Actionable)
                {
                    stop = TurnPlanStopReasons.Unsettled;
                    break;
                }

                if (step.Action is not ("play_card" or "end_turn" or "use_potion" or "discard_potion"))
                {
                    stop = TurnPlanStopReasons.IllegalAction;
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
                    card_cid = step.CardCid,
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
                }, stopPlanOnPending: true);
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

            if (stop == TurnPlanStopReasons.Completed
                && !lastUnsettled
                && !string.Equals(lastAction, "end_turn", StringComparison.OrdinalIgnoreCase)
                && _settings().AutopilotEnabled
                && !_settings().NonCombatOnlyEnabled)
            {
                token.ThrowIfCancellationRequested();
                var afterPlan = await _bridge.WaitUntilActionableWithStateAsync(PlanStepSettleBudget, token);
                checkState?.Invoke(afterPlan.CompactJson);
                if (afterPlan.Actionable && _peekPlayInstruction?.Invoke(afterPlan.CompactJson) == null)
                {
                    var autoEnd = AutopilotPolicy.Decide(afterPlan.CompactJson, nonCombatOnlyEnabled: false, playerInstructionQueued: false);
                    if (autoEnd.ShouldAct && string.Equals(autoEnd.RuleId, "combat_no_moves", StringComparison.Ordinal))
                    {
                        var endReason = ReasonFor(autoEnd);
                        var endArgs = JsonSerializer.Serialize(new { action = "end_turn", reason = endReason }, JsonOptions);
                        reportPhase?.Invoke(PlayPhases.ExecutingAction);
                        string? endAccepted = null;
                        var endOutcome = await ExecuteActAsync(endArgs, token, checkState, (action, response) =>
                        {
                            endAccepted = action;
                            lastAction = action;
                            lastResult = response;
                            lastUnsettled = true;
                        }, stopPlanOnPending: true);
                        if (endOutcome.Error == null)
                        {
                            lastAction = endOutcome.Action ?? endAccepted ?? lastAction;
                            lastResult = endOutcome.ResultJson;
                            lastFingerprint = endOutcome.Fingerprint;
                            lastUnsettled = endOutcome.Unsettled;
                            steps.Add(new PlanStepReceipt(lastAction!, endReason, endOutcome.Fingerprint));
                        }
                        else if (endAccepted != null)
                        {
                            steps.Add(new PlanStepReceipt(endAccepted, endReason, null));
                            stop = TurnPlanStopReasons.OutcomeUnknown;
                        }
                    }
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

    private async Task WaitSpectatorDwellAsync(
        AgentSettings currentSettings,
        string action,
        string? mode,
        CancellationToken cancellationToken)
    {
        if (currentSettings.SpectatorHighlightMs <= 0
            || !string.Equals(currentSettings.HudMode, AgentSettings.HudFull, StringComparison.Ordinal)
            || InstanceRole.IsCompanion
            || string.Equals(mode, "chat", StringComparison.Ordinal)
            || action is not ("play_card" or "use_potion"))
        {
            return;
        }

        var dwellMs = Math.Clamp(currentSettings.SpectatorHighlightMs, 0, AgentSettings.MaxSpectatorHighlightMs);
        _reportSpectatorDwell?.Invoke(true);
        try
        {
            await Task.Delay(dwellMs, cancellationToken);
        }
        finally
        {
            _reportSpectatorDwell?.Invoke(false);
        }
    }
}
