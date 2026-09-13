using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Guards the structured invite outcome. The old handler matched Chinese substrings in the
/// localized <c>DualStatus</c> text, so a failure was reported as success in any client whose
/// display language was not Chinese. These tests pin the classification truth table, prove the
/// handler no longer touches display text, and keep every launch branch recording a result.
/// </summary>
internal static class DualLaunchOutcomeTests
{
    /// <summary>Every enum value is exercised, and the two predicates are pinned per value.</summary>
    public static void EveryOutcomeHasAPinnedClassification()
    {
        var values = Enum.GetValues<DualLaunchOutcome>();
        Assert.Equal(6, values.Length);
        Assert.Equal(6, values.Distinct().Count());

        Assert.False(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Idle));
        Assert.False(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.InProgress));
        Assert.False(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Succeeded));
        Assert.True(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Rejected));
        Assert.True(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Failed));
        Assert.True(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Canceled));

        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Idle));
        Assert.True(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.InProgress));
        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Succeeded));
        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Rejected));
        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Failed));
        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Canceled));
    }

    /// <summary>
    /// Only <c>Succeeded</c> may be reported as completion: it is neither a failure nor in
    /// progress. A value that is one of those two buckets can never masquerade as success.
    /// </summary>
    public static void OnlyAConfirmedLaunchIsNeitherFailureNorInProgress()
    {
        foreach (var outcome in Enum.GetValues<DualLaunchOutcome>())
        {
            var failure = DualLaunchOutcomePolicy.IsFailure(outcome);
            var inProgress = DualLaunchOutcomePolicy.IsInProgress(outcome);
            Assert.False(
                failure && inProgress,
                $"Outcome {outcome} cannot be both a failure and in progress.");
        }

        Assert.False(DualLaunchOutcomePolicy.IsFailure(DualLaunchOutcome.Succeeded));
        Assert.False(DualLaunchOutcomePolicy.IsInProgress(DualLaunchOutcome.Succeeded));
    }

    /// <summary>
    /// Source contract: the handler reads the structured outcome and no longer inspects the
    /// localized message with substring matching.
    /// </summary>
    public static void HandlerClassifiesOnTheOutcomeNotOnDisplayText()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var handler = AgentSourceFixture.MethodBody(source, "ExecuteInviteAiTeammateAsync");

        Assert.False(
            source.Contains("Contains(\"失败\"", StringComparison.Ordinal),
            "GameActionService.cs still matches the Chinese failure word in DualStatus.");
        Assert.False(
            source.Contains("Contains(\"请先\"", StringComparison.Ordinal),
            "GameActionService.cs still matches the Chinese precondition word in DualStatus.");
        Assert.False(
            source.Contains("Contains(\"找不到\"", StringComparison.Ordinal),
            "GameActionService.cs still matches the Chinese not-found word in DualStatus.");

        Assert.Contains("AgentRuntime.Instance.DualLaunchOutcome", handler);
        Assert.Contains("DualLaunchOutcomePolicy.IsFailure(outcome)", handler);
        Assert.Contains("DualLaunchOutcomePolicy.IsInProgress(outcome)", handler);
        Assert.False(
            handler.Contains("message.Contains(", StringComparison.Ordinal),
            "The invite handler must not classify by matching the localized message.");
    }

    /// <summary>
    /// Contract: every branch of the launch core records an outcome, and the gate-contention
    /// branch records <c>InProgress</c> instead of leaving the previous attempt's result behind.
    /// </summary>
    public static void EveryLaunchBranchRecordsAnOutcome()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        var core = AgentSourceFixture.MethodBody(source, "LaunchDualInstanceCoreAsync");

        var writes = core.Split("_dualLaunchOutcome =", StringSplitOptions.None).Length - 1;
        Assert.True(
            writes >= 6,
            $"Every launch branch must record an outcome; found {writes} assignments.");

        Assert.Contains("DualLaunchOutcome _dualLaunchOutcome = DualLaunchOutcome.Idle", source);
        Assert.Contains("_dualLaunchOutcome = DualLaunchOutcome.InProgress", core);
        Assert.Contains("_dualLaunchOutcome = DualLaunchOutcome.Rejected", core);
        Assert.Contains("_dualLaunchOutcome = DualLaunchOutcome.Canceled", core);
        Assert.Contains("_dualLaunchOutcome = DualLaunchOutcome.Failed", core);
        Assert.Contains("DualLaunchOutcome.Succeeded", core);
    }

    /// <summary>
    /// Contract: the coordinator exposes a structured result and its legacy string entry point
    /// delegates to it, so the unchanged display text cannot drift from the boolean.
    /// </summary>
    public static void CoordinatorExposesAStructuredResult()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs");
        var structured = AgentSourceFixture.MethodBody(source, "HostLocalCoopResultAsync");
        var legacy = AgentSourceFixture.MethodBody(source, "HostLocalCoopAsync");

        Assert.Contains("Task<(bool Ok, string Message)> HostLocalCoopResultAsync", source);
        Assert.Contains("return (false,", structured);
        Assert.Contains("return (true,", structured);
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", structured);
        Assert.Contains("await HostLocalCoopResultAsync(cancellationToken, companionAutoPlay)", legacy);
        Assert.Contains("return result.Message;", legacy);
    }

    /// <summary>The outcome type is offline compilable: no Godot, no game assembly.</summary>
    public static void TheOutcomeTypeStaysOfflineCompilable()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/DualLaunchOutcome.cs");

        Assert.False(source.Contains("using Godot", StringComparison.Ordinal), "DualLaunchOutcome.cs must not reference Godot.");
        Assert.False(source.Contains("using MegaCrit", StringComparison.Ordinal), "DualLaunchOutcome.cs must not reference the game assembly.");
        Assert.Contains("enum DualLaunchOutcome", source);
        Assert.Contains("class DualLaunchOutcomePolicy", source);
    }
}
