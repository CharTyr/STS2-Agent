namespace STS2AIAgent.Tests;

/// <summary>
/// Registration of the 2026-10-28 experience-overhaul tests, kept in one place so
/// <see cref="TestRunner"/> gains a single line instead of one per test.
/// </summary>
internal static class ExperienceTestCatalog
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("Experience.CombatMath.SafeCacheAndTargets", () => Task.Run(CombatMathSafetyContractTests.CacheAndTargetingAreConservative));
        yield return ("Experience.TurnPlan.DispatchChecksIdentity", AgentLoopTests.Plan_CardIdentityIsRecheckedAtDispatch);
        yield return ("Experience.NativeMcp.CombatPlanStopsSafely", McpServiceTests.CombatPlansExecuteAndStopWithoutReplay);
        yield return ("Experience.Settings.DefaultsAreOn", () => Task.Run(ExperienceSettingsTests.DefaultsAreOn));
        yield return ("Experience.Settings.LegacyFileGetsTheNewDefaults", () => Task.Run(ExperienceSettingsTests.LegacyFileGetsTheNewDefaults));
        yield return ("Experience.Settings.JunkIsNormalised", () => Task.Run(ExperienceSettingsTests.JunkIsNormalised));
        yield return ("Experience.Settings.CloneCarriesEveryNonDefault", () => Task.Run(ExperienceSettingsTests.CloneCarriesEveryNonDefault));
        yield return ("Experience.Settings.TeammateGetsPlaySwitchesButNotLocalOnes", () => Task.Run(ExperienceSettingsTests.TeammateGetsPlaySwitchesButNotLocalOnes));
        yield return ("Experience.Settings.TeammateRejectsAnUnknownSpeed", () => Task.Run(ExperienceSettingsTests.TeammateRejectsAnUnknownSpeed));
        yield return ("Experience.Turbo.FollowsSettingNeverSlows", () => Task.Run(OverhaulSmallPolicyTests.TurboFollowsTheSettingAndNeverSlowsThePlayer));
        yield return ("Experience.Turbo.MultiplayerCap", () => Task.Run(OverhaulSmallPolicyTests.TurboCapsMultiplayerAtFast));
        yield return ("Experience.Turbo.RestoreOwnValueRoundTrip", () => Task.Run(OverhaulSmallPolicyTests.TurboRestoresOnlyItsOwnValueAndRoundTrips));
        yield return ("Experience.Vision.OnDemand", () => Task.Run(OverhaulSmallPolicyTests.VisionAttachesOnlyWhenTheStateMayNotBeEnough));
        yield return ("Experience.StallHint.Thresholds", () => Task.Run(OverhaulSmallPolicyTests.StallHintsWaitForTheirThresholds));
        yield return ("Experience.PollCadence.BackOffSnapBack", () => Task.Run(OverhaulSmallPolicyTests.PollCadenceBacksOffAndSnapsBack));
        yield return ("Experience.CombatMath.Threat", () => Task.Run(CombatMathPolicyTests.ThreatSumsLivingAttackersAndBlock));
        yield return ("Experience.CombatMath.UnblockableAndSelfDamage", () => Task.Run(CombatMathPolicyTests.ThreatCountsUnblockableAndSelfDamage));
        yield return ("Experience.CombatMath.Cells", () => Task.Run(CombatMathPolicyTests.CellsSoakBlockAndKillExactly));
        yield return ("Experience.CombatMath.CheapestKill", () => Task.Run(CombatMathPolicyTests.KillOptionsPreferTheCheapestSet));
        yield return ("Experience.CombatMath.BlockSoaksOnce", () => Task.Run(CombatMathPolicyTests.KillOptionsSoakEnemyBlockOnce));
        yield return ("Experience.CombatMath.XCost", () => Task.Run(CombatMathPolicyTests.XCostCardsSpendEverything));
        yield return ("Experience.CombatMath.MaxBlock", () => Task.Run(CombatMathPolicyTests.MaxBlockPicksTheBestAffordableSet));
        yield return ("Experience.Autopilot.CombatNoMoves", () => Task.Run(AutopilotPolicyTests.CombatWithNothingToDoEndsTheTurn));
        yield return ("Experience.Autopilot.CombatChoicesToModel", () => Task.Run(AutopilotPolicyTests.CombatWithAnyChoiceGoesToTheModel));
        yield return ("Experience.Autopilot.Guards", () => Task.Run(AutopilotPolicyTests.GuardsYieldToThePlayerAndToNonCombatMode));
        yield return ("Experience.Autopilot.SinglePath", () => Task.Run(AutopilotPolicyTests.ASinglePathOnTheMapIsTaken));
        yield return ("Experience.Autopilot.Rewards", () => Task.Run(AutopilotPolicyTests.RewardsWithoutChoicesAreCollected));
        yield return ("Experience.Autopilot.SingleOptionScreens", () => Task.Run(AutopilotPolicyTests.SingleOptionScreensAreResolved));
        yield return ("Experience.Autopilot.Modals", () => Task.Run(AutopilotPolicyTests.ModalsAreConfirmedOnlyWhenThereIsNoCancel));
        yield return ("Experience.Autopilot.GameOverAndLoneButtons", () => Task.Run(AutopilotPolicyTests.GameOverAndLoneButtonsContinue));
        yield return ("Experience.Autopilot.NeverTradeOffs", () => Task.Run(AutopilotPolicyTests.TradeOffsAreNeverAutomatic));
        yield return ("Experience.Autopilot.LoopSpendsNoRequest", AgentLoopTests.PlayOnce_ForcedMoveSpendsNoRequest);
        yield return ("Experience.Autopilot.LoopYields", AgentLoopTests.PlayOnce_ForcedMoveYieldsToSwitchAndPlayerGuidance);
        yield return ("Experience.PromptDiet.KeepsWhatDecisionsRead", () => Task.Run(PromptStateDietTests.TheWholeDietKeepsWhatDecisionsRead));
        yield return ("Experience.PromptDiet.EachRuleAlone", () => Task.Run(PromptStateDietTests.EachRuleDoesItsOneThing));
        yield return ("Experience.PromptDiet.CoopAndOutOfCombat", () => Task.Run(PromptStateDietTests.MultiplayerAndOutOfCombatKeepTheirData));
        yield return ("Experience.PromptDiet.BadInput", () => Task.Run(PromptStateDietTests.BadInputPassesThrough));
        yield return ("Experience.TurnPlan.ParseVectors", () => Task.Run(TurnPlanVectorTests.ParseVectors));
        yield return ("Experience.TurnPlan.ResolveVectors", () => Task.Run(TurnPlanVectorTests.ResolveVectors));
        yield return ("Experience.TurnPlan.DivergedVectors", () => Task.Run(TurnPlanVectorTests.DivergedVectors));
        yield return ("Experience.TurnPlan.ThreeStepsOneRequest", AgentLoopTests.Plan_ThreeStepsOneRequest);
        yield return ("Experience.TurnPlan.StopsOnSurprises", AgentLoopTests.Plan_StopsOnANewCardOrADeadTarget);
        yield return ("Experience.TurnPlan.RejectedFirstAndSwitchOff", AgentLoopTests.Plan_RejectedFirstActRunsNothingAndSwitchOffIgnoresThen);
        yield return ("Experience.TurnPlan.FollowUpWaitsForSettle", AgentLoopTests.Plan_FollowUpWaitsForTheCardToResolve);
        yield return ("Experience.TurnPlan.PendingTerminal", AgentLoopTests.Plan_PendingStopsWithoutWaitingOrReplay);
        yield return ("Experience.TurnPlan.PlayerGuidanceStops", AgentLoopTests.Plan_PlayerGuidanceStopsTheRest);
        yield return ("Experience.Recovery.TransportLadder", AutoPlayRecoveryTests.TransportFailuresGetTheirOwnLadder);
        yield return ("Experience.Recovery.StalledGameIsAWait", AutoPlayRecoveryTests.AStalledGameThreadIsAWaitNotAFailure);
        yield return ("Experience.Recovery.FillsOnlyPossibleIndex", AgentLoopTests.PlayOnce_FillsTheOnlyPossibleIndex);
        yield return ("Experience.Recovery.LearnsStreamTruncation", AgentLoopTests.PlayOnce_LearnsThatStreamedArgumentsAreTruncated);
        yield return ("Experience.Recovery.ReplyJsonWhenBothToolPathsCut", AgentLoopTests.PlayOnce_FallsBackToReplyJsonWhenBothToolPathsAreCut);
        yield return ("Experience.Recovery.LearnsToolArgsCut", AgentLoopTests.PlayOnce_LearnsThatToolArgumentsAreCutWithoutStreaming);
        yield return ("Experience.Capability.PersistsPerRoute", () => Task.Run(ProviderCapabilityTests.CapabilityPersistsPerRouteAndInvalidatesOnEdit));
        yield return ("Experience.Capability.CloneAndTeammate", () => Task.Run(ProviderCapabilityTests.CloneAndCompanionPayloadCarryTheCapabilityAndOutputCap));
        yield return ("Experience.Capability.SettingsFileRoundTrip", () => Task.Run(ProviderCapabilityTests.CapabilitySurvivesTheSettingsFile));
        yield return ("Experience.Capability.ProbeArguments", () => Task.Run(ProviderCapabilityTests.ProbeArgumentsCompleteOnlyWithEveryIndex));
        yield return ("Experience.Capability.StreamingProbeWire", ProviderCapabilityTests.StreamingProbeReadsTruncationOffTheWire);
        yield return ("Experience.Capability.StreamingProbeStaysStreamed", ProviderCapabilityTests.StreamingProbeKeepsTheRequestOnTheStreamPath);
        yield return ("Experience.Capability.OutputCapField", ProviderCapabilityTests.OutputCapUsesTheCapabilityFieldName);
        yield return ("Experience.Capability.ModelTestRecordsVerdict", ProviderCapabilityTests.ModelTestPersistsTheStreamVerdict);
        yield return ("Experience.Capability.ModelTestUnknown", ProviderCapabilityTests.ModelTestLeavesTheCapabilityUnknownWhenTheProbeCannotRun);
        yield return ("Experience.Capability.ModelTestCompletionTokens", ProviderCapabilityTests.ModelTestRecordsTheCompletionTokensField);
        yield return ("Experience.Capability.LoopReadsVerdict", () => Task.Run(ProviderCapabilityTests.LoopReadsThePersistedVerdict));
        yield return ("Experience.Capability.LoopReadsToolArgsVerdict", () => Task.Run(ProviderCapabilityTests.LoopReadsThePersistedToolArgsVerdict));
        yield return ("Experience.Recovery.EvidenceClock", () => Task.Run(ProviderCapabilityTests.WaitingClockRestartsOnNewEvidence));
        yield return ("Experience.Turbo.EngageFirstReleaseInFinally", () => Task.Run(TurboWiringContractTests.TheLoopEngagesFirstAndReleasesInItsOuterFinally));
        yield return ("Experience.Turbo.ReleaseNotTiedToEndedToken", () => Task.Run(TurboWiringContractTests.ReleaseIsNotTiedToTheEndedLoopsToken));
        yield return ("Experience.Turbo.PrefsGuardAtModStart", () => Task.Run(TurboWiringContractTests.ThePrefsGuardIsInstalledAtModStart));
        yield return ("Experience.Turbo.HealthNeverReadsGame", () => Task.Run(TurboWiringContractTests.TheHealthSnapshotNeverReadsTheGame));
        yield return ("Experience.Benchmark.SharedArithmetic", () => Task.Run(DecisionBenchmarkMathTests.ReproducesEveryArithmeticFixture));
        yield return ("Experience.Hud.IdentityAndExpiry", () => Task.Run(HudIntentTests.UsesStableCardAndEnemySpace));
        yield return ("Experience.Hud.AllySpace", () => Task.Run(HudIntentTests.AllyTargetDoesNotPointAtEnemy));
        yield return ("Experience.Hud.CaptureVisibility", () => Task.Run(HudIntentTests.CaptureHidesHudEvenWithPanelHidden));
        yield return ("ActionTrust.PlayCardIdentity", () => Task.Run(GameActionTrustContractTests.PlayCardIdentityIsVerifiedBeforeTheCardIsPlayed));
        // W5 strategy state and W4.3 numbers in the fast-judgment options.
        yield return ("Experience.DeckStats.SummarizesTheDeck", () => Task.Run(DeckStatsPolicyTests.RunAll));
        yield return ("Experience.Route.AdvisesOnEveryDoor", () => Task.Run(MapRoutePolicyTests.RunAll));
        yield return ("Experience.Jev.NumericOptions", () => Task.Run(JevAgentLogicTests.RunNumericDescriptions));

        // W8.5 run report and W5 bounded run journal.
        yield return ("Experience.RunSummary.GameOverOnly", () => Task.Run(RunSummaryTests.OnlyAGameOverFrameBecomesASummary));
        yield return ("Experience.RunSummary.FactsTravel", () => Task.Run(RunSummaryTests.FactsTravelIntoTheStoredRow));
        yield return ("Experience.RunSummary.DecisionSplit", () => Task.Run(RunSummaryTests.DecisionsAreSplitBySourceAndRun));
        yield return ("Experience.RunSummary.HighlightRules", () => Task.Run(RunSummaryTests.HighlightsArePickedByRuleFromRecordedDecisions));
        yield return ("Experience.RunSummary.CardAndCopy", () => Task.Run(RunSummaryTests.CardAndCopyTextCarryTheSameFacts));
        yield return ("Experience.RunSummary.DigestBound", () => Task.Run(RunSummaryTests.DigestIsBoundedFactsForOneCharacter));
        yield return ("Experience.RunSummary.DiedToIsAnInference", () => Task.Run(RunSummaryTests.DiedToIsOnlyRecordedWhenTheFrameMakesItUnambiguous));
        yield return ("Experience.RunSummary.JournalBounded", () => Task.Run(RunSummaryTests.JournalKeepsOneBoundedRowPerRun));
        yield return ("Experience.RunSummary.JournalRedacted", () => Task.Run(RunSummaryTests.JournalRowsAreRedactedAndRepairedOnRead));
        yield return ("Experience.RunSummary.JournalDigest", () => Task.Run(RunSummaryTests.StoreDigestReadsTheJournal));
        yield return ("Experience.RunSummary.GameOverSourceContract", () => Task.Run(RunSummaryTests.RuntimeOnlyRecordsAConfirmedGameOver));

        // Co-op message progress: the pending line shows the wait and, when known, the teammate's
        // own combat state instead of freezing on the text that was true at send time.
        yield return ("Experience.Coop.MessageElapsed", () => Task.Run(TeamMessageProgressTests.ElapsedSecondsAreWholeClampedAndClockSafe));
        yield return ("Experience.Coop.MessageWaitingLine", () => Task.Run(TeamMessageProgressTests.WaitingLineNamesTheWaitAndTheCombatReason));
        yield return ("Experience.Coop.MessageClockSafety", () => Task.Run(TeamMessageProgressTests.WaitingLineStaysSaneForImpossibleElapsedValues));
    }
}
