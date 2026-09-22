using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

// Owns retry policy for one automatic session; resuming creates a fresh instance.
internal sealed class AutoPlayRecovery
{
    private int _failures;

    // The no-progress side of the policy. Nothing here is player-visible until a run stops.
    private string? _lastAction;
    private string? _lastFingerprint;
    private int _repeats;
    private int _unsettled;
    private int _reasoningBudgetExhausted;

    public (string? StopReason, string? StopKind, TimeSpan Delay) Observe(AgentTurnResult result)
    {
        if (result.RequiresConfiguration)
            return (Loc.T("请检查模型、端点或凭据后再继续：{0}", result.Error), StopKindPolicy.Configuration, TimeSpan.Zero);

        // Waiting for the human player or an animation must not spend the model retry budget.
        // It also must not erase failures observed before the wait.
        if (result.WaitingForGame)
            return (null, null, TimeSpan.FromSeconds(1));

        // The action executed and the response is still "pending": the game accepted what the model
        // asked for, so this must not spend the retry budget -- but an unbounded run of them is a
        // stall, not slow progress, so the run has to stop once it passes UnsettledLimit.
        if (result.Error == null && result.Acted != null && result.ExecutedUnsettled)
        {
            _unsettled++;
            if (_unsettled >= NoProgressPolicy.UnsettledLimit)
            {
                return (
                    Loc.T(
                        "连续 {0} 次动作已执行但界面一直没有稳定，已停止自动游玩。检查当前局面后可手动继续：{1}",
                        NoProgressPolicy.UnsettledLimit,
                        result.Acted),
                    StopKindPolicy.Failed,
                    TimeSpan.Zero);
            }

            // Back off while the animation finishes: a pending turn that keeps coming back is slow,
            // not free.
            return (null, null, TimeSpan.FromSeconds(Math.Pow(2, Math.Min(_unsettled, 3))));
        }

        // A thinking model can use the provider's whole response window before it gets to the tool
        // call. That is normal partial progress, not the generic model-failure lane. The retry is
        // bounded separately so an endpoint with an impossible provider cap cannot spend forever.
        if (result.ReasoningBudgetExhausted)
        {
            _reasoningBudgetExhausted++;
            if (_reasoningBudgetExhausted >= NoProgressPolicy.ReasoningBudgetLimit)
            {
                return (
                    Loc.T(
                        "连续 {0} 次思考已耗尽模型输出预算而未给出动作，已停止自动游玩。降低思考强度或提高服务商输出预算后再继续。",
                        NoProgressPolicy.ReasoningBudgetLimit),
                    StopKindPolicy.Failed,
                    TimeSpan.Zero);
            }

            return (null, null, TimeSpan.FromSeconds(Math.Pow(2, Math.Min(_reasoningBudgetExhausted, 3))));
        }

        // A settled action is a success, but success alone is not progress: the same action landing
        // on the same state over and over is a spin that never raises an error.
        if (result.Error == null && result.Acted != null)
        {
            // A settled turn ends any run of unconfirmed ones.
            _unsettled = 0;
            _reasoningBudgetExhausted = 0;
            _failures = 0;

            var repeats = NoProgressPolicy.IsRepeat(
                _lastAction,
                _lastFingerprint,
                result.Acted,
                result.StateFingerprint);
            _repeats = repeats ? _repeats + 1 : 1;
            _lastAction = result.Acted;
            _lastFingerprint = result.StateFingerprint;

            if (repeats && _repeats >= NoProgressPolicy.RepeatThreshold)
            {
                return (
                    Loc.T(
                        "连续 {0} 次重复同一个动作且状态没有变化，已停止自动游玩。检查当前局面后可手动继续：{1}",
                        NoProgressPolicy.RepeatThreshold,
                        result.Acted),
                    StopKindPolicy.Failed,
                    TimeSpan.Zero);
            }

            // A repeated no-op gets a pause; real progress moves on immediately.
            return (null, null, repeats ? TimeSpan.FromSeconds(1) : TimeSpan.Zero);
        }

        var reason = result.Error ?? Loc.T("模型未给出可执行动作");
        _failures++;
        // The kind stays unset so the appended reason still supplies the useful distinction:
        // a dead endpoint should read as a network stop, a rejected key as a config stop.
        return _failures >= 3
            ? (Loc.T("连续 3 次决策未成功，已停止自动游玩。检查当前局面后可手动继续：{0}", reason), null, TimeSpan.Zero)
            : (null, null, TimeSpan.FromSeconds(Math.Pow(2, _failures)));
    }

    public static async Task RunAsync(
        Func<CancellationToken, Task<AgentTurnResult>> turn,
        Action<AgentTurnResult> report,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        SessionBudgetGuard? budgetGuard = null,
        Func<CancellationToken, Task>? afterTurn = null,
        Action<AgentTurnResult>? reportInterrupted = null,
        SemaphoreSlim? turnGate = null)
    {
        var recovery = new AutoPlayRecovery();
        delay ??= Task.Delay;
        if (budgetGuard != null)
        {
                var initialExceeded = budgetGuard.CheckBudget();
                if (initialExceeded != null)
                {
                    throw new AutoPlayStoppedException(initialExceeded, StopKindPolicy.Budget);
                }
        }
        string? Commit(AgentTurnResult receipt, bool interrupted)
        {
            // Ledger and accepted actions describe work already done. Cancellation only prevents
            // future work; it cannot erase these facts. Keep interrupted UI updates optional.
            var reason = budgetGuard?.Observe(receipt);
            (interrupted ? reportInterrupted ?? report : report)(receipt);
            return reason;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (turnGate != null) await turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            string? budgetReason;
            try
            {
                try { result = await turn(cancellationToken); }
                catch (AgentTurnCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    Commit(ex.Receipt, interrupted: true);
                    throw;
                }
                catch (AutoPlayStoppedException ex)
                {
                    if (ex.Receipt != null) Commit(ex.Receipt, interrupted: true);
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { result = new AgentTurnResult { Error = ex.Message }; }
                budgetReason = Commit(result, cancellationToken.IsCancellationRequested);
            }
            finally
            {
                // A waiting model turn must see this turn's receipt before it can start.
                turnGate?.Release();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (budgetReason != null) throw new AutoPlayStoppedException(budgetReason, StopKindPolicy.Budget);
            var next = recovery.Observe(result);
            if (next.StopReason != null) throw new AutoPlayStoppedException(next.StopReason, next.StopKind);
            if (afterTurn != null)
            {
                await afterTurn(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                budgetReason = budgetGuard?.CheckBudget();
                if (budgetReason != null) throw new AutoPlayStoppedException(budgetReason, StopKindPolicy.Budget);
            }
            if (next.Delay > TimeSpan.Zero) await delay(next.Delay, cancellationToken);
        }
    }
}

internal sealed class AutoPlayStoppedException(string message, string? kind = null) : Exception(message)
{
    /// <summary>
    /// The kind the thrower already knows, or null when only the message can describe it.
    /// A typed kind wins over message matching, so a recovery stop cannot be reported as a
    /// budget or network stop just because the underlying error text contained a keyword.
    /// </summary>
    public string? Kind { get; } = kind;

    /// <summary>Completed work before a run-boundary stop, if a model turn had already started.</summary>
    public AgentTurnResult? Receipt { get; set; }
}
