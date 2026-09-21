using System.Threading;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>
/// The one lease that decides which game action may run.
/// </summary>
/// <remarks>
/// <para>
/// Every action entry point converges on <c>GameActionService.ExecuteAsync</c> -- <c>POST /action</c>,
/// the in-game agent through <c>GameBridge</c>, the native MCP <c>act</c> tool, the teammate
/// coordinator, the overlay -- and <c>HttpServer</c> dispatches each request on its own task. Before
/// this gate, two requests that arrived together could each validate their indexes against the same
/// state, mutate the game, and then wait for a transition the other one was causing: the second
/// request validated a snapshot the first had already invalidated, and whichever handler settled
/// last decided what <c>status</c> the two callers were told.
/// </para>
/// <para>
/// The gate is deliberately non-blocking. A request that finds the lease held is refused with
/// <c>409 action_in_flight</c> instead of queued, because queueing is the same bug with a delay: the
/// caller would act on the state it read before the first action started. Refusing is safe to retry
/// -- nothing was executed and nothing was clicked -- which is why this is the one action error the
/// mod marks retryable.
/// </para>
/// <para>
/// The lease lives in <c>ExecuteAsync</c>'s <c>finally</c> and covers the whole dispatched task,
/// every transition wait included. A thrown or canceled action therefore cannot leave the mod
/// permanently busy, and a second caller never inherits a lease a dead action forgot to release.
/// </para>
/// </remarks>
internal sealed class ActionExecutionGate
{
    /// <summary>One game, one gate: all transports reach this instance through <c>ExecuteAsync</c>.</summary>
    public static ActionExecutionGate Shared { get; } = new();

    private int _held;
    private string? _inFlightAction;

    /// <summary>True while an action owns the lease; for diagnostics and tests, never for admission.</summary>
    public bool IsHeld => Volatile.Read(ref _held) != 0;

    /// <summary>
    /// Takes the lease for <paramref name="actionName"/>, or refuses immediately.
    /// </summary>
    /// <exception cref="ApiException">
    /// 409 <c>action_in_flight</c>, retryable, when another action already owns the lease.
    /// </exception>
    public Lease Acquire(string? actionName = null)
    {
        if (Interlocked.CompareExchange(ref _held, 1, 0) != 0)
        {
            throw new ApiException(
                409,
                "action_in_flight",
                "Another action is still running. Wait for its response, read the state again, then retry.",
                new
                {
                    in_flight_action = Volatile.Read(ref _inFlightAction),
                    action = actionName,
                    hint = "The request was neither executed nor queued; its indexes were never applied to the game.",
                },
                retryable: true);
        }

        Volatile.Write(ref _inFlightAction, actionName);
        return new Lease(this);
    }

    /// <summary>
    /// Frees the lease. Only the owning <see cref="Lease"/> reaches this, and only once: the name is
    /// cleared before the flag so a caller admitted right after never reports the previous action as
    /// still in flight.
    /// </summary>
    private void Release()
    {
        Volatile.Write(ref _inFlightAction, null);
        Volatile.Write(ref _held, 0);
    }

    /// <summary>
    /// The ownership token an action holds until its task completes.
    /// </summary>
    /// <remarks>
    /// Releasing is idempotent and never frees somebody else's lease: the exchange nulls this lease's
    /// own reference, so a duplicate release from a finished action cannot unlock the action that
    /// started after it. <see cref="Release"/> is the explicit name the action path uses; the class is
    /// disposable so a <c>using</c> would be equally safe.
    /// </remarks>
    internal sealed class Lease : IDisposable
    {
        private ActionExecutionGate? _gate;

        internal Lease(ActionExecutionGate gate)
        {
            _gate = gate;
        }

        /// <summary>True once this lease has been released, whatever released it.</summary>
        public bool IsReleased => Volatile.Read(ref _gate) == null;

        public void Release()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }

        public void Dispose() => Release();
    }
}
