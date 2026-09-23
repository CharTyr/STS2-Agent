namespace STS2AIAgent.Agent;

/// <summary>
/// Turns one decision snapshot into the concrete action Jev executes this frame.
/// </summary>
/// <remarks>
/// The decider is the seam the two-layer engine introduces: the fast execution model implements it and
/// answers per frame, while the LLM only writes the <see cref="PlayStrategy"/> that steers it. Keeping
/// the decision behind an interface means the pure model, a scripted test double, and any future
/// fallback can be swapped without the orchestrator knowing which one answered -- and, because the
/// whole contract is action strings, indices and a single JSON snapshot, it stays Godot-free and
/// compiles into the offline test assembly.
/// </remarks>
internal interface IActionDecider
{
    /// <summary>
    /// Decide the next action from the single-frame snapshot and the current strategy. The
    /// cancellation token is the orchestrator's turn gate: a canceled turn must surface as a failed
    /// decision, never as a dispatched action on a frame the game has already left.
    /// </summary>
    Task<ExecutionDecision> DecideAsync(string snapshotJson, PlayStrategy strategy, CancellationToken cancellationToken);
}
