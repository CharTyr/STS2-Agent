using System.Diagnostics;
using System.Text.Json;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The one-action lease, offline. Two requests arriving together used to validate their indexes
/// against the same state, mutate the game, and then wait for a transition the other one was causing;
/// <see cref="ActionExecutionGate"/> is what stops that, so its own rules are pinned here rather than
/// only at the call site: the first action owns it, a concurrent one is refused without waiting, and
/// no thrown, canceled or duplicated release can leave it held.
/// </summary>
internal static class ActionExecutionGateTests
{
    public static void AFreshGateAdmitsTheFirstAction()
    {
        var gate = new ActionExecutionGate();
        Assert.False(gate.IsHeld, "A gate nobody has taken must not report itself held.");

        var lease = gate.Acquire("play_card");

        Assert.True(gate.IsHeld, "The first action must own the gate for as long as its task runs.");
        Assert.False(lease.IsReleased, "A lease that was never released must not claim to be.");

        lease.Release();

        Assert.False(gate.IsHeld, "Releasing the lease must free the gate.");
        Assert.True(lease.IsReleased, "The released lease must report itself released.");
    }

    public static void AHeldGateRefusesTheSecondActionImmediately()
    {
        var gate = new ActionExecutionGate();
        var lease = gate.Acquire("play_card");
        var stopwatch = Stopwatch.StartNew();

        var refusal = Refusal(gate, "end_turn");

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            "Acquire must refuse a concurrent action, never wait for the lease; a queue would hand this "
            + "caller the state the first action has already left.");

        Assert.Equal(409, refusal.StatusCode);
        Assert.Equal("action_in_flight", refusal.Code);
        Assert.True(
            refusal.Retryable,
            "The refusal executed nothing, so it is the one action failure a caller may safely retry.");
        Assert.True(
            gate.IsHeld,
            "A refused caller must not disturb the lease the running action owns.");

        // The details a client needs to tell "something else is running" from "this request was wrong".
        var details = JsonSerializer.Serialize(refusal.Details);
        Assert.Contains("play_card", details, StringComparison.Ordinal);
        Assert.Contains("in_flight_action", details, StringComparison.Ordinal);

        lease.Release();
    }

    public static async Task TheLeaseIsHeldUntilTheAwaitedCoreTaskCompletes()
    {
        var gate = new ActionExecutionGate();
        var core = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = ExecuteLikeTheActionPathAsync(gate, "play_card", core.Task);

        Assert.True(
            gate.IsHeld,
            "The lease must cover the action's own task, every transition wait included -- releasing it "
            + "when the handler is merely started would let the next request run beside it.");
        Refusal(gate, "end_turn");

        core.SetResult(true);
        await running;

        Assert.False(gate.IsHeld, "The lease must end with the action's task.");
        gate.Acquire("end_turn").Release();
    }

    public static async Task AThrownCoreFailureStillReleasesTheLease()
    {
        var gate = new ActionExecutionGate();
        var failure = new InvalidOperationException("handler failed");
        var propagated = false;

        try
        {
            await ExecuteLikeTheActionPathAsync(gate, "end_turn", Task.FromException(failure));
        }
        catch (InvalidOperationException caught) when (ReferenceEquals(caught, failure))
        {
            propagated = true;
        }

        Assert.True(propagated, "An action that throws must still surface its own failure.");
        Assert.False(
            gate.IsHeld,
            "A thrown action must release the lease in finally, or the mod refuses every later request "
            + "until it is restarted.");
        gate.Acquire("end_turn").Release();
    }

    public static async Task ACanceledCoreWaitStillReleasesTheLease()
    {
        var gate = new ActionExecutionGate();
        using var cancellation = new CancellationTokenSource();
        var running = ExecuteLikeTheActionPathAsync(
            gate,
            "end_turn",
            Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token));

        cancellation.Cancel();
        var canceled = false;
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        Assert.True(canceled, "A canceled action wait must propagate cancellation to its caller.");
        Assert.False(gate.IsHeld, "A canceled action must release the lease in finally.");
        gate.Acquire("end_turn").Release();
    }

    public static void ReleasingTwiceNeverFreesAnotherActionsLease()
    {
        var gate = new ActionExecutionGate();
        var first = gate.Acquire("play_card");

        first.Release();
        first.Release();

        var second = gate.Acquire("end_turn");
        Assert.True(gate.IsHeld, "The second action must own the gate while its task runs.");

        // A late second release from a finished action must be a no-op, not a licence for the next
        // request to run beside the one that is actually in flight.
        first.Release();
        Assert.True(gate.IsHeld, "A duplicate release must never free the action that took the lease next.");

        second.Release();
        Assert.False(gate.IsHeld);
    }

    public static void DisposingTheLeaseReleasesIt()
    {
        var gate = new ActionExecutionGate();

        using (gate.Acquire("proceed"))
        {
            Assert.True(gate.IsHeld);
        }

        Assert.False(gate.IsHeld, "Disposing the lease must free the gate the way Release does.");
        gate.Acquire("proceed").Release();
    }

    /// <summary>
    /// <c>ExecuteAsync</c>'s own shape, reduced to the part that runs offline: take the lease, await
    /// the dispatched core task, release it in <c>finally</c>. The real call site is pinned by
    /// <see cref="ActionExecutionGateWiringContractTests"/>; this is the behaviour that shape has to
    /// have for every one of the three outcomes -- completion, failure, cancellation.
    /// </summary>
    private static async Task ExecuteLikeTheActionPathAsync(ActionExecutionGate gate, string action, Task core)
    {
        var lease = gate.Acquire(action);
        try
        {
            await core;
        }
        finally
        {
            lease.Release();
        }
    }

    private static ApiException Refusal(ActionExecutionGate gate, string action)
    {
        try
        {
            gate.Acquire(action);
        }
        catch (ApiException refusal)
        {
            return refusal;
        }

        throw new Exception($"Expected {action} to be refused while another action owns the gate.");
    }
}
