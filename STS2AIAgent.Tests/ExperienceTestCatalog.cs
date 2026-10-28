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
        yield return ("Experience.Turbo.EngageFirstReleaseInFinally", () => Task.Run(TurboWiringContractTests.TheLoopEngagesFirstAndReleasesInItsOuterFinally));
        yield return ("Experience.Turbo.ReleaseNotTiedToEndedToken", () => Task.Run(TurboWiringContractTests.ReleaseIsNotTiedToTheEndedLoopsToken));
        yield return ("Experience.Turbo.PrefsGuardAtModStart", () => Task.Run(TurboWiringContractTests.ThePrefsGuardIsInstalledAtModStart));
        yield return ("Experience.Turbo.HealthNeverReadsGame", () => Task.Run(TurboWiringContractTests.TheHealthSnapshotNeverReadsTheGame));
    }
}
