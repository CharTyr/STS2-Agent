namespace STS2AIAgent.Tests;

/// <summary>
/// The one-state-build-one-gate contract. available_actions, combat.action_readiness and the potion
/// flags of one payload all answer from a single gate evaluation, because the gate advances a shared
/// 200 ms stability sampler. Evaluating it more than once per response let a payload contradict
/// itself: the actions serialized first said "not yet" (no play_card, no end_turn) while the
/// readiness payload built later in the same response said "ready" with a stable snapshot.
/// </summary>
internal static class GameStateCombatGateContractTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";

    public static void OneStateBuildEvaluatesTheGateOnce()
    {
        var state = Flat(AgentSourceFixture.MethodBody(ReadSource(), "BuildStatePayload"));

        Assert.Equal(1, Occurrences(state, "EvaluateCombatActionGate("));
        Assert.Contains(
            "varcombatActionGate=EvaluateCombatActionGate(currentScreen,combatState);",
            state,
            StringComparison.Ordinal);
        Assert.Contains(
            "BuildAvailableActionNames(currentScreen,combatState,runState,combatActionGate)",
            state,
            StringComparison.Ordinal);
        Assert.Contains("BuildCombatPayload(combatState,combatActionGate)", state, StringComparison.Ordinal);
        Assert.Contains(
            "BuildRunPayload(currentScreen,combatState,runState,combatActionGate)",
            state,
            StringComparison.Ordinal);
    }

    public static void AvailableActionsAskTheSharedGate()
    {
        var state = ReadSource();
        var names = Flat(AgentSourceFixture.MethodBody(state, "BuildAvailableActionNames"));
        var descriptors = Flat(AgentSourceFixture.MethodBody(state, "BuildAvailableActionsPayload"));

        // The name list receives the gate; only the descriptor endpoint, which is its own request,
        // evaluates one. Neither may evaluate it twice.
        Assert.False(
            names.Contains("EvaluateCombatActionGate(", StringComparison.Ordinal),
            "BuildAvailableActionNames must take the gate instead of evaluating one per action.");
        Assert.Equal(1, Occurrences(descriptors, "EvaluateCombatActionGate("));

        foreach (var body in new[] { names, descriptors })
        {
            Assert.Contains(
                "if(CanEndTurn(currentScreen,combatState,requireButtonReady:false,combatActionGate:combatActionGate))",
                body,
                StringComparison.Ordinal);
            Assert.Contains("if(CanPlayAnyCard(currentScreen,combatState,combatActionGate))", body, StringComparison.Ordinal);
            Assert.Contains(
                "if(CanUsePotion(currentScreen,combatState,runState,combatActionGate))",
                body,
                StringComparison.Ordinal);

            // The probes must not reach past the gate: CanUseCombatActions would evaluate the
            // stability sampler a second time and could answer from a later moment than the gate.
            Assert.False(
                body.Contains("CanUseCombatActions(", StringComparison.Ordinal),
                "A gated action list must not call CanUseCombatActions directly.");
        }
    }

    public static void ReadinessIsAProjectionOfTheGate()
    {
        var state = ReadSource();
        var readiness = Flat(AgentSourceFixture.DeclarationBody(
            state,
            "private static CombatActionReadinessPayload BuildCombatActionReadinessPayload("));
        var gate = Flat(AgentSourceFixture.DeclarationBody(
            state,
            "private static CombatActionGate EvaluateCombatActionGate("));
        // CanUseCombatActions is called later in the file than it is declared, so its body has to be
        // located by declaration instead of by last mention.
        var canUse = Flat(AgentSourceFixture.DeclarationBody(
            state,
            "private static bool CanUseCombatActions("));

        Assert.Contains("can_use_combat_actions=gate.Usable", readiness, StringComparison.Ordinal);
        Assert.Contains("reason=gate.Reason", readiness, StringComparison.Ordinal);
        Assert.Contains("snapshot_stable=gate.SnapshotStable", readiness, StringComparison.Ordinal);
        Assert.False(
            readiness.Contains("IsCombatActionSnapshotStable(", StringComparison.Ordinal)
            || readiness.Contains("IsCombatActionSnapshotCurrentlyStable(", StringComparison.Ordinal)
            || readiness.Contains("ActionQueueSet.GetReadyAction()", StringComparison.Ordinal)
            || readiness.Contains("GetOpenModal()", StringComparison.Ordinal),
            "Readiness must project the gate, not sample the live queue or overlay on its own.");

        // The read-only twin is gone: it was the half that could disagree inside one payload.
        Assert.False(
            Flat(state).Contains("IsCombatActionSnapshotCurrentlyStable", StringComparison.Ordinal),
            "IsCombatActionSnapshotCurrentlyStable must not come back; readiness reads the gate.");

        // The gate is the one place the sampler advances, and it keeps every lock readiness reports.
        Assert.Contains("IsCombatActionSnapshotStable(combatState,me!)", gate, StringComparison.Ordinal);
        foreach (var reason in new[]
                 {
                     "\"modal_open\"",
                     "\"combat_screen_unavailable\"",
                     "\"combat_not_in_progress\"",
                     "\"combat_over_or_ending\"",
                     "\"combat_paused\"",
                     "\"player_actions_disabled\"",
                     "\"combat_room_not_active\"",
                     "\"hand_unavailable\"",
                     "\"hand_in_card_play\"",
                     "\"hand_in_card_selection\"",
                     "\"hand_mode_not_play\"",
                     "\"local_player_dead\"",
                     "\"local_turn_not_ready\"",
                     "\"game_action_running\"",
                     "\"game_action_queued\"",
                     "\"action_queue_unsettled\"",
                     "\"not_player_action_phase\"",
                     "\"snapshot_stabilizing\"",
                 })
        {
            Assert.Contains(reason, gate, StringComparison.Ordinal);
        }

        // A caller that already holds the gate must not evaluate it again.
        Assert.Contains(
            "vargate=combatActionGate??EvaluateCombatActionGate(currentScreen,combatState);",
            canUse,
            StringComparison.Ordinal);
    }

    private static string ReadSource() => AgentSourceFixture.Read(StatePath);

    private static string Flat(string source) => AgentSourceFixture.WithoutWhitespace(source);

    private static int Occurrences(string source, string value)
    {
        var count = 0;
        var index = source.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
