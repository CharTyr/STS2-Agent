namespace STS2AIAgent.Tests;

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new Exception(message ?? "Expected true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new Exception(message ?? "Expected false.");
        }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(message ?? $"Expected {expected}, actual {actual}.");
        }
    }

    public static void Null(object? value)
    {
        if (value != null)
        {
            throw new Exception("Expected null.");
        }
    }

    public static void NotNull(object? value)
    {
        if (value == null)
        {
            throw new Exception("Expected non-null.");
        }
    }

    public static void NotEmpty<T>(IEnumerable<T> values)
    {
        if (!values.Any())
        {
            throw new Exception("Expected non-empty.");
        }
    }

    public static void Single<T>(IEnumerable<T> values)
    {
        var count = values.Count();
        if (count != 1)
        {
            throw new Exception($"Expected 1 item, actual {count}.");
        }
    }

    public static void Contains(string expected, string? actual, StringComparison comparison = StringComparison.Ordinal)
    {
        if (actual == null || actual.IndexOf(expected, comparison) < 0)
        {
            throw new Exception($"Expected '{actual}' to contain '{expected}'.");
        }
    }

    public static void EndsWith(string expected, string? actual, StringComparison comparison = StringComparison.Ordinal)
    {
        if (actual == null || !actual.EndsWith(expected, comparison))
        {
            throw new Exception($"Expected '{actual}' to end with '{expected}'.");
        }
    }
}

internal static class TestRunner
{
    public static int Run(IEnumerable<(string Name, Func<Task> Body)> tests)
    {
        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Body().GetAwaiter().GetResult();
                Console.WriteLine("PASS  " + test.Name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL  " + test.Name);
                Console.WriteLine("      " + ex.Message);
            }
        }

        return failed;
    }

    public static void Main()
    {
        var failed = Run(AllTests());
        if (failed > 0)
        {
            Environment.Exit(1);
        }
    }

    private static IEnumerable<(string Name, Func<Task> Body)> AllTests()
    {
        foreach (var test in AuditRegressionTests.All()) yield return test;
        foreach (var test in AuditRemediationJevTests.All()) yield return test;
        foreach (var test in AuditRemediationSessionTests.All()) yield return test;
        yield return ("ModeSwitch.ConfirmedPause", () => Task.Run(AuditRemediationUiTests.ModeSwitchRequiresConfirmedPause));
        yield return ("Companion.JevPatchOnly", () => Task.Run(AuditRemediationUiTests.CompanionPatchOnlyChangesJevFields));
        yield return ("Companion.InvalidPatch", () => Task.Run(AuditRemediationUiTests.CompanionPatchRejectsUnsafeValuesBeforeMutation));
        yield return ("Companion.SafeStatus", () => Task.Run(AuditRemediationUiTests.CompanionStatusDoesNotReturnKey));
        yield return ("Briefing.FiltrationAndRedaction", () => Task.Run(AuditRemediationBriefingTests.Build_FiltersOldRunsAndRedactsDecisionDetails));
        yield return ("Briefing.UnknownRunDoesNotFallback", () => Task.Run(AuditRemediationBriefingTests.Build_UnknownRunNeverFallsBackToOldDecisions));
        yield return ("Briefing.BoundedOrdering", () => Task.Run(AuditRemediationBriefingTests.Build_BoundsDecisionsAndKeepsTheirOrder));
        yield return ("Briefing.UnscoredDoesNotInventTrend", () => Task.Run(AuditRemediationBriefingTests.Build_NoScoreDoesNotInventTrend));
        yield return ("FirstRun.DefaultUnverified", () => Task.Run(PlayerExperienceTests.DefaultSettingsAreUnverifiedAndNotInvitable));
        yield return ("FirstRun.VerifiedInvite", () => Task.Run(PlayerExperienceTests.VerifiedPlayFingerprintAllowsInvite));
        yield return ("FirstRun.KeyChangeInvalidates", () => Task.Run(PlayerExperienceTests.ChangingKeyInvalidatesVerification));
        yield return ("Probe.Play401NotMaskedByChat", PlayerExperienceTests.ConversationSuccessDoesNotMarkPlayWhenPlayReturns401);
        yield return ("Probe.SkipFreshVerified", PlayerExperienceTests.FreshVerifiedFingerprintSkipsRetest);
        yield return ("Settings.EndpointRemovalBlocked", () => Task.Run(PlayerExperienceTests.EndpointRemovalRequiresConfirmationWhenReferenced));
        yield return ("Settings.ModelRemovalBlocked", () => Task.Run(PlayerExperienceTests.ModelRemovalRequiresConfirmationWhenBound));
        yield return ("Settings.EndpointRemovalDisabledReference", () => Task.Run(SettingsExperienceRegressionTests.EndpointRemovalBlocksDisabledEndpointReferences));
        yield return ("Settings.EndpointRemovalFallbackPlay", () => Task.Run(SettingsExperienceRegressionTests.EndpointRemovalIncludesFallbackPlayReference));
        yield return ("Settings.ModelRemovalAllRoles", () => Task.Run(SettingsExperienceRegressionTests.ModelRemovalReportsEveryRoleReference));
        yield return ("Settings.ModelRemovalUnreferenced", () => Task.Run(SettingsExperienceRegressionTests.ModelRemovalAllowsUnreferencedModel));
        yield return ("Usage.MissingNotZero", () => Task.Run(PlayerExperienceTests.MissingUsageIsNotDisplayedAsZero));
        yield return ("Usage.JevRateCounts", () => Task.Run(PlayerExperienceTests.JevRateShowsAcceptedAndFallbackCounts));
        yield return ("Usage.SummaryKeepsUnknownAndBudgetReason", () => Task.Run(PlayerExperienceTests.UsageSummaryKeepsUnknownUnknownAndCarriesTheBudgetReason));
        yield return ("Diagnostics.RedactsSecrets", () => Task.Run(PlayerExperienceTests.DiagnosticExportRedactsSecretsAndOmitsChat));
        yield return ("Diagnostics.RedactsAllCredentialShapes", () => Task.Run(RuntimeExperienceRegressionTests.DiagnosticExportRedactsAllCredentialShapes));
        yield return ("Probe.RoleInFallbackError", () => Task.Run(RuntimeExperienceRegressionTests.ModelRoleProbeUsesTheTestedRoleInFallbackErrors));
        yield return ("Probe.FailureKind", () => Task.Run(RuntimeExperienceRegressionTests.ModelRoleProbeClassifiesConfigAndNetworkFailures));
        yield return ("Session.RejectsStaleCompletion", () => Task.Run(RuntimeExperienceRegressionTests.CompletionIdentityRejectsLatePreviousSessionCallbacks));
        yield return ("Session.ClearOwnModelTestStop", () => Task.Run(RuntimeExperienceRegressionTests.SuccessfulModelTestClearsOnlyItsOwnTransientStop));
       yield return ("Session.PauseAndConfigCopy", () => Task.Run(PlayerExperienceTests.PlayerFacingMapsPauseAndConfigError));
       yield return ("Session.RunningBranchReachable", () => Task.Run(PlayerExperienceTests.RunningSessionReportsActionUnlessAModelRoundIsOpen));
        yield return ("Session.StepOnceRecordsBudget", () => Task.Run(SessionControlContractTests.StepOnceRecordsTheSessionBudget));
        yield return ("Mcp.NativeActContract", () => Task.Run(PlayerExperienceTests.NativeMcpToolsMatchGuidedActContract));
        yield return ("CurrentRun.AllowsLobbyBeforeRun", () => Task.Run(CurrentRunBoundaryTests.AllowsLobbyBeforeRun));
        yield return ("CurrentRun.StopsWhenLeavingRunToMainMenu", () => Task.Run(CurrentRunBoundaryTests.StopsWhenLeavingRunToMainMenu));
        yield return ("CurrentRun.StopsWhenLeavingRunToLobby", () => Task.Run(CurrentRunBoundaryTests.StopsWhenLeavingRunToLobby));
        yield return ("CurrentRun.StopsWhenRunIdChanges", () => Task.Run(CurrentRunBoundaryTests.StopsWhenRunIdChanges));
        yield return ("CurrentRun.AllowsGameOverAndUnlock", () => Task.Run(CurrentRunBoundaryTests.AllowsGameOverAndUnlock));
        yield return ("CurrentRun.StopsMainMenuEvenIfSessionPhaseStillRun", () => Task.Run(CurrentRunBoundaryTests.StopsMainMenuEvenIfSessionPhaseStillRun));
       yield return ("CurrentRun.StopsCharacterSelectByScreenName", () => Task.Run(CurrentRunBoundaryTests.StopsCharacterSelectByScreenName));
        yield return ("CurrentRun.FreshSessionAcceptsNewRun", () => Task.Run(CurrentRunBoundaryTests.FreshSessionAcceptsARunThatStartedWhilePaused));
        yield return ("CurrentRun.NonObjectStateIgnored", () => Task.Run(CurrentRunBoundaryTests.NonObjectStatePayloadIsIgnored));
        yield return ("StopKind.RetryIsNotRunEnd", StopKindPolicyTests.RetryStopIsNotRunEnd);
        yield return ("StopKind.BoundaryIsRunEnd", () => Task.Run(StopKindPolicyTests.BoundaryStopsAreRunEnd));
       yield return ("StopKind.OtherKindsSurvive", () => Task.Run(StopKindPolicyTests.BudgetConfigAndNetworkKindsSurvive));
        yield return ("StopKind.ExplicitKindWins", () => Task.Run(StopKindPolicyTests.ExplicitKindBeatsTheMessage));
        yield return ("Recovery.NoActionStops", AutoPlayRecoveryTests.RepeatedNoActionStops);
        yield return ("Recovery.HttpStatus", AutoPlayRecoveryTests.HttpFailuresKeepStatusWithoutStreamReplay);
        yield return ("Recovery.Waiting", AutoPlayRecoveryTests.WaitingDoesNotHideFailures);
        yield return ("Recovery.CompanionMapWait", AutoPlayRecoveryTests.CompanionMapWaitDoesNotStopAutoPlay);
        yield return ("Recovery.PlayerWaitNoStuckClock", AutoPlayRecoveryTests.PlayerDrivenWaitNeverRunsTheStuckClock);
        yield return ("Sweep.HostWatchPid", () => { SevereBugSweepTests.HostWatchReadsPidAndDetectsAbsence(); return Task.CompletedTask; });
        yield return ("Sweep.HostWatchExitedHost", () => Task.Run(SevereBugSweepTests.HostWatchReportsAnExitedHostGone));
        yield return ("Sweep.LauncherHandsHostPid", () => { SevereBugSweepTests.TheLauncherHandsTheHostPidToTheCompanion(); return Task.CompletedTask; });
        yield return ("Sweep.SseLeadingComment", () => { SevereBugSweepTests.SseWithLeadingCommentIsStillSse(); return Task.CompletedTask; });
        yield return ("Sweep.LooseToolCalls", () => { SevereBugSweepTests.LooseToolCallShapesStillParse(); return Task.CompletedTask; });
        yield return ("Sweep.StreamedToolCallIds", () => { SevereBugSweepTests.StreamedToolCallKeepsItsIdAndSurvivesWithoutOne(); return Task.CompletedTask; });
        yield return ("Sweep.ReasoningEcho", SevereBugSweepTests.ReasoningIsEchoedOnlyOnToolCallTurns);
        yield return ("Sweep.LoopCarriesReasoning", () => { SevereBugSweepTests.TheLoopCarriesReasoningIntoTheToolRound(); return Task.CompletedTask; });
        yield return ("Sweep.JevOptionAvailability", () => { SevereBugSweepTests.JevSkipsOptionsTheExecutorWouldReject(); return Task.CompletedTask; });
        yield return ("ProjectMap.EverySourceMapped", () => { ProjectMapFreshnessTests.EverySourceFileIsMapped(); return Task.CompletedTask; });
        yield return ("ProjectMap.NoDanglingPaths", () => { ProjectMapFreshnessTests.EveryMappedPathExists(); return Task.CompletedTask; });
        yield return ("AgentTurn.GameThreadPostsCarryToken", () => { AgentTurnGameThreadPostTests.EveryAgentBridgePostCarriesTheToken(); return Task.CompletedTask; });
        yield return ("Recovery.SuccessResets", AutoPlayRecoveryTests.SuccessfulActionResetsFailures);
        yield return ("Recovery.CancelBackoff", AutoPlayRecoveryTests.CancelDuringBackoffPreventsNextTurn);
        yield return ("Recovery.TimeoutNotCancel", AutoPlayRecoveryTests.TimeoutFailureDoesNotLookLikeUserCancel);
        yield return ("Recovery.ThinkingBudgetKeepsFailures", AutoPlayRecoveryTests.ThinkingBudgetExhaustionDoesNotSpendGenericFailureBudget);
        yield return ("Recovery.ThinkingBudgetLimit", AutoPlayRecoveryTests.ThinkingBudgetExhaustionStopsAtItsOwnLimit);
        yield return ("Recovery.ThinkingBudgetReset", AutoPlayRecoveryTests.SuccessfulActionClearsThinkingBudgetExhaustion);
        yield return ("Recovery.UnchangedActionStops", AutoPlayRecoveryTests.UnchangedActionStopsTheLoop);
        yield return ("Recovery.ProgressResetsRepeat", AutoPlayRecoveryTests.ProgressResetsTheRepeatRun);
        yield return ("Recovery.UnsettledBudget", AutoPlayRecoveryTests.UnsettledTurnsDoNotSpendTheRetryBudget);
        yield return ("Recovery.UnsettledLimit", AutoPlayRecoveryTests.UnsettledRunStopsAtItsLimit);
        yield return ("Recovery.UnsettledRunCleared", AutoPlayRecoveryTests.SettledTurnClearsTheUnsettledRun);
        yield return ("Recovery.UnsettledKeepsFailures", AutoPlayRecoveryTests.UnsettledTurnsDoNotEraseEarlierFailures);
        yield return ("Recovery.PendingReturnShape", () => Task.Run(AutoPlayRecoveryTests.PendingActReturnsWithoutAnError));
        yield return ("NoProgressPolicy.IsRepeatTruthTable", () => Task.Run(NoProgressPolicyTests.IsRepeatNeedsBothTheSameActionAndTheSameState));
        yield return ("NoProgressPolicy.Thresholds", () => Task.Run(NoProgressPolicyTests.ThresholdsAreNamedConstants));
        yield return ("NoProgressPolicy.Fingerprint", () => Task.Run(NoProgressPolicyTests.FingerprintOnlyTracksTheCompactStateText));
        yield return ("TeamControl.WaitForCommittedWork", AutoPlaySessionTests.PauseWaitsForCommittedWorkAndBlocksRestart);
        yield return ("TeamControl.CancelModel", AutoPlaySessionTests.PauseCancelsWaitingModel);
        yield return ("TeamControl.NoOverlappingLoops", AutoPlaySessionTests.ImmediatePauseNeverOverlapsGenerations);
        yield return ("TeamControl.Lifetime", () => Task.Run(AutoPlaySessionTests.CanceledLifetimeCannotStart));
        yield return ("TeamControl.NoLateAct", AgentLoopTests.PauseAfterModelResponseDoesNotDispatchAct);
        yield return ("AgentLoop.RunBoundaryRethrown", AgentLoopTests.PlayOnce_RethrowsRunBoundaryAfterAct);
        yield return ("AgentLoop.CheckStateOnGetGameState", AgentLoopTests.PlayOnce_InvokesCheckStateOnGetGameState);
        yield return ("AgentLoop.RequestBudgetStopsNextRound", AgentLoopTests.PlayOnce_StopsFurtherLlmCallsWhenRequestBudgetIsSpent);
        yield return ("AgentLoop.NonCombatYields", AgentLoopTests.NonCombatOnly_YieldsBeforeWaitModelOrAction);
        yield return ("AgentLoop.NonCombatAllowsRewards", AgentLoopTests.NonCombatOnly_AllowsNonCombatActions);
        yield return ("AgentLoop.NonCombatRaceGuard", AgentLoopTests.NonCombatOnly_RechecksBeforeDispatchAndKeepsWaiting);
        yield return ("TeamControl.TransportAck", TeamConversationTests.PauseControlHasExplicitAcknowledgement);
        yield return ("Session.LocalControlContract", () => Task.Run(SessionControlContractTests.RouterExposesLocalSessionControl));
        yield return ("Session.LocalMcpControl", () => Task.Run(SessionControlContractTests.RouterExposesLocalMcpControl));
        yield return ("Session.LocalScreenshot", () => Task.Run(SessionControlContractTests.RouterExposesLocalScreenshot));
        yield return ("CoopStartup.KeepLocalCandidate", () => Task.Run(SessionControlContractTests.WorkshopStagingKeepsLocalCandidate));
        yield return ("Diagnostics.RouteServesRedactedExport", () => Task.Run(SessionControlContractTests.DiagnosticsRouteServesTheRedactedExport));
        yield return ("TeamChat.ReadOnly", AgentLoopTests.TeamChat_CannotActEvenWithPlayIntent);
        yield return ("TeamChat.NextDecision", AgentLoopTests.TeamSuggestion_ReachesNextPlayDecision);
        yield return ("TeamChat.BoundedHistory", () => Task.Run(TeamConversationTests.HistoryIsBoundedAndCleared));
        yield return ("TeamChat.SessionAuthorization", () => Task.Run(TeamConversationTests.SessionTokensAreRequiredAndDistinct));
        yield return ("TeamChat.Transport", TeamConversationTests.TransportChecksIdentityAndSendsBoundedBody);
        yield return ("TeamChat.DegradedCompanion", TeamConversationTests.DegradedCompanionRemainsControllable);
        yield return ("TeamChat.ReusedPort", TeamConversationTests.ReusedPortDoesNotReceiveMessage);
        yield return ("CoopStartup.OfflineIsolation", () => Task.Run(CompanionStartupTests.OfflineLaunchKeepsAccountsIsolated));
        yield return ("CoopStartup.FourPlayerOccupancy", () => Task.Run(CompanionStartupTests.LocalJoinOccupiesOneSlotInFourPlayerLobby));
        yield return ("CoopStartup.OwnCharacterOnly", () => Task.Run(CompanionStartupTests.CompanionActionsTargetOnlyLocalCharacter));
        yield return ("CoopStartup.JoinBootstrap", () => Task.Run(CompanionStartupTests.CompanionBootstrapJoinsAsExtraPlayerThenReady));
        yield return ("CoopStartup.CombatFtueConfirm", () => Task.Run(CompanionStartupTests.CombatRulesFtueIsConfirmedImmediately));
        yield return ("Ftue.CombatRulesWithoutButton", () => Task.Run(FtueModalPolicyTests.CombatRulesFtueWithoutButtonIsConfirmable));
        yield return ("Ftue.MultiPageStaysOpen", () => Task.Run(FtueModalPolicyTests.MultiPageFtueKeepsTheModalOpen));
        yield return ("CoopStartup.FirstRunProvider", () => Task.Run(CompanionStartupTests.FirstRunProviderConfigIsReachable));
        yield return ("CoopStartup.ProfileMods", () => Task.Run(CompanionStartupTests.CompanionProfileEnablesTheMod));
        yield return ("CoopStartup.SettingsIsolation", () => Task.Run(CompanionStartupTests.SettingsPathCanBeIsolated));
        yield return ("CoopStartup.PortFile", () => Task.Run(CompanionStartupTests.CompanionPortFileRoundTrip));
        yield return ("CoopStartup.CompanionDoesNotHost", () => Task.Run(CompanionStartupTests.CompanionBootstrapDoesNotHostLobby));
        yield return ("CoopStartup.HumanChoiceHoldsClock", () => Task.Run(CompanionStartupTests.BootstrapHoldsTheClockOnlyWhileAHumanChooses));
        yield return ("CoopStartup.Identity", () => Task.Run(CompanionStartupTests.HealthRequiresExactCompanionIdentity));
        yield return ("CoopStartup.Preconditions", () => Task.Run(CompanionStartupTests.LaunchPreconditionsProtectHumanRun));
        yield return ("CoopRoute.ExternalTakeover", () => Task.Run(CoopRouteTests.UnverifiedPlayModelLaunchesForExternalTakeoverOnly));
        yield return ("CoopRoute.SupportedSurfaces", () => Task.Run(CoopRouteTests.CompanionRouteReachesTheSupportedSurfaces));
        yield return ("CoopRoute.SharedModelGate", () => Task.Run(CoopRouteTests.EveryStartEntryPointSharesTheModelGate));
        yield return ("CoopRoute.OverlayInviteRoute", () => Task.Run(CoopRouteTests.OverlayInviteFollowsTheApiRoute));
        yield return ("CoopRoute.OverlayEntries", () => Task.Run(CoopRouteTests.OverlayOffersContinueAndCharacterChoice));
        yield return ("CoopSave.ReadNetIds", () => Task.Run(CoopSavePrecheckTests.ReadsPlayerNetIdsFromTheSave));
        yield return ("CoopSave.NumericStringNetIds", () => Task.Run(CoopSavePrecheckTests.ReadsNumericStringNetIds));
        yield return ("CoopSave.UnreadableSaves", () => Task.Run(CoopSavePrecheckTests.UnreadableSavesYieldNoIds));
        yield return ("CoopSave.HostMismatch", () => Task.Run(CoopSavePrecheckTests.HostMismatchNamesBothIdsAndTheConsequence));
        yield return ("CoopSave.CompanionMismatch", () => Task.Run(CoopSavePrecheckTests.CompanionMismatchNamesBothIdsAndTheRejection));
        yield return ("CoopSave.FailOpenWhenUnreadable", () => Task.Run(CoopSavePrecheckTests.UnreadableIdsFailOpen));
        yield return ("SettingsStore.RoundTrip", () => Task.Run(SettingsStoreTests.RoundTrip_PreservesEndpointsModelsAndRoles));
        yield return ("ProactiveChat.LegacySettingsStayOff", () => Task.Run(SettingsStoreTests.ProactiveChat_LegacyFileLoadsDisabledWithDefaultTone));
        yield return ("ProactiveChat.StoredToneRepaired", () => Task.Run(SettingsStoreTests.ProactiveChat_UnknownStoredToneIsRepaired));
        yield return ("SettingsClone.CarriesEditableFields", () => Task.Run(SettingsStoreTests.Clone_CarriesEveryEditableField));
        yield return ("SettingsClone.IsDeep", () => Task.Run(SettingsStoreTests.Clone_IsDeep));
        yield return ("Settings.DualLayerFieldsNormalized", () => Task.Run(SettingsStoreTests.DualLayerFields_AreNormalized));
        yield return ("SettingsStore.FingerprintCase", () => Task.Run(SettingsStoreTests.Load_VerifiedPlayFingerprint_IsCaseInsensitive));
        yield return ("SettingsStore.MissingFile", () => Task.Run(SettingsStoreTests.Load_MissingFile_CreatesDefaults));
        yield return ("SettingsStore.MigrateThinking", () => Task.Run(SettingsStoreTests.Load_MigratesGlobalThinkingIntensityOntoModels));
        yield return ("SettingsStore.CorruptBackup", () => Task.Run(SettingsStoreTests.Load_CorruptJson_BacksUpOriginalAndDoesNotLoseSecret));
        yield return ("SettingsStore.CorruptRestore", () => Task.Run(SettingsStoreTests.Load_CorruptJson_RestoresLastGoodBackup));
        yield return ("SettingsStore.UnwritableTemp", () => Task.Run(SettingsStoreTests.Save_UnwritableTemp_LeavesOriginalIntact));
        yield return ("SettingsStore.NoticeNoSecret", () => Task.Run(SettingsStoreTests.Load_CorruptJson_NoticeDoesNotContainFileBody));
        yield return ("Settings.BudgetIllegalKeepsSafe", () => Task.Run(PlayerExperienceTests.IllegalBudgetInputKeepsSafeValue));
        yield return ("Settings.BudgetEmptyUnlimited", () => Task.Run(PlayerExperienceTests.EmptyOrZeroBudgetMeansUnlimited));
        yield return ("Settings.BudgetHarvestBeforeMutate", () => Task.Run(PlayerExperienceTests.HarvestBudgetRejectsBeforeMutatingSettings));
        yield return ("Session.BudgetCopyReachableReset", () => Task.Run(PlayerExperienceTests.BudgetCopyNamesReachableResetEntry));
        yield return ("Session.ResetStatsBlockedRunning", () => Task.Run(PlayerExperienceTests.ResetStatsBlockedWhileRunning));
        yield return ("Thinking.gpt-4o", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("gpt-4o", "auto", "prompt")));
        yield return ("Thinking.gpt-5", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("gpt-5", "auto", "reasoning_effort")));
        yield return ("Thinking.step-5", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("step-5-preview", "auto", "reasoning_effort")));
        yield return ("Thinking.o3-mini", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("o3-mini", "auto", "reasoning_effort")));
        yield return ("Thinking.deepseek", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("deepseek-chat", "auto", "deepseek")));
        yield return ("Thinking.explicit", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("anything", "reasoning_effort", "reasoning_effort")));
        yield return ("Thinking.off", () => Task.Run(ThinkingRequestBuilderTests.Off_DisablesDeepSeekThinking));
        yield return ("OpenAI.ResolveUrl", () => Task.Run(OpenAiCompatibleClientTests.ResolveCompletionsUrl_NormalizesBase));
        yield return ("OpenAI.ParseCompletion", () => Task.Run(OpenAiCompatibleClientTests.ParseCompletion_ReadsToolCallsAndReasoning));
        yield return ("OpenAI.PostBody", OpenAiCompatibleClientTests.CompleteAsync_PostsOpenAiCompatibleBody);
        yield return ("OpenAI.DeepSeekExtraBody", OpenAiCompatibleClientTests.CompleteAsync_PostsDeepSeekThinkingInExtraBody);
        yield return ("OpenAI.VisionDataUrl", OpenAiCompatibleClientTests.CompleteAsync_AttachesImageAsDataUrlContentParts);
        yield return ("OpenAI.VisionPlainContent", OpenAiCompatibleClientTests.CompleteAsync_PlainContentStaysStringWithoutImage);
        yield return ("OpenAI.ParseSse", () => Task.Run(OpenAiCompatibleClientTests.ParseSse_AccumulatesContentAndToolCalls));
        yield return ("OpenAI.ParseCompletionUsage", () => Task.Run(OpenAiCompatibleClientTests.ParseCompletion_ReadsUsage));
        yield return ("OpenAI.ParseSseUsage", () => Task.Run(OpenAiCompatibleClientTests.ParseSse_ReadsUsageFromEndChunk));
        yield return ("OpenAI.LlmUsageMath", () => Task.Run(OpenAiCompatibleClientTests.LlmUsage_CombineAndAdd));
        yield return ("OpenAI.StalledBodyTimeout", OpenAiCompatibleClientTests.CompleteAsync_HeadersThenStalledBodyTimesOut);
        yield return ("OpenAI.StalledBodyUserCancel", OpenAiCompatibleClientTests.CompleteAsync_HeadersThenStalledBodyUserCancel);
        yield return ("OpenAI.MaxTokensFieldDetection", () => Task.Run(OpenAiCompatibleClientTests.MaxTokensField_DetectsOnlyTheUnsupportedParameterError));
        yield return ("OpenAI.MaxTokensFieldRename", () => Task.Run(OpenAiCompatibleClientTests.MaxTokensField_RenamesOnlyWhenThereIsAValueToMove));
        yield return ("OpenAI.PingCompletionTokensRetry", OpenAiCompatibleClientTests.Ping_RetriesWithCompletionTokensWhenTheEndpointRefusesMaxTokens);
        yield return ("OpenAI.PingUnrelated400NotRetried", OpenAiCompatibleClientTests.Ping_DoesNotRetryA400ThatIsNotAboutTheParameter);
        yield return ("OpenAI.ParseCompletionFinishReason", () => Task.Run(OpenAiCompatibleClientTests.ParseCompletion_ReadsFinishReason));
        yield return ("OpenAI.ParseSseFinishReason", () => Task.Run(OpenAiCompatibleClientTests.ParseSsePayload_ReadsFinishReasonFromMessageChoice));
        yield return ("OpenAI.ParseSseDeltaFinishReason", () => Task.Run(OpenAiCompatibleClientTests.ParseSsePayload_ReadsFinishReasonFromDeltaChoice));
        yield return ("OpenAI.ReasoningStreamsBeforeCompletion", OpenAiCompatibleClientTests.CompleteAsync_StreamsReasoningBeforeCompletion);
        yield return ("OpenAI.StreamFlagIgnoredStillParses", OpenAiCompatibleClientTests.CompleteAsync_StreamFlagIgnoredStillParsesJson);
        // The dual-layer engine: the Jev transport and the pure decision logic behind it.
        yield return ("Jev.PostsRequestShape", JevClientTests.SystemOneAsync_PostsStateModelAndQuestionsToSystemOne);
        yield return ("Jev.ParsesChoiceAnswer", JevClientTests.SystemOneAsync_ParsesChoiceAnswerWithProbabilitiesAndConfidence);
        yield return ("Jev.ParsesFractionalScore", JevClientTests.SystemOneAsync_ParsesFractionalScoreAnswerWithLegend);
        yield return ("Jev.RetriesAfter429", JevClientTests.SystemOneAsync_RetriesAfter429ThenSucceeds);
        yield return ("Jev.RateLimitSurfaces", JevClientTests.SystemOneAsync_RateLimitAfterRetriesSurfacesAsRateLimited);
        yield return ("Jev.CallerCancellation", JevClientTests.SystemOneAsync_CallerCancellationSurfacesAsOperationCanceled);
        yield return ("Jev.UnauthorizedIsConfig", JevClientTests.SystemOneAsync_UnauthorizedMapsToConfigError);
        yield return ("Jev.ServerFailure", JevClientTests.SystemOneAsync_ServerFailureMapsToServerError);
        yield return ("Jev.AnswerlessIsServerError", JevClientTests.SystemOneAsync_AnswerlessBodyIsAServerError);
        yield return ("Jev.PingReportsModelCount", JevClientTests.PingAsync_ReportsTheModelCount);
        yield return ("Jev.PingReportsFailure", JevClientTests.PingAsync_ReportsTheFailureInsteadOfThrowing);
        yield return ("Jev.BlankConfigRejected", () => Task.Run(JevClientTests.BlankConfigurationIsRejectedAtConstruction));
        yield return ("NonCombat.DetectsSnapshots", () => Task.Run(NonCombatOnlyPolicyTests.DetectsCompactAndActionSnapshotCombat));
        yield return ("NonCombat.UnknownFailsClosed", () => Task.Run(NonCombatOnlyPolicyTests.UnknownStateFailsClosed));
        yield return ("Jev.PostsAlignedChoiceQuestion", JevClientTests.SystemOneAsync_PostsTheAlignedChoiceQuestionTheDeciderBuilds);
        yield return ("JevLogic.StrategyRoundTrips", () => Task.Run(JevAgentLogicTests.PlayStrategy_RoundTripsEveryField));
        yield return ("JevLogic.StrategyParseTolerates", () => Task.Run(JevAgentLogicTests.PlayStrategy_TryParseToleratesMissingFields));
        yield return ("JevLogic.StrategyDefault", () => Task.Run(JevAgentLogicTests.PlayStrategy_DefaultIsBalancedAndMarksItsSource));
        yield return ("JevLogic.StoreStartsAtDefault", () => Task.Run(JevAgentLogicTests.StrategyStore_StartsAtDefault));
        yield return ("JevLogic.StoreConcurrentUpdate", () => Task.Run(JevAgentLogicTests.StrategyStore_UpdateIsVisibleUnderConcurrency));
        yield return ("JevLogic.DeciderReturnsDecision", () => Task.Run(JevAgentLogicTests.ActionDecider_ReturnsItsDecision));
        yield return ("JevLogic.ActArgumentsShape", () => Task.Run(JevAgentLogicTests.ExecutionDecision_ToActArgumentsJsonMatchesActShape));
        yield return ("JevLogic.OptionIndexCarried", () => Task.Run(JevAgentLogicTests.ExecutionDecision_OptionIndexDecisionCarriesItsIndex));
        yield return ("JevLogic.EmptyDecisionSerializes", () => Task.Run(JevAgentLogicTests.ExecutionDecision_EmptyDecisionSerializesToEmptyObject));
        yield return ("JevLogic.EnumerateCombat", () => Task.Run(JevAgentLogicTests.Enumerate_CombatCardsTimesTargetsAndEndTurn));
        yield return ("JevLogic.EnumerateMapNodes", () => Task.Run(JevAgentLogicTests.Enumerate_MapNodeOptions));
        yield return ("JevLogic.EnumerateSkipsLocks", () => Task.Run(JevAgentLogicTests.Enumerate_IndexedOptionsExpandTargetsAndSkipLocks));
        yield return ("JevLogic.EnumerateNoArgFallback", () => Task.Run(JevAgentLogicTests.Enumerate_NoArgFallbackScreenYieldsSingleOptions));
        yield return ("JevLogic.EnumerateRewardCards", () => Task.Run(JevAgentLogicTests.Enumerate_RewardCardChoiceExpandsResolveRewards));
        yield return ("JevLogic.EnumerateRewardNoCards", () => Task.Run(JevAgentLogicTests.Enumerate_RewardWithoutCardsKeepsBareMacro));
        yield return ("JevLogic.EnumerateCapsAt255", () => Task.Run(JevAgentLogicTests.Enumerate_CapsAt255WithCardPlaysFirst));
        yield return ("JevLogic.EnumerateMalformedSafe", () => Task.Run(JevAgentLogicTests.Enumerate_MalformedInputDoesNotThrow));
        foreach (var test in JevStrategyAlignmentTests.All()) yield return test;
        yield return ("StrategyUpdate.OmittedFieldsKept", () => Task.Run(PlayStrategyUpdateTests.OmittedFieldsKeepTheirCurrentValues));
        yield return ("StrategyUpdate.GoalWritable", () => Task.Run(PlayStrategyUpdateTests.AnExternalPlannerCanSetTheMacroGoal));
        yield return ("StrategyUpdate.GoalClamped", () => Task.Run(PlayStrategyUpdateTests.AnOverlongGoalIsClamped));
        yield return ("StrategyUpdate.EmptyRejected", () => Task.Run(PlayStrategyUpdateTests.AnUpdateWithNoRecognisedFieldIsRejected));
        yield return ("StrategyUpdate.HintsReplaced", () => Task.Run(PlayStrategyUpdateTests.ExplicitHintsReplaceTheMap));
        yield return ("StrategyUpdate.SharedMerge", () => Task.Run(PlayStrategyUpdateTests.BothWritersUseTheSharedMerge));
        yield return ("StrategyUpdate.ScopeSurvivesTruncation", () => Task.Run(PlayStrategyUpdateTests.PlanScopeSurvivesTheTruncatedPlannerSummary));
        yield return ("StrategyUpdate.AtomicMerge", () => Task.Run(PlayStrategyUpdateTests.AnExternalUpdateMergesAtomicallyInTheStore));
        // Per-run play-session persistence.
        yield return ("Session.RoundTrips", () => Task.Run(PlaySessionStoreTests.SaveThenLoadRoundTripsEveryField));
        yield return ("Session.LoadMissingIsNull", () => Task.Run(PlaySessionStoreTests.LoadReturnsNullWhenNoSessionExists));
        yield return ("Session.RunUnknownNotPersisted", () => Task.Run(PlaySessionStoreTests.RunUnknownIsNeverPersisted));
        yield return ("Session.IsolatedByRunId", () => Task.Run(PlaySessionStoreTests.SessionsAreIsolatedByRunId));
        yield return ("Session.CorruptRestoresBackup", () => Task.Run(PlaySessionStoreTests.CorruptFileIsMovedAsideAndBackupRestored));
        yield return ("Session.CorruptNoBackupIsNull", () => Task.Run(PlaySessionStoreTests.CorruptFileWithoutBackupYieldsNullNotThrow));
        yield return ("Session.ChatTrimmedToCap", () => Task.Run(PlaySessionStoreTests.ChatIsTrimmedToTheCapKeepingNewest));
        yield return ("Session.RedactsSecrets", () => Task.Run(PlaySessionStoreTests.SecretsInChatTextAreRedactedBeforeDisk));
        yield return ("Session.RedactsStrategyGoalAndScope", () => Task.Run(PlaySessionStoreTests.NewStrategyFieldsAreRedactedBeforeDisk));
        yield return ("Session.Delete", () => Task.Run(PlaySessionStoreTests.DeleteRemovesTheSession));
        yield return ("Session.RunIdPathSafe", () => Task.Run(PlaySessionStoreTests.RunIdWithPathSeparatorsCannotEscapeTheDirectory));
        yield return ("Budget.NoLimit", () => Task.Run(SessionBudgetGuardTests.NoLimit_NeverStops));
        yield return ("Budget.MaxTokens", () => Task.Run(SessionBudgetGuardTests.MaxTokens_StopsWhenExceeded));
        yield return ("Budget.MaxRequests", () => Task.Run(SessionBudgetGuardTests.MaxRequests_StopsEvenWithoutUsage));
        yield return ("Budget.InFlightRequests", () => Task.Run(SessionBudgetGuardTests.CheckBudget_CountsInFlightRequests));
        yield return ("Budget.RecoveryStops", SessionBudgetGuardTests.Recovery_AutoPlayStopsOnBudgetExceeded);
        yield return ("Budget.ResumePreservesCumulativeUsage", () => Task.Run(SessionBudgetGuardTests.InitialCounters_ResumePreservesCumulativeUsage));
        yield return ("Budget.ExceededRequestsStopsImmediately", SessionBudgetGuardTests.InitialCounters_AlreadyExceeded_RunAsyncStopsImmediately);
        yield return ("Budget.ExceededTokensStopsImmediately", SessionBudgetGuardTests.InitialTokens_AlreadyExceeded_RunAsyncStopsImmediately);
        yield return ("Budget.SettingsCarriesInitialCounters", () => Task.Run(SessionBudgetGuardTests.Settings_CreateBudgetGuard_CarriesInitialCounters));
        yield return ("Budget.UpdateLimitsLoweringStops", () => Task.Run(SessionBudgetGuardTests.UpdateLimits_LoweringMaxRequests_CheckBudgetFailsWhileTotalsStay));
        yield return ("Budget.UpdateLimitsRaisingContinues", () => Task.Run(SessionBudgetGuardTests.UpdateLimits_RaisingMaxRequests_AllowsFurtherRecordWithoutStaleStop));
        yield return ("Budget.UpdateLimitsClearingRemovesAxis", () => Task.Run(SessionBudgetGuardTests.UpdateLimits_ClearingLimit_RemovesHasLimitForThatAxis));
        yield return ("Budget.UpdateLimitsLockSafe", () => Task.Run(SessionBudgetGuardTests.UpdateLimits_LockSafeWithConcurrentRecord));
        yield return ("Budget.RuntimeSaveReloadKeepsGuard", () => Task.Run(SessionBudgetGuardTests.Runtime_SaveAndReload_UpdateLimitsInPlaceWithoutReplacingGuard));
        yield return ("Budget.ZeroRequestImmediateAct", () => Task.Run(SessionBudgetGuardTests.Observe_ZeroRequestsSpent_DoesNotInventARequest));
        yield return ("Budget.WaitingStillZero", () => Task.Run(SessionBudgetGuardTests.Observe_WaitingForGame_StillRecordsZero));
        yield return ("Budget.NegativeRequestsClamp", () => Task.Run(SessionBudgetGuardTests.Observe_NegativeRequestsSpent_ClampsToZero));
        yield return ("Budget.MultiRoundExactCount", () => Task.Run(SessionBudgetGuardTests.Observe_MultiRoundRequestsSpent_RecordsExactCount));
        yield return ("Budget.ImmediateActsDoNotHitCap", SessionBudgetGuardTests.Recovery_ImmediateZeroRequestActs_DoNotHitRequestCap);
        yield return ("GameData.DetectScene", () => Task.Run(GameDataFilterTests.DetectScene_MatchesGuidedMcpRules));
        yield return ("GameData.ProjectRelevant", () => Task.Run(GameDataFilterTests.ProjectRelevant_KeepsCombatCardFields));
        yield return ("GameDataFilter.CombatHandIds", () => Task.Run(GameDataFilterItemSourceTests.CombatHandAndEnemyIdsFollowTheSurfaceOrder));
        yield return ("GameDataFilter.CombatPowers", () => Task.Run(GameDataFilterItemSourceTests.CombatPowersMergePlayerPowersThenEnemyPowers));
        yield return ("GameDataFilter.NullPotionId", () => Task.Run(GameDataFilterItemSourceTests.NullPotionIdYieldsNoIdsInsteadOfAnError));
        yield return ("GameDataFilter.NonCombatDeckFallback", () => Task.Run(GameDataFilterItemSourceTests.NonCombatScreenFallsBackToTheDeck));
        yield return ("GameDataFilter.UnknownCollection", () => Task.Run(GameDataFilterItemSourceTests.UnknownCollectionYieldsNoIds));
        yield return ("GameDataFilter.DuplicateIds", () => Task.Run(GameDataFilterItemSourceTests.DuplicateIdsAppearOnceInFirstSeenOrder));
        yield return ("GameDataFilter.CaseInsensitive", () => Task.Run(GameDataFilterItemSourceTests.CollectionAndScreenMatchingIsCaseInsensitive));
        yield return ("GameDataFilter.EmptyStringId", () => Task.Run(GameDataFilterItemSourceTests.EmptyStringIdsAreSkipped));
        yield return ("GameDataFilter.CombatRelicFallback", () => Task.Run(GameDataFilterItemSourceTests.CombatRelicsFallBackToTheRunRelics));
        yield return ("GameDataFilter.EmptySceneFallsBack", () => Task.Run(GameDataFilterItemSourceTests.SceneSourceWithoutItsPayloadFallsBack));
        yield return ("GameDataFilter.RewardOffersCards", () => Task.Run(GameDataFilterItemSourceTests.RewardScreenLooksUpTheOfferedCards));
        yield return ("GameDataFilter.RewardCompactCards", () => Task.Run(GameDataFilterItemSourceTests.RewardCompactViewAnswersWithItsOwnOfferedCards));
        yield return ("GameDataFilter.RewardRelicFallback", () => Task.Run(GameDataFilterItemSourceTests.RewardRelicLookupFallsBackWithoutCardIds));
        yield return ("GameDataFilter.RewardPotionFallback", () => Task.Run(GameDataFilterItemSourceTests.RewardPotionLookupFallsBackInsteadOfInventingAnId));
        yield return ("GameDataFilter.SelectionOffersCards", () => Task.Run(GameDataFilterItemSourceTests.CardSelectionGridOffersItsOwnCardsOnceInOrder));
        yield return ("GameDataFilter.SelectionRelicAware", () => Task.Run(GameDataFilterItemSourceTests.CardSelectionRelicLookupNeverAnswersWithOfferedCards));
        yield return ("GameDataFilter.ChestOffersRelics", () => Task.Run(GameDataFilterItemSourceTests.ChestOffersItsRelicOptions));
        yield return ("GameDataFilter.ChestCompactRelics", () => Task.Run(GameDataFilterItemSourceTests.ChestCompactViewAnswersWithItsOwnRelicOffers));
        yield return ("GameDataFilter.ChestCardFallback", () => Task.Run(GameDataFilterItemSourceTests.ChestCardLookupFallsBackToTheDeck));
        yield return ("GameDataFilter.BundleOffersCards", () => Task.Run(GameDataFilterItemSourceTests.BundleScreenOffersEveryCardsInItsBundles));
        yield return ("GameDataFilter.ShopOfferIds", () => Task.Run(GameDataFilterItemSourceTests.ShopOffersStockIdsForCardsRelicsAndPotions));
        yield return ("GameDataFilter.CollectionCaseInsensitive", () => Task.Run(GameDataFilterItemSourceTests.OfferScreensMatchTheCollectionNameCaseInsensitively));
        yield return ("GameData.OfferCardProjection", () => Task.Run(GameDataFilterTests.ProjectRelevant_KeepsOfferCardFieldsOnReward));
        yield return ("PlayIntent.Detect", () => Task.Run(PlayIntentTests.DetectsPlayPhrasesAndIgnoresQuestions));
        yield return ("ActIndex.Validate", () => Task.Run(ActIndexValidatorTests.RejectsMissingAndStaleIndexes));
        yield return ("ActIndex.Structured", () => Task.Run(ActIndexValidatorTests.StructuredIndexRejections));
        yield return ("ActIndex.Unsettled", () => Task.Run(ActIndexValidatorTests.DetectsUnsettledActResults));
        yield return ("ActIndex.UnsettledNonObject", () => Task.Run(ActIndexValidatorTests.NonObjectActResultReadsAsSettled));
        yield return ("Reflection.PrivateBaseField", () => Task.Run(ReflectionMemberAccessorTests.ReadsPrivateBaseFieldFromDerivedInstance));
        yield return ("Reflection.PrivateBaseProperty", () => Task.Run(ReflectionMemberAccessorTests.ReadsPrivateBasePropertyFromDerivedInstance));
        yield return ("Reflection.DerivedPrecedence", () => Task.Run(ReflectionMemberAccessorTests.PrefersDerivedMemberWithSameName));
        yield return ("Reflection.ThrowingDerived", () => Task.Run(ReflectionMemberAccessorTests.DoesNotFallBackWhenDerivedGetterThrows));
        yield return ("UnlockConfirm.Reflected", () => Task.Run(UnlockConfirmResolutionPolicyTests.PrefersUsableReflectedCandidate));
        yield return ("UnlockConfirm.Fallback", () => Task.Run(UnlockConfirmResolutionPolicyTests.SkipsUnusableCandidatesBeforeUsableFallback));
        yield return ("UnlockConfirm.Session", () => Task.Run(UnlockConfirmResolutionPolicyTests.ProbeSignatureIncludesScreenInstance));
        yield return ("UnlockScreen.MixedCardGrid", () => Task.Run(UnlockScreenContractTests.UnlockCardsScreenWithVisibleGridReportsOnlyUnlockAction));
        yield return ("GameOver.ContinueAction", () => Task.Run(GameOverContractTests.DedicatedContinueActionIsWiredEndToEnd));
        yield return ("Reward.EmptyScreenEscape", () => Task.Run(RewardFlowContractTests.EmptyRewardsScreenEscapesInsteadOfPending));
        yield return ("Skill.McpPlayerContract", () => Task.Run(McpPlayerSkillTests.SkillTracksLivePlayContract));
        yield return ("GameOver.ReturnGate", () => Task.Run(GameOverContractTests.ReturnActionRequiresVisibleAndEnabledMainMenuButton));
        yield return ("GameOver.NativeButtons", () => Task.Run(GameOverContractTests.ContinueAndReturnUseNativeButtonsWithoutSkippingSummary));
        yield return ("GameOver.SummaryReady", () => Task.Run(GameOverContractTests.ContinueWaitsForNativeSummaryReadiness));
        yield return ("GameOver.NoForcedReturn", () => Task.Run(GameOverContractTests.ContinueDoesNotForceEnableReturnBeforeNativeSave));
        yield return ("GameOver.Phases", () => Task.Run(GameOverContractTests.GameOverPayloadKeepsContinueSummaryAndReturnAsDistinctPhases));
        yield return ("GameOver.SaveContract", () => Task.Run(GameOverContractTests.GameOverPayloadReportsPhysicalProgressSaveVerification));
        yield return ("GameOver.SaveVerified", () => Task.Run(ProgressSaveVerificationTests.MatchingPhysicalFileIsVerified));
        yield return ("GameOver.SaveEquivalentJson", () => Task.Run(ProgressSaveVerificationTests.EquivalentJsonWithDifferentFormattingAndPropertyOrderIsVerified));
        yield return ("GameOver.SaveEquivalentNumbers", () => Task.Run(ProgressSaveVerificationTests.EquivalentNumericRepresentationsAreVerified));
        yield return ("GameOver.SavePersistedJson", () => Task.Run(ProgressSaveVerificationTests.MatchingPersistedJsonIsVerifiedWithoutPhysicalReopen));
        yield return ("GameOver.SaveMismatch", () => Task.Run(ProgressSaveVerificationTests.MismatchedScoreOrUnlockStateCannotReportSuccess));
        yield return ("GameOver.SaveMissingMalformed", () => Task.Run(ProgressSaveVerificationTests.MissingOrMalformedFileCannotReportSuccess));
        yield return ("GameOver.SaveReadFailure", () => Task.Run(ProgressSaveVerificationTests.ReadFailureCannotReportSuccess));
        yield return ("CardGridSelection.PayloadProgress", () => Task.Run(DeckSelectionContractTests.CardGridPayloadReportsNativeSelectionProgress));
        yield return ("CardGridSelection.ClickSettle", () => Task.Run(DeckSelectionContractTests.CardGridClickSettlesInEitherDirectionBeforeConfirming));
        yield return ("CardGridSelection.ConfirmDispatch", () => Task.Run(DeckSelectionContractTests.CardGridConfirmationUsesSharedExecutor));
        yield return ("CombatDiagnostics.CanPlay", () => Task.Run(CombatDiagnosticsContractTests.HandPayloadKeepsNativeCanPlayEvidence));
        yield return ("Map.NoVoteDuringCombat", () => Task.Run(MapCombatGatingTests.ChooseMapNodeHiddenWhileCombatInProgress));
        yield return ("CombatDiagnostics.Readiness", () => Task.Run(CombatDiagnosticsContractTests.CombatPayloadDistinguishesQueueModalAndSnapshotLocks));
        yield return ("CombatReadiness.RejectsPreTurn", () => Task.Run(CombatTurnReadinessPolicyTests.RejectsPreTurnEmptyHandEvenWhenButtonLooksReady));
        yield return ("CombatReadiness.KeepsOpeningGuard", () => Task.Run(CombatTurnReadinessPolicyTests.KeepsOpeningDrawGuardWhenNoNativeReadyEvidenceExists));
        yield return ("CombatReadiness.AcceptsTurnEvidence", () => Task.Run(CombatTurnReadinessPolicyTests.AcceptsCardsOrRecordedPlayAsTurnEvidence));
        yield return ("CombatReadiness.RecoversNativeEndTurn", () => Task.Run(CombatTurnReadinessPolicyTests.RecoversEmptyHandWhenNativeEndTurnIsReady));
        yield return ("CombatDiagnostics.CancelPlayCard", () => Task.Run(CombatDiagnosticsContractTests.PlayCardTimeoutCancelsNativeGameAction));
        yield return ("CombatDiagnostics.OwnPets", () => Task.Run(CombatDiagnosticsContractTests.CombatPayloadExposesOwnPets));
        yield return ("CombatDiagnostics.DamageOverTimeRisks", () => Task.Run(CombatDiagnosticsContractTests.LethalRisksIncludeDamageOverTime));
        yield return ("ProfileSelection.NativeSwitch", () => Task.Run(ProfileSelectionContractTests.NativeProfileIdentityAndSwitchAreWiredEndToEnd));
        yield return ("DecisionLog.BoundsAndRedacts", () => Task.Run(DecisionLogTests.Record_RedactsBoundsAndKeepsNewest));
        yield return ("DecisionLog.PersistsAndRotates", () => Task.Run(DecisionLogTests.Record_PersistsJsonlAndRotates));
        yield return ("DecisionLog.PersistenceFailureIsSafe", () => Task.Run(DecisionLogTests.Record_UnwritablePathNeverThrows));
        yield return ("DecisionLog.NotifiesMirrorsSafely", () => Task.Run(DecisionLogTests.Record_NotifiesMirrorsAfterCommitting));
        yield return ("DecisionLog.RunAttribution", () => Task.Run(DecisionLogTests.Record_AttributesDecisionsToTheirRun));
        yield return ("DecisionLog.UnknownSpendStaysUnknown", () => Task.Run(DecisionLogTests.Spend_KeepsUnknownTokensUnknown));
        yield return ("DecisionLog.SpendMatchesSnapshot", () => Task.Run(DecisionLogTests.Spend_TracksTheEntriesTheSnapshotCanShow));
        yield return ("PlayerExperience.RunSpendLine", () => Task.Run(PlayerExperienceTests.RunSpendLineStaysHonestAboutUnknowns));
        yield return ("Events.DecisionMadeMirror", () => Task.Run(DecisionLogTests.SseDecisionEvent_IsMirroredFromTheSameLog));
        yield return ("DecisionLog.ClientContextReason", () => Task.Run(DecisionContextTests.ClientContext_ReasonIsOptionalAndTrimmed));
        yield return ("Mcp.DecisionLogTool", McpServiceTests.ToolsCall_DecisionLogRecordsAcceptedActOnly);
        yield return ("Mcp.DecisionLogAbsentIsEmpty", McpServiceTests.NativeServerWithoutDecisionLog_StaysSilent);
        yield return ("TurnIntegrity.AcceptedActionSurvivesObservationFailure", AgentTurnIntegrityTests.AcceptedActionSurvivesObservationFailure);
        yield return ("TurnIntegrity.ChatDoesNotReplayAcceptedAction", AgentTurnIntegrityTests.ChatDoesNotReplayAcceptedAction);
        yield return ("TurnIntegrity.InTurnTokensStopTheNextRequest", AgentTurnIntegrityTests.InTurnTokensStopTheNextRequest);
        yield return ("TurnIntegrity.VisionTokensStopThePrimaryRequest", AgentTurnIntegrityTests.VisionTokensStopThePrimaryRequest);
        yield return ("TurnIntegrity.CanceledCompletedTurnIsRecorded", AgentTurnIntegrityTests.CanceledCompletedTurnIsRecorded);
        yield return ("TurnIntegrity.CancellationAfterAcceptedActionKeepsReceipt", AgentTurnIntegrityTests.CancellationAfterAcceptedActionKeepsReceipt);
        yield return ("TurnIntegrity.CanceledLaterModelRequestKeepsEarlierUsage", AgentTurnIntegrityTests.CanceledLaterModelRequestKeepsEarlierUsage);
        yield return ("TurnIntegrity.NonObjectActArgumentsAreRecoverable", AgentTurnIntegrityTests.NonObjectActArgumentsAreRecoverable);
        yield return ("TurnIntegrity.NormalTurnStillActsOnce", AgentTurnIntegrityTests.NormalTurnStillActsOnce);
        yield return ("TurnIntegrity.ProactiveOrder", () => Task.Run(AgentTurnIntegrityTests.ProactiveWorkRunsAfterRecoveryAccounting));
        yield return ("TurnIntegrity.ReadToolCancellationPreservesPriorModelUsage", AgentTurnIntegrityTests.ReadToolCancellationPreservesPriorModelUsage);
        yield return ("TurnIntegrity.ProactiveCancellationKeepsPlayAndChatReceiptsOnce", AgentTurnIntegrityTests.ProactiveCancellationKeepsPlayAndChatReceiptsOnce);
        yield return ("TurnIntegrity.ExplicitRejectionStillAllowsACorrectedAction", AgentTurnIntegrityTests.ExplicitRejectionStillAllowsACorrectedAction);
        yield return ("TurnIntegrity.WaitingTurnSeesUsageBeforeGateRelease", AgentTurnIntegrityTests.WaitingTurnSeesUsageBeforeGateRelease);
        yield return ("TurnIntegrity.InterruptedReceiptIsCommittedBeforeGateRelease", AgentTurnIntegrityTests.InterruptedReceiptIsCommittedBeforeGateRelease);
        yield return ("TurnIntegrity.RuntimeCommitsBeforeReleasingTheTurnGate", () => Task.Run(AgentTurnIntegrityTests.RuntimeCommitsBeforeReleasingTheTurnGate));
        yield return ("TurnIntegrity.ProactiveCannotStartAfterPlayHitsRequestCap", AgentTurnIntegrityTests.ProactiveCannotStartAfterPlayHitsRequestCap);
        yield return ("TurnIntegrity.ProactiveSeesCommittedPlayAndCannotDoubleCharge", AgentTurnIntegrityTests.ProactiveSeesCommittedPlayAndCannotDoubleCharge);
        yield return ("TurnIntegrity.InterruptedCallbackDoesNotPublishLiveUi", AgentTurnIntegrityTests.InterruptedCallbackDoesNotPublishLiveUi);
        yield return ("TurnIntegrity.CancellationBeforeFirstRequestSpendsNothing", AgentTurnIntegrityTests.CancellationBeforeFirstRequestSpendsNothing);
        yield return ("TurnIntegrity.CanceledVisionRetainsRequestWithUnknownUsage", AgentTurnIntegrityTests.CanceledVisionRetainsRequestWithUnknownUsage);
        yield return ("TurnIntegrity.RunBoundaryAfterActionRetainsReceipt", AgentTurnIntegrityTests.RunBoundaryAfterActionRetainsReceipt);
        yield return ("TurnIntegrity.JsonFallbackAlsoKeepsAcceptedAction", AgentTurnIntegrityTests.JsonFallbackAlsoKeepsAcceptedAction);
        yield return ("TurnIntegrity.MalformedArgumentsReturnStructuredErrorWithinBudget", AgentTurnIntegrityTests.MalformedArgumentsReturnStructuredErrorWithinBudget);
        yield return ("TurnIntegrity.PendingBudgetCheckDoesNotMutateCounters", () => Task.Run(AgentTurnIntegrityTests.PendingBudgetCheckDoesNotMutateCounters));
        yield return ("TurnIntegrity.RuntimeConsumersPreserveInterruptedReceipts", () => Task.Run(AgentTurnIntegrityTests.RuntimeConsumersPreserveInterruptedReceipts));
        yield return ("TurnIntegrity.IdleChatHandlesNonCombatYield", () => Task.Run(AgentTurnIntegrityTests.IdleChatHandlesNonCombatYieldGracefully));
        yield return ("DevAudit.ModelProbeHonorsPreCancellation", AgentLoopTests.ModelProbeHonorsPreCancellation);
        yield return ("DevAudit.ModelProbePropagatesInFlightCancellation", AgentLoopTests.ModelProbePropagatesInFlightCancellation);
        yield return ("DevAudit.ModelProbeStillReportsProviderFailure", AgentLoopTests.ModelProbeStillReportsProviderFailure);
        yield return ("CompletionErrors.ThinkingModelSilence", () => Task.Run(AgentLoopTests.CompletionErrors_ExplainsThinkingModelSilence));
        yield return ("DevAudit.DiffExactCapIsComplete", () => Task.Run(DevAuditRegressionTests.DiffExactCapIsComplete));
        yield return ("DevAudit.DiffDeepComparisonReportsItsLimit", () => Task.Run(DevAuditRegressionTests.DiffDeepComparisonReportsItsLimit));
        yield return ("DevAudit.DiffKeepsIntegerPrecision", () => Task.Run(DevAuditRegressionTests.DiffKeepsIntegerPrecision));
        yield return ("DevAudit.DiffEquivalentNumbersAreEqual", () => Task.Run(DevAuditRegressionTests.DiffEquivalentNumbersAreEqual));
        yield return ("DevAudit.DiffLiteralKeysDoNotCollide", () => Task.Run(DevAuditRegressionTests.DiffLiteralKeysDoNotCollide));
        yield return ("DevAudit.DiffUnicodePathsMatchPython", () => Task.Run(DevAuditRegressionTests.DiffUnicodePathsMatchPython));
        yield return ("DevAudit.DiffEmptyObjectIsNotString", () => Task.Run(DevAuditRegressionTests.DiffEmptyObjectIsNotString));
        yield return ("DevAudit.EventOverflowDropsSnapshot", () => Task.Run(DevAuditRegressionTests.EventOverflowDropsSnapshot));
        yield return ("DevAudit.EventSnapshotOverflowDropsSnapshot", () => Task.Run(DevAuditRegressionTests.EventSnapshotOverflowDropsSnapshot));
        yield return ("DevAudit.EventInFlightSampleCannotReviveIdleState", () => Task.Run(DevAuditRegressionTests.EventInFlightSampleCannotReviveIdleState));
        yield return ("DevAudit.SaveRetryClearsFailureNotice", () => Task.Run(DevAuditRegressionTests.SaveRetryClearsFailureNotice));
        yield return ("DevAudit.ThemeSaveDoesNotHarvestOtherEdits", () => Task.Run(DevAuditRegressionTests.ThemeSaveDoesNotHarvestOtherEdits));
        yield return ("StateViews.RunSummary", () => Task.Run(StateViewsTests.RunSummaryReadsTheDocumentedRunFields));
        yield return ("StateViews.RunSummaryCounts", () => Task.Run(StateViewsTests.RunSummaryCountsWhatThePayloadHolds));
        yield return ("StateViews.RunSummaryNoRun", () => Task.Run(StateViewsTests.RunSummaryWithoutARunIsNull));
        yield return ("StateViews.RunSummaryMissingFields", () => Task.Run(StateViewsTests.RunSummaryMissingFieldsStayNull));
        yield return ("StateViews.DiffChangedPaths", () => Task.Run(StateViewsTests.DiffReportsOnlyChangedPaths));
        yield return ("StateViews.DiffOneSided", () => Task.Run(StateViewsTests.DiffReportsOneSidedPathsAsNull));
        yield return ("StateViews.DiffIdentical", () => Task.Run(StateViewsTests.DiffOfIdenticalPayloadsIsEmptyAndNotTruncated));
        yield return ("StateViews.DiffTypeChange", () => Task.Run(StateViewsTests.DiffTreatsATypeChangeAsAChange));
        yield return ("StateViews.DiffListIndexes", () => Task.Run(StateViewsTests.DiffTracksListLengthAndIndexes));
        yield return ("StateViews.DiffCapReported", () => Task.Run(StateViewsTests.DiffCapIsReportedSoAPartialDiffNeverReadsAsEmpty));
        yield return ("StateViews.DiffEmptyObject", () => Task.Run(StateViewsTests.DiffTreatsAnEmptyObjectAsALeaf));
        yield return ("Mcp.RunSummaryTool", McpServiceTests.ToolsCall_RunSummaryUsesRawState);
        yield return ("Mcp.DiffStateTool", McpServiceTests.ToolsCall_DiffStateComparesTwoPayloads);
        yield return ("Mcp.StringifiedNonObjectArgs", McpServiceTests.ToolsCall_StringifiedNonObjectArgumentsDegradeToEmpty);
        yield return ("Playbook.Embedded", () => Task.Run(PlaybookSectionsTests.StrategyReferenceIsEmbedded));
        yield return ("Playbook.PerScreenOnly", () => Task.Run(PlaybookSectionsTests.ScreenGuidanceIsLimitedToTheScreen));
        yield return ("Playbook.FakeMerchant", () => Task.Run(PlaybookSectionsTests.FakeMerchantGetsTheShopGuidance));
        yield return ("Playbook.NoChoiceNoGuidance", () => Task.Run(PlaybookSectionsTests.ScreensWithoutAStrategicChoiceGetNothing));
        yield return ("Playbook.Bounded", () => Task.Run(PlaybookSectionsTests.TheInjectionIsBounded));
        yield return ("Playbook.MappedHeadingsExist", () => Task.Run(PlaybookSectionsTests.EveryMappedHeadingExistsInTheReference));
        yield return ("Playbook.HeadingsAccountedFor", () => Task.Run(PlaybookSectionsTests.EveryReferenceHeadingIsAccountedFor));
        yield return ("Playbook.ScreenFromState", () => Task.Run(PlaybookSectionsTests.ScreenComesFromTheCompactPayload));
        yield return ("Playbook.PlaybookHeadingsAccountedFor", () => Task.Run(PlaybookSectionsTests.EveryPlaybookHeadingIsAccountedFor));
        yield return ("Playbook.ScreensMatchTheResolver", () => Task.Run(PlaybookSectionsTests.EveryPlaybookScreenIsOneTheGameCanReport));
        yield return ("Playbook.PlaybookSlicePerScreen", () => Task.Run(PlaybookSectionsTests.PlaybookSliceIsLimitedToTheScreen));
        yield return ("Playbook.UnknownScreenGetsTheIndex", () => Task.Run(PlaybookSectionsTests.UnknownScreenGetsTheSectionIndexNotNothing));
        yield return ("Playbook.SystemPromptSlicesTheReferences", () => Task.Run(PlaybookSectionsTests.PlaySystemCarriesTheSliceNotTheDocuments));
        yield return ("Playbook.StaticPromptBudget", () => Task.Run(PlaybookSectionsTests.TheStaticPromptStaysUnderItsBudget));
        yield return ("Playbook.StrategyUsesCompactFields", () => Task.Run(PlaybookSectionsTests.StrategyNamesCompactFieldsNotRawOnes));
        yield return ("Mcp.SceneGuidanceTool", McpServiceTests.ToolsCall_SceneGuidanceFollowsTheScreen);
        yield return ("Mcp.IndexRejection", McpServiceTests.ToolsCall_IndexRejectionNamesTheValidIndices);
        yield return ("Mcp.RawStateFlag", McpServiceTests.ToolsCall_RawStateFlagReachesTheBridge);
        yield return ("Mcp.WaitRawState", McpServiceTests.ToolsCall_WaitRawStateSelectsTheRawRead);
        yield return ("Mcp.ToolErrorEnvelope", McpServiceTests.ToolsCall_ExceptionCarriesTheStructuredEnvelope);
        yield return ("Mcp.ToolNameRefusals", McpServiceTests.ToolsCall_ToolNameRefusalsAreStructured);
        yield return ("Mcp.DecideTool", McpServiceTests.ToolsCall_DecideAnswersOneDecisionPerRead);
        yield return ("Mcp.SceneGuidancePlaybook", McpServiceTests.ToolsCall_SceneGuidanceCarriesThePlaybookSlice);
        yield return ("TeamIntent.Optional", () => Task.Run(TeamIntentTests.NoIntentIsAllowedSoTextOnlyClientsKeepWorking));
        yield return ("TeamIntent.ParsesKnownTypes", () => Task.Run(TeamIntentTests.KnownTypesParseTheirFields));
        yield return ("TeamIntent.RefusesMalformed", () => Task.Run(TeamIntentTests.AMalformedIntentIsRefusedRatherThanDropped));
        yield return ("TeamIntent.DescribesItsOwnFields", () => Task.Run(TeamIntentTests.DescriptionNamesOnlyTheFieldsTheTypeCarries));
        yield return ("TeamIntent.ReachesTheDecisionContext", () => Task.Run(TeamIntentTests.TheConversationCarriesTheSignalBesideTheMessage));
        yield return ("TeamIntent.FocusFireConstrains", () => Task.Run(TeamIntentTests.FocusFireBecomesAConstraintOnTheNextDecision));
        yield return ("TeamIntent.NewestAnnouncementWins", () => Task.Run(TeamIntentTests.ALaterAnnouncementSupersedesAnEarlierOne));
        yield return ("TeamIntent.NoAnnouncementNoConstraint", () => Task.Run(TeamIntentTests.WithoutAnAnnouncementThereIsNoConstraint));
        yield return ("TeammateStatus.HttpEnvelope", () => Task.Run(TeammateStatusTests.ReadsTheActualHttpEnvelope));
        yield return ("TeammateStatus.RejectsErrorEnvelope", () => Task.Run(TeammateStatusTests.RejectsFailedOrMalformedHttpEnvelopes));
        yield return ("TeammateStatus.Combat", () => Task.Run(TeammateStatusTests.ReadsTheCompanionLocalPlayerInCombat));
        yield return ("TeammateStatus.RunFallback", () => Task.Run(TeammateStatusTests.FallsBackToTheRunPartyOutsideCombat));
        yield return ("TeammateStatus.DescribesOwnFacts", () => Task.Run(TeammateStatusTests.DescriptionNamesHealthAndOnlyTheFactsThatApply));
        yield return ("TeammateStatus.Downed", () => Task.Run(TeammateStatusTests.ADownedTeammateSaysSoInsteadOfShowingEnergy));
        yield return ("TeammateStatus.UnreadableIsEmpty", () => Task.Run(TeammateStatusTests.NothingReadableProducesNoLineRatherThanAGuess));
        yield return ("TeammateStatus.UnknownHealthStaysUnknown", () => Task.Run(TeammateStatusTests.MissingHealthStaysUnknownRatherThanZero));
        yield return ("AgentLoop.PlayOnce", AgentLoopTests.PlayOnce_ExecutesSingleValidatedAct);
        yield return ("AgentLoop.CrystalArgs", AgentLoopTests.PlayOnce_ForwardsCrystalSphereArguments);
        yield return ("AgentTools.CrystalSchema", () => Task.Run(AgentLoopTests.ActToolSchema_IncludesCrystalSphereArguments));
        yield return ("AgentLoop.NotActionable", AgentLoopTests.PlayOnce_SkipsWhenNotActionable);
        yield return ("AgentLoop.RejectStaleIndex", AgentLoopTests.PlayOnce_RejectsIndexNotInLatestPayload);
        yield return ("AgentLoop.WaitPending", AgentLoopTests.PlayOnce_WaitsWhenActIsPending);
        yield return ("AgentLoop.NoVisionCapture", AgentLoopTests.PlayOnce_DoesNotCaptureWithoutVision);
        yield return ("AgentLoop.PerModelThinking", AgentLoopTests.PlayOnce_UsesPerModelThinkingIntensity);
        yield return ("Context.DefaultWindow", () => Task.Run(ContextCompactionTests.DefaultWindowIs256000AndUnsetMeansDefault));
        yield return ("Context.TriggerAtEightyPercent", () => Task.Run(ContextCompactionTests.CompactionStartsAtEightyPercentOfTheModelWindow));
        yield return ("Context.SummaryKeepsRecent", () => Task.Run(ContextCompactionTests.SummaryReplacesOlderDecisionsAndKeepsRecentOnes));
        yield return ("Context.BelowThreshold", () => Task.Run(ContextCompactionTests.BelowTheThresholdKeepsTheWholeHistory));
        yield return ("Context.ShortHistoryStaysWhole", () => Task.Run(ContextCompactionTests.ShortHistoryIsNotSummarized));
        yield return ("AgentLoop.ReasoningBudget", AgentLoopTests.PlayOnce_MarksReasoningBudgetExhaustionForRecovery);
        yield return ("AgentLoop.ToolStreamDemotion", AgentLoopTests.PlayOnce_DemotesToollessStreamToNonStreamingOnce);
        yield return ("AgentLoop.JsonActNoTools", AgentLoopTests.PlayOnce_TextOnlyJsonActWithoutTools);
        yield return ("AgentLoop.CrystalJsonNoTools", AgentLoopTests.PlayOnce_TextOnlyCrystalJsonForwardsCoordinatesAndNullTool);
        yield return ("AgentLoop.WaitTool", AgentLoopTests.PlayOnce_WaitUntilActionableTool);
        yield return ("AgentLoop.ParseActJson", () => Task.Run(AgentLoopTests.ParsesActJsonFromMarkdownFence));
        yield return ("AgentLoop.ChatNoAct", AgentLoopTests.Chat_DoesNotExecuteAct);
        yield return ("AgentLoop.ChatPlayIntent", AgentLoopTests.Chat_AllowsActWhenUserAsks);
        yield return ("AgentLoop.ChatAdviceQuestion", AgentLoopTests.Chat_IgnoresPlayACardAdviceQuestion);
        yield return ("AgentLoop.JsonIgnoredWithTools", AgentLoopTests.PlayOnce_IgnoresJsonWhenToolsEnabled);
        yield return ("AgentLoop.RetryFailedAct", AgentLoopTests.PlayOnce_RetriesAfterFailedAct);
        yield return ("AgentLoop.StaticPrefixBeforeState", AgentLoopTests.PlayOnce_PutsTheStaticPrefixBeforeTheDynamicState);
        yield return ("AgentLoop.StaticPrefixStableAcrossSteps", AgentLoopTests.PlayOnce_KeepsTheStaticPrefixStableAcrossSteps);
        yield return ("AgentLoop.StateLastWhenVisionIsAttached", AgentLoopTests.PlayOnce_KeepsTheStateLastWhenVisionIsAttached);
        yield return ("AgentLoop.JsonFallbackStaysInTheStaticPrefix", AgentLoopTests.PlayOnce_JsonFallbackStaysInTheStaticPrefix);
        yield return ("AgentLoop.FallbackSeesPlannerGoal", AgentLoopTests.PlayOnce_FallbackSeesThePlannerGoal);
        yield return ("AgentLoop.DefaultStrategyAddsNoMessage", AgentLoopTests.PlayOnce_DefaultStrategyAddsNoMessage);
        yield return ("PlanningSummary.UnderCapPassThrough", () => Task.Run(PlanningSummaryTests.UnderCapPassesThrough));
        yield return ("PlanningSummary.OverCapStaysValid", () => Task.Run(PlanningSummaryTests.OverCapStaysValidJson));
        yield return ("PlanningSummary.CombatFieldsSurvive", () => Task.Run(PlanningSummaryTests.CombatFieldsSurviveTrimming));
        yield return ("PlanningSummary.NonJsonPassThrough", () => Task.Run(PlanningSummaryTests.NonJsonInputPassesThrough));
        yield return ("PlanningSummary.ExtremeSkeleton", () => Task.Run(PlanningSummaryTests.ExtremeFrameDegradesToASkeletonNotEmpty));
        yield return ("JevTrace.DeciderCarriesOfferedIds", JevTurnTraceTests.DeciderCarriesTheOfferedOptionIds);
        yield return ("JevTrace.DecisionLogCarriesTrace", () => Task.Run(JevTurnTraceTests.DecisionLogCarriesTheTrace));
        yield return ("JevTrace.FallbackMarked", () => Task.Run(JevTurnTraceTests.FallbackRowsAreMarkedAsJevAttempts));
        yield return ("JevTrace.NoTraceStaysLean", () => Task.Run(JevTurnTraceTests.RowsWithoutATraceStayLean));
        yield return ("AgentLoop.CancelPropagates", AgentLoopTests.PlayOnce_PropagatesCancellation);
        yield return ("AgentLoop.UnexpectedExceptionCountsRequest", AgentLoopTests.PlayOnce_UnexpectedExceptionAfterTheRequestStillCountsIt);
        yield return ("AgentLoop.ReasoningDeltaForwarded", AgentLoopTests.ReasoningDeltaReachesTheTurnCallback);
        yield return ("AgentLoop.ChatErrorRecordsBudget", AgentLoopTests.Chat_ErrorPathRecordsTheSpentRequestOnTheBudgetGuard);
        yield return ("McpLauncher.DetectRoot", () => Task.Run(AgentLoopTests.McpRoot_DetectsValidLayout));
        yield return ("NativeMcp.Disabled", McpServiceTests.Disabled_Returns403);
        yield return ("NativeMcp.Initialize", McpServiceTests.Initialize_ReturnsServerInfoAndSession);
        yield return ("NativeMcp.ToolsList", McpServiceTests.ToolsList_IncludesHealthAndAct);
        yield return ("NativeMcp.SkillResources", McpServiceTests.Resources_ExposeSharedPlaySkill);
        yield return ("NativeMcp.ToolsCall", McpServiceTests.ToolsCall_GetGameStateAndAct);
        yield return ("NativeMcp.Notification", McpServiceTests.Notification_Returns202);
        yield return ("NativeMcp.ClientConfig", McpServiceTests.ClientConfig_UsesEnabledUrl);
        yield return ("NativeMcp.OriginMissing", McpServiceTests.MissingOrigin_AllowsNativeInitializeAndOptions);
        yield return ("NativeMcp.OriginUntrusted", McpServiceTests.UntrustedOrigin_RejectedWithoutCreatingSession);
        yield return ("NativeMcp.OriginSameHost", McpServiceTests.SameOriginHost_AllowsInitializeAndEchoesOrigin);
        yield return ("NativeMcp.OriginNullLiteral", McpServiceTests.NullLiteralOrigin_Rejected);
        yield return ("NativeMcp.OriginEvilHostPair", McpServiceTests.EvilOriginAndHost_RejectedEvenWhenTheyMatchEachOther);
        yield return ("NativeMcp.OriginMalformed", McpServiceTests.MalformedOrigin_Rejected);
        yield return ("NativeMcp.OriginHttpPolicy", McpServiceTests.HandleHttp_OriginPolicyRejectsUntrustedAndAllowsSameOrigin);
        yield return ("CrystalSettle.Progress", () => Task.Run(CrystalSphereSettlePolicyTests.RequiresObservedProgressOnSameScreen));
        yield return ("CrystalSettle.FinalProceed", () => Task.Run(CrystalSphereSettlePolicyTests.WaitsForProceedAfterFinalDivination));
        yield return ("CrystalSettle.ScreenChange", () => Task.Run(CrystalSphereSettlePolicyTests.AcceptsChildScreenButNotMissingMinigame));
        yield return ("RewardChoice.ExplicitPick", () => Task.Run(RewardChoicePolicyTests.ExplicitIndexPicksThatOption));
        yield return ("RewardChoice.OutOfRange", () => Task.Run(RewardChoicePolicyTests.ExplicitOutOfRangeIndexIsInvalid));
        yield return ("RewardChoice.AutoFirstCard", () => Task.Run(RewardChoicePolicyTests.MissingIndexResolvesToTheAutomaticKind));
        yield return ("RewardChoice.SkipAnyCount", () => Task.Run(RewardChoicePolicyTests.SkipIsValidWithoutOptions));
        yield return ("RewardChoice.AutoWithoutOptions", () => Task.Run(RewardChoicePolicyTests.AutoWithoutOptionsIsNotAPick));
        yield return ("RewardFlowChoice.ExplicitChoiceSpentOnce", () => Task.Run(RewardFlowChoiceStateTests.ExplicitChoiceIsSpentOnce));
        yield return ("RewardFlowChoice.SkipSentinelSpentOnce", () => Task.Run(RewardFlowChoiceStateTests.SkipSentinelIsSpentOnce));
        yield return ("RewardFlowChoice.StatesDoNotShare", () => Task.Run(RewardFlowChoiceStateTests.StatesDoNotShareChoice));
        yield return ("RewardSkipScope.SameRewardSet", () => Task.Run(RewardSkipScopeTests.AppliesToTheRewardSetItRecorded));
        yield return ("RewardSkipScope.OtherRewardSet", () => Task.Run(RewardSkipScopeTests.DoesNotApplyToAnotherRewardSet));
        yield return ("RewardSkipScope.NothingRecorded", () => Task.Run(RewardSkipScopeTests.DoesNotApplyBeforeAnythingIsRecorded));
        yield return ("RewardSkipScope.UnresolvedNeverApplies", () => Task.Run(RewardSkipScopeTests.UnresolvedRewardSetNeverApplies));
        yield return ("RewardSkipScope.ClearForgets", () => Task.Run(RewardSkipScopeTests.ClearForgetsTheRecordedSkip));
        yield return ("RewardSkipScope.RemarkReplaces", () => Task.Run(RewardSkipScopeTests.RemarkingReplacesThePreviousRewardSet));
        yield return ("RewardSkipScopeContract.NoUnscopedField", () => Task.Run(RewardSkipScopeContractTests.TheUnscopedSkipFieldIsGone));
        yield return ("RewardSkipScopeContract.ButtonFilter", () => Task.Run(RewardSkipScopeContractTests.TheButtonFilterGoesThroughTheScope));
        yield return ("RewardSkipScopeContract.OwnerResolution", () => Task.Run(RewardSkipScopeContractTests.TheOwnerResolutionCoversBothRewardScreens));
        yield return ("RewardSkipScopeContract.SkipRecords", () => Task.Run(RewardSkipScopeContractTests.SkipRewardCardsRecordsTheScope));
        yield return ("RewardSkipScopeContract.DrainRecordsAndClears", () => Task.Run(RewardSkipScopeContractTests.TheDrainRecordsAndClearsTheScope));
        yield return ("RewardSkipScopeContract.ExplicitPicksClear", () => Task.Run(RewardSkipScopeContractTests.ExplicitPicksClearTheScope));
        yield return ("RewardChoiceThreading.NoStaticState", () => Task.Run(RewardChoiceThreadingContractTests.RewardChoiceIsNeverStaticState));
        yield return ("RewardChoiceThreading.DrainForwardsChoice", () => Task.Run(RewardChoiceThreadingContractTests.DrainTakesAndForwardsTheChoice));
        yield return ("RewardChoiceThreading.CollectAsksAuto", () => Task.Run(RewardChoiceThreadingContractTests.CollectRewardsAsksForTheAutomaticChoice));
        yield return ("RewardChoiceThreading.AutoStopsAtCard", () => Task.Run(RewardChoiceThreadingContractTests.AutomaticChoiceStopsAtTheCardDecision));
        yield return ("MenuTransition.ModalNotExit", () => Task.Run(MenuTransitionPolicyTests.ABlockingModalIsNotAMenuExit));
        yield return ("MenuTransition.UnchangedScreen", () => Task.Run(MenuTransitionPolicyTests.UnchangedOrUnknownScreenIsNotAMenuExit));
        yield return ("MenuTransition.ModalNotEmbark", () => Task.Run(MenuTransitionPolicyTests.AModalDoesNotSettleASingleplayerEmbark));
        yield return ("MenuTransition.LobbyReady", () => Task.Run(MenuTransitionPolicyTests.LobbyReadyStaysSettled));
        yield return ("MenuTransition.CharacterSelectScreen", () => Task.Run(MenuTransitionPolicyTests.CharacterSelectNeedsTheScreenItself));
        yield return ("MenuTransition.UnsettledMessage", () => Task.Run(MenuTransitionPolicyTests.UnsettledMessageNamesTheBlockingModal));
        yield return ("BackgroundOutcome.Running", () => Task.Run(BackgroundTaskOutcomeTests.RunningTaskHasNoFailure));
        yield return ("BackgroundOutcome.Success", () => Task.Run(BackgroundTaskOutcomeTests.SuccessfulPurchaseHasNoFailure));
        yield return ("BackgroundOutcome.Rejected", () => Task.Run(BackgroundTaskOutcomeTests.RejectedPurchaseReportsAReason));
        yield return ("BackgroundOutcome.FaultedCanceled", () => Task.Run(BackgroundTaskOutcomeTests.FaultedAndCanceledTasksReportAReason));
        yield return ("CardPlayCounter.StillInHand", () => Task.Run(CardPlayCounterPolicyTests.APlayStillInHandRollsBack));
        yield return ("CardPlayCounter.Settled", () => Task.Run(CardPlayCounterPolicyTests.ASettledPlayKeepsTheCounters));
        yield return ("CardPlayCounter.LeftHand", () => Task.Run(CardPlayCounterPolicyTests.LeftHandOrEndedCombatKeepsTheCounters));
        yield return ("ActionTrust.NoRewardFallback", () => Task.Run(GameActionTrustContractTests.RewardConsumeNeverFallsBackToTheFirstOption));
        yield return ("ActionTrust.RewardRequestValidation", () => Task.Run(GameActionTrustContractTests.RewardRequestRejectsAnOutOfRangeIndexBeforeClicking));
        yield return ("ActionTrust.ShopRemovalFailure", () => Task.Run(GameActionTrustContractTests.ShopRemovalPurchaseFailureSurfaces));
        yield return ("ActionTrust.MenuExitModal", () => Task.Run(GameActionTrustContractTests.MenuExitWaitDoesNotTreatAModalAsSuccess));
        yield return ("ActionTrust.EmbarkModal", () => Task.Run(GameActionTrustContractTests.EmbarkWaitDoesNotTreatAModalAsSuccess));
        yield return ("ActionTrust.CharacterSelectScreen", () => Task.Run(GameActionTrustContractTests.CharacterSelectNeedsTheScreenItself));
        yield return ("ActionTrust.NoEmptyState", () => Task.Run(GameActionTrustContractTests.BundleHandlersNeverFabricateAnEmptyState));
        yield return ("GameTaskWait.Completed", () => Task.Run(GameTaskWaitPolicyTests.AFinishedTaskIsCompleted));
        yield return ("GameTaskWait.Failed", () => Task.Run(GameTaskWaitPolicyTests.AFaultedTaskIsFailed));
        yield return ("GameTaskWait.TimedOut", () => Task.Run(GameTaskWaitPolicyTests.AStillRunningTaskPastItsDeadlineTimesOut));
        yield return ("GameTaskWait.Unreachable", () => Task.Run(GameTaskWaitPolicyTests.AStillRunningTaskBeforeItsDeadlineCannotBeClassified));
        yield return ("GameTaskWait.TimeoutMessage", () => Task.Run(GameTaskWaitPolicyTests.TimeoutMessageNamesTheActionAndTheTimeout));
        yield return ("GameTaskBounding.AllSites", () => Task.Run(GameTaskBoundingContractTests.EveryGameTaskAwaitIsBounded));
        yield return ("GameTaskBounding.Observer", () => Task.Run(GameTaskBoundingContractTests.TheBackgroundObserverStaysUnbounded));
        yield return ("GameTaskBounding.TaskHandedBack", () => Task.Run(GameTaskBoundingContractTests.TheBoundedWaitHandsTheTaskBackForObservation));
        yield return ("EventOptionLocalization.DynamicVars", () => Task.Run(EventOptionLocalizationTests.AddsEventVariablesBeforeFormatting));
        yield return ("EventOptionLocalization.Null", () => Task.Run(EventOptionLocalizationTests.MissingLocStringReturnsEmpty));
        yield return ("EventOptionLocalization.Signature", () => Task.Run(EventOptionLocalizationTests.FormatsSignatureFieldsWithEventVariables));
        yield return ("LoopbackListener.ExcludedRangeUsesDynamicPort", () => Task.Run(LoopbackListenerTests.ExcludedRangeUsesDynamicPort));
        yield return ("LoopbackListener.BindRaceReselectsDynamicPort", () => Task.Run(LoopbackListenerTests.BindRaceReselectsDynamicPort));
        yield return ("LoopbackListener.ExplicitPortNeverChanges", () => Task.Run(LoopbackListenerTests.ExplicitPortNeverChanges));
        yield return ("LoopbackListener.ExplicitReservedPortFailsClearly", () => Task.Run(LoopbackListenerTests.ExplicitReservedPortFailsClearly));
        yield return ("LoopbackListener.ExhaustionIsBounded", () => Task.Run(LoopbackListenerTests.ExhaustionIsBounded));
        yield return ("LoopbackListener.UnexpectedFailureIsNotHidden", () => Task.Run(LoopbackListenerTests.UnexpectedFailureIsNotHidden));
        yield return ("LoopbackListener.RealLoopbackListenerResponds", LoopbackListenerTests.RealLoopbackListenerResponds);
        yield return ("ProactiveChat.DefaultsOff", () => Task.Run(ProactiveChatPolicyTests.DefaultsStayOff));
        yield return ("ProactiveChat.UnknownToneFallsBack", () => Task.Run(ProactiveChatPolicyTests.UnknownToneFallsBackToDefault));
        yield return ("ProactiveChat.ShapeRepair", () => Task.Run(ProactiveChatPolicyTests.ShapeRepairKeepsOptInOffAndFixesTone));
        yield return ("ProactiveChat.TonesDistinct", () => Task.Run(ProactiveChatPolicyTests.ToneInstructionsAreDistinctAndBounded));
        yield return ("ProactiveChat.SituationKey", () => Task.Run(ProactiveChatPolicyTests.SituationKeySeparatesCombatFromScreen));
        yield return ("ProactiveChat.ObserveBoundaries", () => Task.Run(ProactiveChatPolicyTests.ObserveReportsOnlyBoundaryCrossings));
        yield return ("ProactiveChat.RefusesDisabled", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesWhenDisabled));
        yield return ("ProactiveChat.RefusesWithoutMoment", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesWithoutMoment));
        yield return ("ProactiveChat.RefusesWhilePaused", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesWhilePaused));
        yield return ("ProactiveChat.RefusesOnBudget", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesWhenBudgetBlocks));
        yield return ("ProactiveChat.RefusesAtSessionCap", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesAtSessionCap));
        yield return ("ProactiveChat.RefusesInsideInterval", () => Task.Run(ProactiveChatPolicyTests.DecideRefusesInsideMinimumInterval));
        yield return ("ProactiveChat.SendsWhenAllGatesPass", () => Task.Run(ProactiveChatPolicyTests.DecideSendsWhenEveryGatePasses));
        yield return ("ProactiveChat.PromptPerMoment", () => Task.Run(ProactiveChatPolicyTests.BuildPromptDistinguishesMoments));
        yield return ("ProactiveChat.VolumeStaysSmall", () => Task.Run(ProactiveChatPolicyTests.SessionCapStaysSmall));
        yield return ("ProactiveChat.ReadOnlyCannotAct", AgentLoopTests.ReadOnlyChat_CannotActEvenWithPlayIntent);
        yield return ("ProactiveChat.ToneReachesSystemPrompt", AgentLoopTests.ProactiveChat_InjectsToneIntoSystemPrompt);
        yield return ("ProactiveChat.SessionInterval", () => Task.Run(ProactiveChatSessionTests.AllowsSendsThatRespectTheInterval));
        yield return ("ProactiveChat.SessionCap", () => Task.Run(ProactiveChatSessionTests.StopsAtTheSessionCap));
        yield return ("ProactiveChat.RefusalsKeepTheCap", () => Task.Run(ProactiveChatSessionTests.RefusalsDoNotConsumeTheCap));
        yield return ("ProactiveChat.ResetClearsBounds", () => Task.Run(ProactiveChatSessionTests.ResetClearsTheBounds));
       yield return ("ProactiveChat.IntervalBoundaryInclusive", () => Task.Run(ProactiveChatSessionTests.IntervalBoundaryIsInclusive));
        yield return ("ProactiveChat.NewPlaySessionResetsCap", () => Task.Run(ProactiveChatSessionTests.NewPlaySessionHandsBackTheAllowanceButNotTheInterval));
        yield return ("Loc.ChineseIsSource", () => Task.Run(LocalizationTests.ChineseReadsTheTextAsWritten));
        yield return ("Loc.EnglishTable", () => Task.Run(LocalizationTests.EnglishLooksUpTheTable));
        yield return ("Loc.UnknownKeyKeepsChinese", () => Task.Run(LocalizationTests.UnknownKeyFallsBackToChinese));
        yield return ("Loc.BrokenPlaceholderSurvives", () => Task.Run(LocalizationTests.BrokenPlaceholderDoesNotThrow));
        yield return ("Loc.LanguageCodeShapes", () => Task.Run(LocalizationTests.ReadsEveryLanguageCodeShape));
        yield return ("Loc.ChangeNotification", () => Task.Run(LocalizationTests.ReportsLanguageChangesOnlyOnce));
        yield return ("Loc.Coverage", () => Task.Run(LocalizationTests.EveryCallSiteHasAnEnglishEntry));
        yield return ("Loc.SharedKeysAgree", () => Task.Run(LocalizationTests.NoKeyIsDefinedTwiceWithDifferentText));
        yield return ("Loc.EnglishIsRealEnglish", () => Task.Run(LocalizationTests.EveryChineseEntryCarriesRealEnglish));
        yield return ("Loc.NoFrozenText", () => Task.Run(LocalizationTests.NoTranslatedTextIsFrozenAtConstruction));
        yield return ("Loc.GlossaryKeywords", () => Task.Run(LocalizationTests.GlossaryKeywordsStayAlignedWithTheirEnglishSpellings));
        yield return ("Loc.StartupOrder", () => Task.Run(LocalizationTests.StartupReadsTheLanguageBeforeTheUiIsBuilt));
        yield return ("ScreenResolution.MappingTable", () => Task.Run(ScreenResolutionContractTests.EveryScreenMappingIsPinned));
        yield return ("ScreenResolution.FakeMerchant", () => Task.Run(ScreenResolutionContractTests.FakeMerchantOpensThroughTheSharedButton));
        yield return ("ScreenResolution.PatchNotes", () => Task.Run(ScreenResolutionContractTests.PatchNotesClosePathIsWidenedWithoutWeakeningSubmenus));
        yield return ("ScreenResolution.InspectOverlays", () => Task.Run(ScreenResolutionContractTests.InspectOverlaysCloseThroughTheirOwnClose));
        yield return ("ScreenResolution.CapstoneContainerPages", () => Task.Run(ScreenResolutionContractTests.CapstoneContainerPagesAreNamedAndNotDecisionScreens));
        yield return ("ScreenResolution.GameOverBeforeCombatRoom", () => Task.Run(ScreenResolutionContractTests.GameOverIsNamedBeforeTheCombatRoomClaimsIt));
        yield return ("ScreenResolution.CapstonePagesOneBackStep", () => Task.Run(ScreenResolutionContractTests.CapstonePagesOfferOneBackStepAndNeverThePausePage));
        yield return ("TimelineIndex.OneSpace", () => Task.Run(TimelineIndexContractTests.ExecutorIndexesTheSameSlotListTheStateExposes));
        yield return ("TimelineIndex.NonActionableRejected", () => Task.Run(TimelineIndexContractTests.NonActionableSlotsAreRejectedExplicitly));
        yield return ("TimelineIndex.DescriptorFlags", () => Task.Run(TimelineIndexContractTests.CrystalDescriptorsCarryTheirRequirements));
        yield return ("DualLaunchOutcome.TruthTable", () => Task.Run(DualLaunchOutcomeTests.EveryOutcomeHasAPinnedClassification));
        yield return ("DualLaunchOutcome.OnlySuccessCompletes", () => Task.Run(DualLaunchOutcomeTests.OnlyAConfirmedLaunchIsNeitherFailureNorInProgress));
        yield return ("DualLaunchBlock.SoloModeReasoned", () => Task.Run(DualLaunchBlockReasonPolicyTests.SoloModeIsANamedReasonNotBusy));
        yield return ("DualLaunchBlock.AutoplayReasoned", () => Task.Run(DualLaunchBlockReasonPolicyTests.AutoplayRunningIsANamedReasonNotBusy));
        yield return ("DualLaunchBlock.SoloBeatsAutoplay", () => Task.Run(DualLaunchBlockReasonPolicyTests.SoloModeBeatsAutoplayRunning));
        yield return ("DualLaunchBlock.InFlightBusy", () => Task.Run(DualLaunchBlockReasonPolicyTests.InFlightWorkStaysBusy));
        yield return ("DualLaunchBlock.CompanionPasses", () => Task.Run(DualLaunchBlockReasonPolicyTests.CompanionRolePassesTheModeGateAsBefore));
        yield return ("DualLaunchBlock.RuntimeRecordsReason", () => Task.Run(DualLaunchBlockReasonPolicyTests.RuntimeRecordsAReasonForEveryNamedBlock));
        yield return ("DualLaunchOutcome.LanguageIndependent", () => Task.Run(DualLaunchOutcomeTests.HandlerClassifiesOnTheOutcomeNotOnDisplayText));
        yield return ("DualLaunchOutcome.EveryBranchRecords", () => Task.Run(DualLaunchOutcomeTests.EveryLaunchBranchRecordsAnOutcome));
        yield return ("DualLaunchOutcome.PublicEntryInProgressBeforeTaskRun", () => Task.Run(DualLaunchOutcomeTests.PublicEntryAdvertisesInProgressBeforeTaskRun));
        yield return ("DualLaunchOutcome.CoordinatorContract", () => Task.Run(DualLaunchOutcomeTests.CoordinatorExposesAStructuredResult));
        yield return ("DualLaunchOutcome.OfflineCompilable", () => Task.Run(DualLaunchOutcomeTests.TheOutcomeTypeStaysOfflineCompilable));
        yield return ("MenuWaitObservation.SubmenuType", () => Task.Run(MenuWaitObservationTests.SubmenuIsOnlyObservedWhenTheTargetTypeIsCurrent));
        yield return ("MenuWaitObservation.SurvivingSource", () => Task.Run(MenuWaitObservationTests.FlagIsOnlyObservedWhenTheSourceNodeSurvives));
        yield return ("MenuWaitObservation.NoLostNodeSuccess", () => Task.Run(MenuWaitObservationTests.MenuWaitsDoNotTreatALostNodeAsSuccess));
        yield return ("MenuWaitObservation.ConsoleTimeout", () => Task.Run(MenuWaitObservationTests.ConsoleTimeoutNeverReportsCompletion));
        yield return ("GameDataExportSchema.SceneFields", () => Task.Run(GameDataExportSchemaTests.SceneFieldsExistInTheExportSchema));
        yield return ("GameDataExportSchema.ExportCode", () => Task.Run(GameDataExportSchemaTests.ExportSchemaFieldsAppearInTheExportCode));
        yield return ("GameDataExportSchema.KnownCollections", () => Task.Run(GameDataExportSchemaTests.KnownCollectionsMatchTheExportSchema));
        yield return ("ApiException.CarriesStatusAndCode", () => Task.Run(ApiExceptionTests.CarriesStatusAndCode));
        yield return ("ApiException.RetryableDefaultsToFalse", () => Task.Run(ApiExceptionTests.RetryableDefaultsToFalse));
        yield return ("ApiException.DetailsAreOptional", () => Task.Run(ApiExceptionTests.DetailsAreOptional));
        yield return ("JsonHelper.PascalCaseCompact", () => Task.Run(JsonHelperTests.SerializationKeepsPascalCaseAndDropsLayout));
        yield return ("JsonHelper.CaseInsensitiveRead", () => Task.Run(JsonHelperTests.DeserializationIgnoresCase));
        yield return ("HttpServerPort.ExplicitNeverDrifts", () => Task.Run(HttpServerPortPolicyTests.ExplicitPortNeverDrifts));
        yield return ("HttpServerPort.AutoIncrementFlagged", () => Task.Run(HttpServerPortPolicyTests.AutoIncrementedPortIsFlagged));
        yield return ("HttpServerPort.FallbackGate", () => Task.Run(HttpServerPortPolicyTests.FallbackPolicyGateIsPresent));
        yield return ("DeckSelectionAvailability.MatchesExecutableSet", () => Task.Run(DeckSelectionAvailabilityTests.AvailabilityMatchesTheExecutableSet));
        yield return ("DeckSelectionAvailability.ExecutorKeepsGuard", () => Task.Run(DeckSelectionAvailabilityTests.ExecutorKeepsItsGuard));
        yield return ("CharacterSelectReady.ReadyGate", () => Task.Run(CharacterSelectReadyContractTests.ReadyGateClosesSelectCharacterBeforeButtonAndLobbyProbes));
        yield return ("CharacterSelectReady.Advertising", () => Task.Run(CharacterSelectReadyContractTests.AdvertisingStaysBehindTheProbeAndUnreadyStaysIndependent));
        yield return ("CharacterSelectReady.ExecutorGate", () => Task.Run(CharacterSelectReadyContractTests.ExecutorStillUsesCanSelectCharacter));
        yield return ("ContinueCoop.Advertised", () => Task.Run(ContinueCoopContractTests.ActionIsAdvertisedBehindTheSaveProbe));
        yield return ("InviteCoop.Advertised", () => Task.Run(InviteCoopContractTests.ActionIsAdvertisedBehindTheStructuralProbe));
        yield return ("ContinueCoop.ExecutorGuard", () => Task.Run(ContinueCoopContractTests.ExecutorRechecksTheProbeAndFailsRetryably));
        yield return ("ContinueCoop.LoadScreenSurface", () => Task.Run(ContinueCoopContractTests.LoadScreenAdvertisesOnlyWhatTheExecutorHandles));
        yield return ("ContinueCoop.LoadEmbarkWait", () => Task.Run(ContinueCoopContractTests.LoadEmbarkWaitIsBoundedAndSettlesOnEveryExit));
        yield return ("ContinueCoop.LoadCancellation", () => Task.Run(ContinueCoopContractTests.StartLocalLoadWaitsHonourTheCoordinatorToken));
        yield return ("InviteCoop.NoAsyncWithoutAwait", () => Task.Run(InviteCoopContractTests.PendingExecutorReturnsTaskWithoutAsync));
        yield return ("HealthRole.HostRuntime", () => Task.Run(HealthRoleDataTests.HostKeepsHostRuntimeState));
        yield return ("HealthRole.CompanionNotApplicable", () => Task.Run(HealthRoleDataTests.CompanionDoesNotInventHostRuntimeState));
        yield return ("HealthRole.CommonFields", () => Task.Run(HealthRoleDataTests.RouterKeepsCommonFieldsOutsideRoleProjection));
        yield return ("Events.PollingDemand", EventPollingCoordinatorTests.PollingFollowsSubscriberDemand);
        yield return ("Events.SinglePollLoop", EventPollingCoordinatorTests.ConcurrentStartsCreateOneLoop);
        yield return ("Events.StopCancels", EventPollingCoordinatorTests.StopCancelsAnInFlightPoll);
        yield return ("Events.RestartRefusedAfterAbandonedPoll", EventPollingCoordinatorTests.RestartIsRefusedWhileAnAbandonedPollStillRuns);
        yield return ("Events.GenerationFence", () => Task.Run(EventPollingCoordinatorTests.GenerationFenceRejectsPreStopCommits));
        yield return ("Events.SessionSnapshotOrdering", () => Task.Run(EventStreamSubscribersTests.SubscribeAfterSnapshotKeepsInitialFrameFirst));
        yield return ("Events.SessionDropsSnapshotWhenIdle", () => Task.Run(EventStreamSubscribersTests.SnapshotIsDroppedWhenTheLastSubscriberLeaves));
        yield return ("Events.SessionPublishesSnapshotToAll", () => Task.Run(EventStreamSubscribersTests.PublishSnapshotReachesEverySubscriber));
        yield return ("Events.RepeatSuppressed", () => Task.Run(ConsecutiveRepeatSuppressorTests.ImmediateRepeatIsSuppressed));
        yield return ("Events.ChangedStatePublished", () => Task.Run(ConsecutiveRepeatSuppressorTests.DifferentEventOrPayloadIsPublished));
        yield return ("Events.ReturnedStatePublished", () => Task.Run(ConsecutiveRepeatSuppressorTests.ReturnToAPreviousStateIsPublishedAgain));
        yield return ("Events.SuppressorReset", () => Task.Run(ConsecutiveRepeatSuppressorTests.ResetForcesTheNextEventThrough));
        yield return ("Events.NoRepeatedStreamReady", () => Task.Run(ConsecutiveRepeatSuppressorTests.SteadyStatePollRecordsTheSnapshotWithoutAnnouncingIt));
        yield return ("Events.ReaderLoopBounded", () => Task.Run(ConsecutiveRepeatSuppressorTests.EventStreamReaderLoopStaysBounded));
        yield return ("Events.SuppressedIdsNotBurned", () => Task.Run(ConsecutiveRepeatSuppressorTests.SuppressedRepeatsDoNotConsumeEventIds));
        yield return ("Events.SignatureStable", () => Task.Run(ConsecutiveRepeatSuppressorTests.SignatureIsStableForIdenticalPayloads));
        yield return ("Churn.DefaultFillsQueue", () => Task.Run(EventChurnPolicyTests.DefaultCountFillsOneQueue));
        yield return ("Churn.RejectsSmallCount", () => Task.Run(EventChurnPolicyTests.CountBelowQueueCapacityIsRejected));
        yield return ("Churn.RejectsHugeCount", () => Task.Run(EventChurnPolicyTests.CountAboveTheCeilingIsRejected));
        yield return ("Churn.EventsAreNumbered", () => Task.Run(EventChurnPolicyTests.EventsAreSyntheticNumberedAndInOrder));
        yield return ("Churn.PayloadsAreDistinct", () => Task.Run(EventChurnPolicyTests.EveryEventPayloadIsDistinct));
        yield return ("Churn.ActionIsDebugGated", () => Task.Run(EventChurnPolicyTests.ActionIsDebugGatedAndUsesTheRealPublishPath));
        yield return ("Events.OrderedDelivery", () => Task.Run(GameEventSubscriberHubTests.NormalConsumersKeepOrderUntilCapacity));
        yield return ("Events.OverflowDisconnects", GameEventSubscriberHubTests.FullQueueClosesInsteadOfDroppingOldest);
        yield return ("Events.SlowSubscriberIsolation", () => Task.Run(GameEventSubscriberHubTests.SlowSubscriberDoesNotAffectHealthySubscriber));
        yield return ("Events.InitialOrdering", () => Task.Run(GameEventSubscriberHubTests.InitialItemPrecedesPublishedEvents));
        yield return ("ContinueCoop.NoAsyncWithoutAwait", () => Task.Run(ContinueCoopContractTests.PendingExecutorReturnsTaskWithoutAsync));
        yield return ("AgentErrorEnvelope.ApiExceptionKeepsItsHttpMetadata", () => Task.Run(AgentErrorEnvelopeTests.ApiExceptionKeepsItsHttpMetadata));
        yield return ("AgentErrorEnvelope.ApiExceptionDefaultsArePreserved", () => Task.Run(AgentErrorEnvelopeTests.ApiExceptionDefaultsArePreserved));
        yield return ("AgentErrorEnvelope.UnexpectedFailureIsClassifiedNotDropped", () => Task.Run(AgentErrorEnvelopeTests.UnexpectedFailureIsClassifiedNotDropped));
        yield return ("AgentErrorEnvelope.CancellationIsClassifiedWithoutRetry", () => Task.Run(AgentErrorEnvelopeTests.CancellationIsClassifiedWithoutRetry));
        yield return ("AgentErrorEnvelope.EnvelopeReadsBackAsTheFailureText", () => Task.Run(AgentErrorEnvelopeTests.EnvelopeReadsBackAsTheFailureText));
        yield return ("AgentErrorEnvelope.EnvelopeFieldNamesMatchTheHttpRouter", () => Task.Run(AgentErrorEnvelopeTests.EnvelopeFieldNamesMatchTheHttpRouter));
        yield return ("AgentErrorEnvelope.GameBridgeActFailureCarriesTheEnvelope", () => Task.Run(AgentErrorEnvelopeTests.GameBridgeActFailureCarriesTheEnvelope));
        yield return ("AgentErrorEnvelope.AgentLoopFailureCarriesTheEnvelope", () => Task.Run(AgentErrorEnvelopeTests.AgentLoopFailureCarriesTheEnvelope));
        yield return ("CompactViewFidelity.Powers", () => Task.Run(CompactViewFidelityTests.PowersReachTheCompactCombatView));
        yield return ("CompactViewFidelity.IntentNumbers", () => Task.Run(CompactViewFidelityTests.IntentNumbersReachTheCompactCombatView));
        yield return ("CompactViewFidelity.CardAndRelicIds", () => Task.Run(CompactViewFidelityTests.CardAndRelicIdsReachTheCompactViews));
        yield return ("CompactViewFidelity.OverlayAndParty", () => Task.Run(CompactViewFidelityTests.OverlayAndPartyReachTheCompactView));
        yield return ("EnemyBaseHp.RawPayload", () => Task.Run(EnemyBaseHpContractTests.RawEnemyPayloadCarriesTheBaseRoll));
        yield return ("EnemyBaseHp.CompactView", () => Task.Run(EnemyBaseHpContractTests.CompactEnemyPayloadMirrorsTheBaseRoll));
        yield return ("EnemyBaseHp.PayloadType", () => Task.Run(EnemyBaseHpContractTests.EnemyPayloadTypeDeclaresNullableBaseMaxHp));
        yield return ("RewardScreen.BranchPrecedesGrid", () => Task.Run(RewardScreenContractTests.RewardOverlayBranchPrecedesTheVisibleGrid));
        yield return ("Parity.PlaySurfaceExcludesHealthCheck", () => Task.Run(HealthCheckParityTests.InGamePlaySurfaceKeepsNoConnectionCheck));
        yield return ("Parity.EmbeddedPromptUsesOnlyPlayTools", () => Task.Run(HealthCheckParityTests.EmbeddedPromptOnlyInstructsPlaySurfaceTools));
        yield return ("Parity.McpSurfaceKeepsHealthCheck", () => Task.Run(HealthCheckParityTests.McpSurfaceStillDocumentsHealthCheck));
        yield return ("SourceCoverage.ModSourcesParse", () => Task.Run(SourceCoverageTests.EveryModSourceParsesWithoutSyntaxErrors));
        yield return ("SourceCoverage.UncompiledWhitelist", () => Task.Run(SourceCoverageTests.UncompiledSourcesMatchTheDeclaredWhitelist));
        yield return ("SurfacedAction.SelectionConfirmSameSource", () => Task.Run(SurfacedActionParityTests.SelectionCanConfirmComesFromTheExecutorProbe));
        yield return ("SurfacedAction.ModalConfirmSameSource", () => Task.Run(SurfacedActionParityTests.ModalCanConfirmComesFromTheExecutorProbe));
        yield return ("SurfacedAction.ResolveRewardsIndexOptional", () => Task.Run(SurfacedActionParityTests.ResolveRewardsDescriptorDoesNotRequireAnIndex));
        yield return ("SurfacedAction.SkipRewardCardsEnabledFilter", () => Task.Run(SurfacedActionParityTests.SkipRewardCardsFiltersOnAlternativeButtonEnablement));
        yield return ("SurfacedAction.ChooseRewardCardCollection", () => Task.Run(SurfacedActionParityTests.ChooseRewardCardStaysOnTheExecutorCollection));
        yield return ("SurfacedAction.CrystalSphereScreenGuard", () => Task.Run(SurfacedActionParityTests.CrystalSphereExposureStaysOnTheScreenTypeGuard));
        yield return ("CrystalReveal.IdentityFieldsNullable", () => Task.Run(CrystalSphereRevealGatingContractTests.ItemIdentityFieldsAreNullable));
        yield return ("CrystalReveal.IdentityGatedOnTheReveal", () => Task.Run(CrystalSphereRevealGatingContractTests.IdentityIsWithheldUntilTheItemIsRevealed));
        yield return ("CrystalReveal.CompactPassesTheGatedBoard", () => Task.Run(CrystalSphereRevealGatingContractTests.CompactViewPassesTheGatedBoardThrough));
        yield return ("CrystalReveal.DocsStateTheGate", () => Task.Run(CrystalSphereRevealGatingContractTests.DocsSayIdentityArrivesWithTheReveal));
        yield return ("CrystalReveal.HiddenIdentityStaysNullOnTheWire", () => Task.Run(CrystalSphereRevealGatingContractTests.HiddenIdentityStaysOnTheWireAsNull));
        yield return ("SurfacedAction.SkipTargetsEnabledAlternative", () => Task.Run(SurfacedActionParityTests.SkipTargetsEnabledAlternative));
        yield return ("SurfacedAction.RoomProbesDoNotSwallowFailures", () => Task.Run(SurfacedActionParityTests.RoomProbesDoNotSwallowTheirFailures));
        yield return ("CombatGate.OneEvaluationPerStateBuild", () => Task.Run(GameStateCombatGateContractTests.OneStateBuildEvaluatesTheGateOnce));
        yield return ("CombatGate.ActionsAskTheSharedGate", () => Task.Run(GameStateCombatGateContractTests.AvailableActionsAskTheSharedGate));
        yield return ("CombatGate.ReadinessProjectsTheGate", () => Task.Run(GameStateCombatGateContractTests.ReadinessIsAProjectionOfTheGate));
        yield return ("CombatGate.QueueReadIsCombatOnly", () => Task.Run(GameStateCombatGateContractTests.TheActionQueueIsReadOnlyInsideCombat));
        yield return ("DecisionSnapshot.OneWalkPerStateBuild", () => Task.Run(DecisionSnapshotContractTests.OneStateBuildEnumeratesTheActionSurfaceOnce));
        yield return ("DecisionSnapshot.BuilderReadsTheStateOnce", () => Task.Run(DecisionSnapshotContractTests.TheSnapshotBuilderReadsTheStateOnceAndNothingElse));
        yield return ("DecisionSnapshot.CacheIsNotAWireField", () => Task.Run(DecisionSnapshotContractTests.TheDescriptorCacheIsNotAWireField));
        yield return ("DecisionSnapshot.HttpRouteIsOneTurn", () => Task.Run(DecisionSnapshotContractTests.TheHttpRouteServesTheSnapshotInOneGameThreadTurn));
        yield return ("DecisionSnapshot.BridgeUsesTheBuilder", () => Task.Run(DecisionSnapshotContractTests.TheBridgeSerializesTheSnapshotBuilder));
        yield return ("ActionSurface.OneWalkDecidesWhatIsOffered", () => Task.Run(ActionSurfaceContractTests.OneWalkDecidesWhatIsOffered));
        yield return ("ActionSurface.NeitherSurfaceDecidesForItself", () => Task.Run(ActionSurfaceContractTests.NeitherSurfaceDecidesForItself));
        yield return ("ActionSurface.DescriptorTargetIsDocumentedConstant", () => Task.Run(ActionSurfaceContractTests.DescriptorTargetIsDocumentedConstant));
        yield return ("SourceShape.FilesStayWithinBudget", () => Task.Run(SourceShapeContractTests.NoSourceFileGrowsPastItsBudget));
        yield return ("SourceShape.BudgetsTrackTheirFiles", () => Task.Run(SourceShapeContractTests.BudgetsStayCloseToTheFilesTheyGuard));
        yield return ("PredicateSplit.BodyHashProof", () => Task.Run(PredicateRelocationContractTests.MovedPredicatesMatchTheirPreSplitBodies));
        yield return ("PredicateSplit.MovedPredicatesInPredicatePartial", () => Task.Run(PredicateRelocationContractTests.MovedPredicatesAreDeclaredInThePredicatePartialInSourceOrder));
        yield return ("PredicateSplit.SharedHelpersStayInBase", () => Task.Run(PredicateRelocationContractTests.SharedHelpersAreDeclaredOnceAcrossTheSplit));
        yield return ("StateSplit.MovedBodiesUnchanged", () => Task.Run(GameStateServiceRelocationContractTests.EveryMovedDeclarationStillMatchesItsPreSplitText));
        yield return ("StateSplit.BaseFileShrank", () => Task.Run(GameStateServiceRelocationContractTests.TheSplitActuallyShrankTheBaseFile));
        yield return ("PredicateSplit.BudgetCameDown", () => Task.Run(PredicateRelocationContractTests.TheSplitLoweredTheBaseBudgetInsteadOfRaisingIt));
        yield return ("ActionDiagnostics.NoWordlessRecoveryCatch", () => Task.Run(ActionDiagnosticsContractTests.NoRecoveryCatchSwallowsWithoutSayingSo));
        yield return ("ActionDiagnostics.FaultedTasksNameTheirException", () => Task.Run(ActionDiagnosticsContractTests.FaultedGameTasksNameTheirException));
        yield return ("MonsterMoves.FromTheLocTableDirectly", () => Task.Run(MonsterMovesExportContractTests.MovesComeFromTheLocalizationTableDirectly));
        yield return ("MonsterMoves.OnlyTitles", () => Task.Run(MonsterMovesExportContractTests.OnlyTheTitleOfEachMoveIsExported));
        yield return ("MonsterMoves.PrefixFromTitleKey", () => Task.Run(MonsterMovesExportContractTests.ThePrefixFollowsTheMonstersOwnTitleKey));
        yield return ("HandlerContract.RefusesBySurfaceRule", () => Task.Run(HandlerBehaviourContractTests.EachHandlerRefusesByTheRuleTheSurfaceOffersBy));
        yield return ("HandlerContract.ReportsWhatItObserved", () => Task.Run(HandlerBehaviourContractTests.EachHandlerReportsWhatItObserved));
        yield return ("HandlerContract.Potions", () => Task.Run(HandlerBehaviourContractTests.PotionsAreRefusedPreciselyAndQueuedLikeTheGameDoes));
        yield return ("HandlerContract.TreasureRelic", () => Task.Run(HandlerBehaviourContractTests.TreasureRelicsAreChosenThroughTheSynchronizer));
        yield return ("HandlerContract.MissingButtonsAreTransient", () => Task.Run(HandlerBehaviourContractTests.MissingButtonsAreTransientNotRefusals));
        yield return ("HandlerContract.TimelineOverlayRevalidates", () => Task.Run(HandlerBehaviourContractTests.TimelineOverlayRevalidatesBeforeEachClick));
        yield return ("HandlerContract.CrystalToolReadBack", () => Task.Run(HandlerBehaviourContractTests.CrystalToolIsConfirmedByReadingItBack));
        yield return ("HandlerContract.AscensionOneStep", () => Task.Run(HandlerBehaviourContractTests.AscensionMovesOneStepThroughTheLobby));
        yield return ("HandlerContract.GameOverWait", () => Task.Run(HandlerBehaviourContractTests.GameOverWaitIsDismissedOnlyWhileWaiting));
        yield return ("HandlerContract.LobbyDisconnect", () => Task.Run(HandlerBehaviourContractTests.LobbyDisconnectQuitsThroughTheRegistry));
        yield return ("StateBuild.FastBuildsDoNotWarn", () => Task.Run(StateBuildTimingTests.FastBuildsAreCountedWithoutAWarning));
        yield return ("StateBuild.SlowBuildsWarnRateLimited", () => Task.Run(StateBuildTimingTests.ASlowBuildWarnsOnceAndThenSaysHowManyItHeldBack));
        yield return ("StateBuild.RecentPercentiles", () => Task.Run(StateBuildTimingTests.TheSummaryReportsRecentPercentilesOverABoundedWindow));
        yield return ("StateBuild.EveryBuildIsTimed", () => Task.Run(StateBuildTimingTests.EveryStateBuildIsTimedAndReported));
        yield return ("TypedReads.RelicStackIsTheDisplayedCounter", () => Task.Run(TypedStateReadsContractTests.RelicStackIsTheCounterThePlayerSees));
        yield return ("TypedReads.CardModsFromKeywordsAndEnchantment", () => Task.Run(TypedStateReadsContractTests.CardModsComeFromKeywordsAndTheEnchantment));
        yield return ("TypedReads.PilesByType", () => Task.Run(TypedStateReadsContractTests.CombatPilesAreReadByType));
        yield return ("AbandonRun.StopsAtTheConfirmation", () => Task.Run(AbandonRunContractTests.AbandonRunStopsAtTheConfirmation));
        yield return ("AbandonRun.RefusesRatherThanClickingBlind", () => Task.Run(AbandonRunContractTests.AbandonRunRefusesRatherThanClickingBlind));
        yield return ("ReflectedMembers.DeclaredWinsOverBaseOverload", () => Task.Run(ReflectedMemberResolverTests.ADeclaredMethodIsFoundDespiteAPublicBaseMethodOfTheSameName));
        yield return ("ReflectedMembers.ResolverNeverThrows", () => Task.Run(ReflectedMemberResolverTests.AmbiguityAndInheritanceResolveToNothingInsteadOfThrowing));
        yield return ("ReflectedMembers.StaticnessIsPartOfLookup", () => Task.Run(ReflectedMemberResolverTests.StaticnessIsPartOfTheLookup));
        yield return ("ReflectedMembers.RegistryUsesResolver", () => Task.Run(ReflectedMemberResolverTests.TheRegistryResolvesThroughTheResolver));
        yield return ("ReflectedMembers.NoLookupOutsideTheRegistry", () => Task.Run(ReflectedMemberRegistryTests.NothingLooksUpAGameMemberByNameOutsideTheRegistry));
        yield return ("ReflectedMembers.NoNameTakingHelpers", () => Task.Run(ReflectedMemberRegistryTests.NoHelperTakesAGameMemberNameAsAParameter));
        yield return ("ReflectedMembers.NoGodotCallByString", () => Task.Run(ReflectedMemberRegistryTests.GodotIsNeverCalledByAString));
        yield return ("ReflectedMembers.GameButtonsAreClicked", () => Task.Run(ReflectedMemberRegistryTests.GameButtonsAreClickedRatherThanSignalled));
        yield return ("ReflectedMembers.CallsNameRegisteredMembers", () => Task.Run(ReflectedMemberRegistryTests.EveryRegistryCallNamesARegisteredMember));
        yield return ("ReflectedMembers.NoOrphanEntries", () => Task.Run(ReflectedMemberRegistryTests.EveryRegisteredMemberIsAskedFor));
        yield return ("ReflectedMembers.HealthStatusIsDerived", () => Task.Run(ReflectedMemberRegistryTests.HealthStatusComesFromTheProbe));
        yield return ("ReflectedMembers.ProbeRunsAtLoad", () => Task.Run(ReflectedMemberRegistryTests.TheProbeRunsWhenTheModLoads));
        yield return ("CardViewer.BranchesPrecedeGrid", () => Task.Run(CardViewerScreenContractTests.ViewerBranchesPrecedeTheVisibleGrid));
        yield return ("CardViewer.ClosableViewerSameSource", () => Task.Run(CardViewerScreenContractTests.ClosableViewerSetIsSharedByProbeAndExecutor));
        yield return ("CardViewer.SubmenuStackBase", () => Task.Run(CardViewerScreenContractTests.SubmenuStackLookupUsesTheBaseClass));
        yield return ("OverlayTabs.ExtractedFromTheHost", () => Task.Run(OverlayTabContractTests.TabConstructionLivesInItsOwnFile));
        yield return ("OverlayTabs.ExistingTabsKeepTheirOrder", () => Task.Run(OverlayTabContractTests.ExistingTabsKeepTheirOrder));
        yield return ("OverlayTabs.EveryTabHasAPageBuilder", () => Task.Run(OverlayTabContractTests.EveryTabHasAPageBuilder));
        yield return ("OverlayTabs.RefreshOnShow", () => Task.Run(OverlayTabContractTests.TabsThatChangeWithoutAnEventRefreshOnShow));
        yield return ("OverlayTabs.LabelsResolveToEnglish", () => Task.Run(OverlayTabContractTests.CatalogLabelsResolveToEnglish));
        yield return ("OverlayTheme.UniquePresets", () => Task.Run(OverlayThemeTests.EveryPresetHasAUniqueIdAndLabel));
        yield return ("OverlayTheme.UnknownFallsBack", () => Task.Run(OverlayThemeTests.AnUnknownThemeFallsBackToTheDefault));
        yield return ("OverlayTheme.ContrastFloors", () => Task.Run(OverlayThemeTests.EveryPresetStaysReadableOnEverySurface));
        yield return ("OverlayTheme.ContrastMath", () => Task.Run(OverlayThemeTests.TheContrastHelperMatchesTheKnownEndpoints));
        yield return ("OverlayTheme.AppliedBeforeBuild", () => Task.Run(OverlayThemeTests.TheStoredThemeIsAppliedBeforeAnythingIsBuilt));
        yield return ("OverlayTabs.DecisionLogLiveRefresh", () => Task.Run(OverlayTabContractTests.DecisionLogFollowsTheLiveRefreshPath));
        yield return ("OverlayTabs.DecisionPageReusesRuntimeCounters", () => Task.Run(OverlayTabContractTests.DecisionPageReusesTheRuntimeCounters));
        yield return ("OverlayTabs.DecisionLinesNewestFirst", () => Task.Run(OverlayTabContractTests.DecisionLinesAreNewestFirstAndUnknownUsageStaysUnknown));
        yield return ("OverlayTabs.NoDecisionsNoLines", () => Task.Run(OverlayTabContractTests.NoDecisionsProducesNoLines));
        yield return ("OverlayTabs.JevAttemptMarked", () => Task.Run(OverlayTabContractTests.JevAttemptRowsAreMarkedInTheLine));
        yield return ("OverlayTabs.DecisionLinesBounded", () => Task.Run(OverlayTabContractTests.DecisionLinesStayBounded));
        yield return ("OverlayLayout.SaveOutsideScroll", () => Task.Run(OverlayLayoutContractTests.SaveStaysOutsideTheScrollingForm));
        yield return ("OverlayLayout.JumpButtonsMatchSections", () => Task.Run(OverlayLayoutContractTests.EveryJumpButtonNamesARegisteredSection));
        yield return ("OverlayLayout.CoopActionsFirst", () => Task.Run(OverlayLayoutContractTests.CoopActionsComeBeforeCoopStatus));
        yield return ("OverlayLayout.CoopChatAdapts", () => Task.Run(OverlayLayoutContractTests.CoopChatBoxAdaptsToThePage));
        yield return ("OverlayLayout.ChatFooterCompact", () => Task.Run(OverlayLayoutContractTests.ChatFooterKeepsItsHeightForMessages));
        yield return ("OverlayLayout.LongLabelsReflow", () => Task.Run(OverlayLayoutContractTests.LongLabelsGoThroughTheReflowingHelper));
        yield return ("OverlayLayout.SentenceFieldsReflow", () => Task.Run(OverlayLayoutContractTests.SentenceBearingFieldsReflow));
        yield return ("OverlayLayout.PlayControlsStack", () => Task.Run(OverlayLayoutContractTests.PlayControlsDoNotRequireOneWideRow));
        yield return ("OverlayLayout.UsageSentencesStack", () => Task.Run(OverlayLayoutContractTests.UsageSentencesDoNotShareARow));
        yield return ("OverlayLayout.ModelDeleteAndMetricsStayNarrow", () => Task.Run(OverlayLayoutContractTests.ModelDeletionAndPlayMetricsCannotStretchThePanel));
        yield return ("OverlayLayout.MetricValuesReflow", () => Task.Run(OverlayLayoutContractTests.MetricValuesReflowInsideTheirTiles));
        yield return ("OverlayLayout.RichLogsReflow", () => Task.Run(OverlayLayoutContractTests.RichLogsReflowInsteadOfClippingHorizontally));
        yield return ("OverlayLayout.ThemeRepaintsSurfaces", () => Task.Run(OverlayLayoutContractTests.ThemeSwitchRepaintsSurfacesNotJustText));
        yield return ("OverlayLayout.ChromeIsTagged", () => Task.Run(OverlayLayoutContractTests.OverlayChromeIsTaggedForRepaint));
        yield return ("OverlayLayout.SwatchUsesChildNodes", () => Task.Run(OverlayLayoutContractTests.SwatchPreviewUsesChildNodes));
        yield return ("OverlayLayout.JumpLandsAtTop", () => Task.Run(OverlayLayoutContractTests.JumpingToASectionPutsItAtTheTop));
        yield return ("OverlayChat.TurnsCarryReasoningAndAction", () => Task.Run(OverlayChatStreamContractTests.ReasoningAndActionsBecomeConversationTurns));
        yield return ("OverlayChat.OneCapForMemoryDiskAndLog", () => Task.Run(OverlayChatStreamContractTests.OneCapForMemoryDiskAndTheLog));
        yield return ("OverlayChat.EachRoleDrawnAndFollowsNewest", () => Task.Run(OverlayChatStreamContractTests.TheLogDrawsEachRoleAndFollowsTheNewest));
        yield return ("OverlayChat.ThrowingBlockCannotFreeze", () => Task.Run(OverlayChatStreamContractTests.AThrowingBlockCannotFreezeTheConversation));
        yield return ("OverlayChat.NoActSwitch", () => Task.Run(OverlayChatStreamContractTests.TheConversationHasNoActSwitch));
        yield return ("OverlayChat.JevPanelReadings", () => Task.Run(OverlayChatStreamContractTests.TheJevPanelShowsWhatJevChose));
        // The live reasoning stream: a thinking model's partial thought reaches the conversation while
        // the turn is still in flight, and the recorded bubble replaces it rather than doubling it.
        yield return ("LiveReasoning.SwitchGatesStorage", () => Task.Run(LiveThoughtBufferTests.NothingIsStoredWhileTheSwitchIsOff));
        yield return ("LiveReasoning.BlankIsNotStored", () => Task.Run(LiveThoughtBufferTests.BlankReasoningIsNotStored));
        yield return ("LiveReasoning.ClippedAndCleared", () => Task.Run(LiveThoughtBufferTests.StreamedTextIsClippedAndCleared));
        yield return ("LiveReasoning.PreviewBounded", () => Task.Run(LiveThoughtBufferTests.ThePreviewIsBoundedWhileTheCompletionKeepsEverything));
        yield return ("LiveReasoning.PreviewShortWhole", () => Task.Run(LiveThoughtBufferTests.ShortReasoningPreviewsWhole));
        yield return ("LiveReasoning.CallbackReachesTheRequest", () => Task.Run(LiveReasoningWiringTests.TheLiveCallbackTravelsFromTheRuntimeToTheRequest));
        yield return ("LiveReasoning.NoDuplicateBubble", () => Task.Run(LiveReasoningWiringTests.TheStreamedBubbleIsClearedBeforeTheRecordedOne));
        yield return ("LiveReasoning.OverlayGatedByTheSwitch", () => Task.Run(LiveReasoningWiringTests.TheOverlayDrawsThePartialOnlyWhenThinkingIsShown));
        yield return ("LiveReasoning.NotPersisted", () => Task.Run(LiveReasoningWiringTests.ThePartialStaysOutOfEverythingPersisted));
        yield return ("ActionGate.FirstActionOwnsIt", () => Task.Run(ActionExecutionGateTests.AFreshGateAdmitsTheFirstAction));
        yield return ("ActionGate.ConcurrentRefused", () => Task.Run(ActionExecutionGateTests.AHeldGateRefusesTheSecondActionImmediately));
        yield return ("ActionGate.HeldAcrossAwait", ActionExecutionGateTests.TheLeaseIsHeldUntilTheAwaitedCoreTaskCompletes);
        yield return ("ActionGate.ReleasedOnThrow", ActionExecutionGateTests.AThrownCoreFailureStillReleasesTheLease);
        yield return ("ActionGate.ReleasedOnCancellation", ActionExecutionGateTests.ACanceledCoreWaitStillReleasesTheLease);
        yield return ("ActionGate.DoubleReleaseIsSafe", () => Task.Run(ActionExecutionGateTests.ReleasingTwiceNeverFreesAnotherActionsLease));
        yield return ("ActionGate.DisposeReleases", () => Task.Run(ActionExecutionGateTests.DisposingTheLeaseReleasesIt));
        yield return ("ActionGate.WiringHoldsTheLease", () => Task.Run(ActionExecutionGateWiringContractTests.ExecuteAsyncHoldsTheLeaseAcrossTheAwaitedCoreTask));
        yield return ("ActionGate.RefusalIsDocumented", () => Task.Run(ActionExecutionGateWiringContractTests.TheRefusalIsTheDocumentedActionInFlightError));
        // The screenshot sequence: hide the overlay, wait for a frame the renderer actually drew, read
        // the viewport, put the overlay back -- bounded, cancellable, serialized, and never reading a
        // frame that still contains the panel.
        yield return ("Screenshot.HideWaitCaptureRestore", () => Task.Run(ScreenshotCapturePolicyTests.CaptureHidesWaitsForDrawnFramesAndRestores));
        yield return ("Screenshot.HiddenStaysHidden", () => Task.Run(ScreenshotCapturePolicyTests.AnOverlayThePlayerHadHiddenIsNotReopened));
        yield return ("Screenshot.NoFrameNoCapture", () => Task.Run(ScreenshotCapturePolicyTests.NoDrawnFrameYieldsNoScreenshotAndStillRestores));
        yield return ("Screenshot.FrozenRendererBounded", () => Task.Run(ScreenshotCapturePolicyTests.ACaptureThatNeverGetsAFrameDoesNotStall));
        yield return ("Screenshot.CancellationRestores", () => Task.Run(ScreenshotCapturePolicyTests.CancellationRestoresTheOverlayAndCapturesNothing));
        yield return ("Screenshot.EmptyViewportRestores", () => Task.Run(ScreenshotCapturePolicyTests.AnEmptyViewportReadIsNoScreenshotAndStillRestores));
        yield return ("Screenshot.ExpiredDeadlineNoGate", () => Task.Run(ScreenshotCapturePolicyTests.AnExpiredDeadlineCapturesNothingAndHoldsNothing));
        yield return ("Screenshot.ConcurrentSerialized", () => Task.Run(ScreenshotCapturePolicyTests.OverlappingCapturesAreSerialized));
        yield return ("Screenshot.HeldGateTimesOut", () => Task.Run(ScreenshotCapturePolicyTests.AHeldGateTimesOutTheWaiterInsteadOfStallingIt));
        // The Godot half of the seam cannot be linked offline, so its two load-bearing facts are read
        // from source: the wait ends on a drawn frame, and the bridge runs the tested policy.
        yield return ("Screenshot.DrawnFrameContract", () => Task.Run(SessionControlContractTests.ScreenshotWaitsForADrawnFrameAndRestoresTheOverlay));
    }
}
