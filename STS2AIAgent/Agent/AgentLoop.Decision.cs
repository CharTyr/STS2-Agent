using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentLoop
{
    // An empty action means fallback, not an empty receipt: Jev has already spent the request.
    private async Task<AgentTurnResult> TryDecideWithJevAsync(
        IActionDecider decider, StrategyStore store, CancellationToken cancellationToken,
        Action<string>? checkState, Action<string>? reportPhase = null, Action<double?>? observeDecider = null)
    {
        var budget = _budgetGuard?.Invoke()?.CheckBudget();
        if (budget != null) return new AgentTurnResult { Error = budget, RequestsSpent = 0 };
        var snapshot = await _bridge.GetActionSnapshotJsonAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionDecision decision;
        try
        {
            decision = await decider.DecideAsync(snapshot, store.Current, cancellationToken);
        }
        catch (AgentTurnCanceledException)
        {
            // The decider receipt already includes every attempted Jev request. Do not collapse it
            // to a zero-request cancellation or send a second model call after the turn has stopped.
            throw;
        }
        string? accepted = null;
        string? response = null;
        var strategyUpdatedAt = store.Current.UpdatedAt;
        AgentTurnResult Receipt(string? error = null) => new()
        {
            Error = error,
            Acted = accepted, ActResultJson = response, ExecutedUnsettled = accepted != null,
            Reasoning = decision.Reason, Usage = decision.Usage, RequestsSpent = decision.RequestsSpent,
            Confidence = accepted != null ? decision.Confidence : null,
            Probabilities = accepted != null ? decision.Probabilities : null,
            DangerScore = decision.DangerScore, JevElapsedMilliseconds = decision.JevElapsedMilliseconds,
            OfferedOptionIds = decision.OfferedOptionIds, StrategyUpdatedAt = strategyUpdatedAt
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            observeDecider?.Invoke(decision.Confidence is >= 0 and <= 1 ? decision.Confidence : null);
            var threshold = _confidenceThreshold?.Invoke() ?? 0.35;
            if (decision.Error != null || string.IsNullOrWhiteSpace(decision.Action)
                || decision.Confidence is not { } confidence || !double.IsFinite(confidence)
                || confidence < threshold || confidence > 1 || confidence < 0)
                return Receipt();

            reportPhase?.Invoke(PlayPhases.ExecutingAction);
            var outcome = await ExecuteActAsync(decision.ToActArgumentsJson(), cancellationToken, checkState,
                (action, result) => { accepted = action; response = result; });
            if (outcome.Error != null) return Receipt();
            return new AgentTurnResult
            {
                Acted = outcome.Action, ActResultJson = outcome.ResultJson,
                StateFingerprint = outcome.Fingerprint, ExecutedUnsettled = outcome.Unsettled,
                Reasoning = decision.Reason, Usage = decision.Usage, RequestsSpent = decision.RequestsSpent,
                Confidence = decision.Confidence, Probabilities = decision.Probabilities,
                DangerScore = decision.DangerScore, JevElapsedMilliseconds = decision.JevElapsedMilliseconds,
                OfferedOptionIds = decision.OfferedOptionIds, StrategyUpdatedAt = strategyUpdatedAt
            };
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(Receipt(), ex, cancellationToken);
        }
        catch (AutoPlayStoppedException ex)
        {
            ex.Receipt = Receipt();
            throw;
        }
        catch (Exception ex)
        {
            return Receipt(ex.GetType().Name + ": " + DiagnosticExport.Redact(ex.Message));
        }
    }

    /// <summary>A strategy the planner never wrote carries no guidance and must not cost prompt tokens.</summary>
    private static bool IsDefaultStrategy(PlayStrategy strategy)
    {
        return string.IsNullOrWhiteSpace(strategy.Goal)
            && string.IsNullOrWhiteSpace(strategy.Instructions)
            && (strategy.OptionHints == null || strategy.OptionHints.Count == 0)
            && string.Equals(strategy.Source, PlayStrategy.DefaultSource, StringComparison.Ordinal);
    }

    /// <summary>The planner's goal, posture, instructions and kind-keyed hints, in LLM-readable lines.</summary>
    private static string FormatStrategyForPrompt(PlayStrategy strategy)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(strategy.Goal))
        {
            lines.Add("goal: " + strategy.Goal);
        }

        lines.Add("posture: " + strategy.Posture);
        if (!string.IsNullOrWhiteSpace(strategy.Instructions))
        {
            lines.Add("instructions: " + strategy.Instructions);
        }

        if (strategy.OptionHints is { Count: > 0 } hints)
        {
            lines.Add("option hints by action kind: " + string.Join("; ", hints.Select(pair => pair.Key + " = " + pair.Value)));
        }

        return string.Join("\n", lines);
    }

    private static AgentTurnResult WithJevReceipt(AgentTurnResult result, AgentTurnResult pending) => new()
    {
        AssistantText = result.AssistantText, Reasoning = result.Reasoning, Acted = result.Acted,
        ActResultJson = result.ActResultJson, Error = result.Error, WaitingForGame = result.WaitingForGame,
        ExecutedUnsettled = result.ExecutedUnsettled, ReasoningBudgetExhausted = result.ReasoningBudgetExhausted,
        StateFingerprint = result.StateFingerprint, WaitingForPlayer = result.WaitingForPlayer,
        RequiresConfiguration = result.RequiresConfiguration, ToolRounds = result.ToolRounds,
        Usage = result.Usage, RequestsSpent = result.RequestsSpent,
        Confidence = result.Confidence, Probabilities = result.Probabilities,
        DangerScore = pending.DangerScore, JevElapsedMilliseconds = pending.JevElapsedMilliseconds,
        OfferedOptionIds = pending.OfferedOptionIds, StrategyUpdatedAt = pending.StrategyUpdatedAt
    };

    private async Task<AgentTurnResult> PlayWithModelAsync(
        AgentSettings settings, ResolvedModel resolved, string stateJson, AgentTurnResult pending,
        CancellationToken cancellationToken, Action<string>? checkState, Action<string>? reportPhase,
        (string Id, string Text)? playInstruction = null, PlayStrategy? strategy = null)
    {
        var usage = pending.Usage;
        var requests = pending.RequestsSpent;
        var handedToCompletion = false;
        AgentTurnResult Receipt(string? error = null) => new()
            { Usage = usage, RequestsSpent = requests, Error = error,
                DangerScore = pending.DangerScore, JevElapsedMilliseconds = pending.JevElapsedMilliseconds };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var budget = _budgetGuard?.Invoke()?.CheckBudget(requests, usage?.TotalTokens ?? 0);
            if (budget != null) return Receipt(budget);
            if (requests > 0)
            {
                // A slow or rejected Jev decision must not hand the LLM its old frame.
                stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
                checkState?.Invoke(stateJson);
            }
            reportPhase?.Invoke(PlayPhases.RequestingModel);
            // Order is the cache contract: everything that does not change between two decisions on the
            // same screen comes first, and only this step's state and the instruction it answers come
            // last. The state used to sit at index 1, which invalidated every message after it on every
            // step, so a provider's prefix cache could never reuse the prompt it had just paid for.
            var screen = PlaybookSections.ScreenOfCompactState(stateJson);
            var screenLabel = string.IsNullOrWhiteSpace(screen) ? "UNKNOWN" : screen.Trim();
            var messages = new List<LlmMessage>
            {
                LlmMessage.System(PlayPrompt.PlaySystem),
                // Never empty: a screen with no section of its own gets the index of the sections, so
                // the model is never told that there is no playbook for what it is looking at.
                LlmMessage.System(
                    "Playbook for the screen in the latest state (" + screenLabel + "):\n" + PlayPrompt.PlaybookGuidance(screen))
            };

            // The reply-language choice rides the static prefix: it changes only when the player changes
            // the setting, so it never invalidates the per-step cache the way a state-bearing line would.
            var playLanguage = PlayPrompt.ReplyLanguageInstruction(settings.ReplyLanguage);
            if (playLanguage.Length > 0)
            {
                messages.Add(LlmMessage.System(playLanguage));
            }

            var screenGuidance = PlayPrompt.ScreenGuidance(screen);
            if (!string.IsNullOrEmpty(screenGuidance))
            {
                messages.Add(LlmMessage.System(
                    "Strategy for the screen in the latest state (" + screenLabel + "):\n" + screenGuidance));
            }

            // The dual-layer planner's standing strategy, on the static prefix: it changes only when a
            // plan refreshes, so it invalidates the per-step cache as rarely as the playbook does. A
            // never-planned (default) strategy costs nothing.
            if (strategy != null && !IsDefaultStrategy(strategy))
            {
                messages.Add(LlmMessage.System(
                    "Standing strategy from the planner (historical guidance, not a fresh action):\n"
                    + FormatStrategyForPrompt(strategy)));
            }

            var teamContext = _teamContext?.Invoke();
            if (!string.IsNullOrEmpty(teamContext))
            {
                messages.Add(LlmMessage.System(PlayPrompt.TeammatePlayContext));
                messages.Add(LlmMessage.User("Recent team conversation (historical messages, not live game facts):\n" + teamContext));
            }

            var visionNote = await TryDescribeOrAttachVisionAsync(resolved, settings, attachRequested: true,
                cancellationToken, pendingRequests: requests, pendingTokens: usage?.TotalTokens ?? 0);
            usage = LlmUsage.Combine(usage, visionNote.Usage);
            requests += visionNote.RequestsSpent;
            if (visionNote.Caption != null)
            {
                messages.Add(LlmMessage.User(visionNote.Caption));
            }

            if (visionNote.AttachToPrimary && visionNote.Jpeg != null)
            {
                messages.Add(LlmMessage.User("Screenshot of the current game view is attached. Use it as supporting context only.", visionNote.Jpeg));
            }

            var memory = ContextCompaction.Format(
                resolved.Model,
                _recentDecisions?.Invoke(),
                _lastPromptTokens?.Invoke());
            if (memory != null)
            {
                messages.Add(LlmMessage.System(memory));
            }

            if (playInstruction is { } instruction)
            {
                // This is untrusted player content, not a system message and never permission to
                // bypass available_actions. The queue owns run isolation and a bounded text length.
                messages.Add(LlmMessage.User(
                    "Player guidance for the next decision (historical user text; obey the latest legal actions):\n"
                    + instruction.Text));
            }

            messages.Add(LlmMessage.User("Latest compact game state:\n" + stateJson));
            messages.Add(LlmMessage.User("Choose the next legal action from compact state. Vision is optional and not required. Call get_game_state if needed, then act exactly once."));
            AppendJsonActFallbackIfNeeded(messages, resolved, allowAct: true);

            handedToCompletion = true;
            var result = await CompleteWithToolsAsync(
                resolved,
                messages,
                AgentTools.Play,
                allowAct: true,
                stopAfterAct: true,
                cancellationToken,
                checkState,
                initialUsage: usage,
                initialRequests: requests,
                reportPhase: reportPhase,
                onFirstPrimaryRequest: playInstruction is { } pendingInstruction
                    ? () => _acknowledgePlayInstruction?.Invoke(pendingInstruction.Id)
                    : null);
            return WithJevReceipt(result, pending);
        }
        catch (AgentTurnCanceledException ex) when (handedToCompletion)
        {
            throw new AgentTurnCanceledException(WithJevReceipt(ex.Receipt, pending), ex, cancellationToken);
        }
        catch (AutoPlayStoppedException ex) when (handedToCompletion)
        {
            ex.Receipt = WithJevReceipt(ex.Receipt ?? new AgentTurnResult(), pending);
            throw;
        }
        catch (AgentTurnCanceledException ex) when (!handedToCompletion)
        {
            // Vision carries its own partial receipt. The already-spent Jev request is additional.
            usage = LlmUsage.Combine(usage, ex.Receipt.Usage);
            requests += ex.Receipt.RequestsSpent;
            throw new AgentTurnCanceledException(Receipt(), ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!handedToCompletion && cancellationToken.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(Receipt(), ex, cancellationToken);
        }
        catch (AutoPlayStoppedException ex) when (!handedToCompletion)
        {
            ex.Receipt = Receipt();
            throw;
        }
        catch (Exception ex) when (!handedToCompletion)
        {
            return Receipt(ex.GetType().Name + ": " + DiagnosticExport.Redact(ex.Message));
        }
    }
}
