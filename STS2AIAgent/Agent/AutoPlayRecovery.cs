namespace STS2AIAgent.Agent;

// Owns retry policy for one automatic session; resuming creates a fresh instance.
internal sealed class AutoPlayRecovery
{
    private int _failures;

    public (string? StopReason, string? StopKind, TimeSpan Delay) Observe(AgentTurnResult result)
    {
        if (result.RequiresConfiguration)
            return ("请检查模型、端点或凭据后再继续：" + result.Error, StopKindPolicy.Configuration, TimeSpan.Zero);

        // Waiting for the human player or an animation must not spend the model retry budget.
        // It also must not erase failures observed before the wait.
        if (result.WaitingForGame)
            return (null, null, TimeSpan.FromSeconds(1));

        if (result.Error == null && result.Acted != null)
        {
            _failures = 0;
            return (null, null, TimeSpan.Zero);
        }

        var reason = result.Error ?? "模型未给出可执行动作";
        _failures++;
        // The kind stays unset so the appended reason still supplies the useful distinction:
        // a dead endpoint should read as a network stop, a rejected key as a config stop.
        return _failures >= 3
            ? ("连续 3 次决策未成功，已停止自动游玩。检查当前局面后可手动继续：" + reason, null, TimeSpan.Zero)
            : (null, null, TimeSpan.FromSeconds(Math.Pow(2, _failures)));
    }

    public static async Task RunAsync(
        Func<CancellationToken, Task<AgentTurnResult>> turn,
        Action<AgentTurnResult> report,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        SessionBudgetGuard? budgetGuard = null)
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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AgentTurnResult result;
            try { result = await turn(cancellationToken); }
            catch (AutoPlayStoppedException) { throw; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { result = new AgentTurnResult { Error = ex.Message }; }
            cancellationToken.ThrowIfCancellationRequested();
            report(result);
            if (budgetGuard != null)
            {
                // Record the finished turn, then stop before starting another.
                var budgetReason = budgetGuard.Observe(result) ?? budgetGuard.CheckBudget();
                if (budgetReason != null) throw new AutoPlayStoppedException(budgetReason, StopKindPolicy.Budget);
            }
            var next = recovery.Observe(result);
            if (next.StopReason != null) throw new AutoPlayStoppedException(next.StopReason, next.StopKind);
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
}
