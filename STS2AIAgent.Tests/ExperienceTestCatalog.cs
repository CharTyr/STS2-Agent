namespace STS2AIAgent.Tests;

/// <summary>
/// Registration of the 2026-10-28 experience-overhaul tests, kept in one place so
/// <see cref="TestRunner"/> gains a single line instead of one per test.
/// </summary>
internal static class ExperienceTestCatalog
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
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
        yield return ("Experience.TurnPlan.PlayerGuidanceStops", AgentLoopTests.Plan_PlayerGuidanceStopsTheRest);
        yield return ("Experience.Recovery.TransportLadder", AutoPlayRecoveryTests.TransportFailuresGetTheirOwnLadder);
        yield return ("Experience.Recovery.StalledGameIsAWait", AutoPlayRecoveryTests.AStalledGameThreadIsAWaitNotAFailure);
        yield return ("Experience.Recovery.FillsOnlyPossibleIndex", AgentLoopTests.PlayOnce_FillsTheOnlyPossibleIndex);
        yield return ("Experience.Recovery.LearnsStreamTruncation", AgentLoopTests.PlayOnce_LearnsThatStreamedArgumentsAreTruncated);
        yield return ("Experience.Turbo.EngageFirstReleaseInFinally", () => Task.Run(TurboWiringContractTests.TheLoopEngagesFirstAndReleasesInItsOuterFinally));
        yield return ("Experience.Turbo.ReleaseNotTiedToEndedToken", () => Task.Run(TurboWiringContractTests.ReleaseIsNotTiedToTheEndedLoopsToken));
        yield return ("Experience.Turbo.PrefsGuardAtModStart", () => Task.Run(TurboWiringContractTests.ThePrefsGuardIsInstalledAtModStart));
        yield return ("Experience.Turbo.HealthNeverReadsGame", () => Task.Run(TurboWiringContractTests.TheHealthSnapshotNeverReadsTheGame));
    }
}
