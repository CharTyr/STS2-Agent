using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AIAgent.Game;

/// <summary>
/// Cheap reads for the auto-play loop's hot paths: the wait for an actionable frame and the per-turn
/// snapshot. Both used to build the whole /state payload (deck, map, inventories, compact view) only
/// to look at a handful of fields -- on every rendered frame while waiting, which was the main source
/// of frame drops while the AI played.
/// </summary>
internal static partial class GameStateService
{
    internal sealed record ActionabilityProbe(
        string Screen,
        CombatActionReadinessPayload? Readiness,
        IReadOnlyList<string> AvailableActions);

    /// <summary>The five fields the auto-play turn reads before it decides anything.</summary>
    internal sealed record TurnProbe(
        string Screen,
        string Phase,
        string RunId,
        bool InCombat,
        string? ActId);

    /// <summary>
    /// Read the actionable wait's signals without constructing the deck, map, inventories or compact
    /// view. Must run on the game thread. Dispatch still validates a fresh full decision snapshot.
    /// </summary>
    /// <remarks>
    /// Preserve the full builder's order: evaluate the shared combat gate once (advancing its 200 ms
    /// stability sampler), enumerate the same action surface, then sync card-play counters as
    /// BuildCombatPayload does. The ordering is protected by ActionabilityProbeContractTests.
    /// </remarks>
    public static ActionabilityProbe ProbeActionable()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = ResolveScreen(currentScreen);
        var combatActionGate = EvaluateCombatActionGate(currentScreen, combatState);
        var names = BuildAvailableActionNames(currentScreen, combatState, runState, combatActionGate, out _);

        CombatActionReadinessPayload? readiness = null;
        var me = GetLocalPlayer(combatState);
        if (combatState != null && me?.PlayerCombatState != null)
        {
            GameActionService.SyncCardPlayCounters(combatState.RoundNumber);
            readiness = BuildCombatActionReadinessPayload(combatActionGate);
        }

        StateBuildTiming.Instance.CountActionableProbe();
        return new ActionabilityProbe(screen, readiness, names);
    }

    /// <summary>
    /// The auto-play turn's snapshot (screen, session phase, run id, in-combat, act) without building
    /// the payload. Must run on the game thread.
    /// </summary>
    /// <remarks>
    /// Every field comes from the same source the full build uses (<see cref="ResolveScreen"/>,
    /// <c>BuildSessionPayload</c>, <c>RunState.Rng.StringSeed</c>, <c>CombatManager.IsInProgress</c>,
    /// <c>RunState.CurrentActIndex</c>), read in one game-thread unit, so it is the same fresh frame
    /// the session rules require before switching or clearing a persisted session. It deliberately
    /// does not evaluate the combat gate: the turn snapshot never used readiness, and advancing the
    /// 200 ms sampler from here would change what the next real build reports.
    /// </remarks>
    public static TurnProbe BuildTurnProbe()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var runState = RunManager.Instance.DebugOnlyGetState();
        return new TurnProbe(
            ResolveScreen(currentScreen),
            BuildSessionPayload(currentScreen, runState).phase,
            runState?.Rng.StringSeed ?? "run_unknown",
            CombatManager.Instance.IsInProgress,
            // run.act_id is null whenever the run payload is (no run, or no local player yet).
            runState != null && GetLocalPlayer(runState) != null ? runState.CurrentActIndex.ToString() : null);
    }
}
