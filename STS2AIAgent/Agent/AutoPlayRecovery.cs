namespace STS2AIAgent.Agent;

// Owns retry policy for one automatic session; resuming creates a fresh instance.
internal sealed class AutoPlayRecovery
{
    private int _failures;

    public (string? StopReason, TimeSpan Delay) Observe(AgentTurnResult result)
    {
        if (result.RequiresConfiguration)
            return ("请检查模型、端点或凭据后再继续：" + result.Error, TimeSpan.Zero);

        // Waiting for the human player or an animation must not spend the model retry budget.
        // It also must not erase failures observed before the wait.
        if (result.WaitingForGame)
            return (null, TimeSpan.FromSeconds(1));

        if (result.Error == null && result.Acted != null)
        {
            _failures = 0;
            return (null, TimeSpan.Zero);
        }

        var reason = result.Error ?? "模型未给出可执行动作";
        _failures++;
        return _failures >= 3
            ? ("连续 3 次决策未成功，已停止自动游玩。检查当前局面后可手动继续：" + reason, TimeSpan.Zero)
            : (null, TimeSpan.FromSeconds(Math.Pow(2, _failures)));
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
                throw new AutoPlayStoppedException(initialExceeded);
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
                var budgetReason = budgetGuard.Observe(result);
                if (budgetReason != null) throw new AutoPlayStoppedException(budgetReason);
            }
            var next = recovery.Observe(result);
            if (next.StopReason != null) throw new AutoPlayStoppedException(next.StopReason);
            if (next.Delay > TimeSpan.Zero) await delay(next.Delay, cancellationToken);
        }
    }
}

internal sealed class AutoPlayStoppedException(string message) : Exception(message);
