namespace STS2AIAgent.Tests;

/// <summary>
/// The lease is only worth its own tests if the real action path uses it, and
/// <c>GameActionService.cs</c> is not part of the offline compile. So the wiring is pinned from
/// source: <c>ExecuteAsync</c> takes the lease, awaits the dispatched action task inside the
/// <c>try</c>, and releases it in <c>finally</c> -- and the refusal it can answer with is the
/// documented <c>409 action_in_flight</c>, retryable, from a gate that never waits.
/// </summary>
internal static class ActionExecutionGateWiringContractTests
{
    private const string GatePath = "STS2AIAgent/Game/ActionExecutionGate.cs";

    public static void ExecuteAsyncHoldsTheLeaseAcrossTheAwaitedCoreTask()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ExecuteAsync"));

        var acquire = body.IndexOf("ActionExecutionGate.Shared.Acquire(", StringComparison.Ordinal);
        var tryIndex = body.IndexOf("try{", StringComparison.Ordinal);
        var awaitIndex = body.IndexOf("awaitDispatchAsync(", StringComparison.Ordinal);
        var finallyIndex = body.IndexOf("}finally{", StringComparison.Ordinal);
        var release = body.IndexOf("lease.Release();", StringComparison.Ordinal);

        Assert.True(
            acquire >= 0,
            "ExecuteAsync must take the one-action lease before dispatching, so every transport that "
            + "converges on it is serialized by the same guard.");
        Assert.True(
            tryIndex > acquire,
            "The lease must be taken before the guarded block, not inside it: a failure while acquiring "
            + "has nothing to release.");
        Assert.True(
            awaitIndex > tryIndex && (finallyIndex < 0 || awaitIndex < finallyIndex),
            "The lease must cover the awaited action task. Dispatching without awaiting would hand the "
            + "lease back while the action is still clicking and waiting for transitions.");
        Assert.True(
            finallyIndex > awaitIndex,
            "The release must sit in a finally, so an action that throws or is canceled cannot leave the "
            + "mod refusing every later request.");
        Assert.True(
            release > finallyIndex,
            "The finally block must release the lease itself rather than merely existing.");
    }

    public static void TheRefusalIsTheDocumentedActionInFlightError()
    {
        var flat = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read(GatePath));

        var refusal = flat.IndexOf("thrownewApiException(409,\"action_in_flight\"", StringComparison.Ordinal);
        Assert.True(
            refusal >= 0,
            "A concurrent action must be refused with 409 action_in_flight -- the status and the code a "
            + "client looks up in docs/api.md before deciding whether to retry.");
        Assert.Contains(
            "retryable:true",
            flat.Substring(refusal, Math.Min(600, flat.Length - refusal)),
            StringComparison.Ordinal);
        Assert.Contains("in_flight_action", flat, StringComparison.Ordinal);

        // Refusing must not become queueing. A wait primitive here would turn a 409 into a caller that
        // acts on the snapshot it read before the first action started.
        foreach (var waiting in new[]
                 {
                     "Monitor.",
                     "SpinWait",
                     "SemaphoreSlim",
                     "WaitOne",
                     "ManualResetEvent",
                     "AutoResetEvent",
                     "Task.Delay",
                     "Thread.Sleep",
                     "await",
                 })
        {
            Assert.False(
                flat.Contains(waiting, StringComparison.Ordinal),
                $"ActionExecutionGate must refuse immediately, not wait ({waiting}). A queued action would "
                + "run against a state the action ahead of it has already changed.");
        }
    }
}
