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

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}, actual {actual}.");
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
        yield return ("TeamControl.WaitForCommittedWork", AutoPlaySessionTests.PauseWaitsForCommittedWorkAndBlocksRestart);
        yield return ("TeamControl.CancelModel", AutoPlaySessionTests.PauseCancelsWaitingModel);
        yield return ("TeamControl.NoOverlappingLoops", AutoPlaySessionTests.ImmediatePauseNeverOverlapsGenerations);
        yield return ("TeamControl.Lifetime", () => Task.Run(AutoPlaySessionTests.CanceledLifetimeCannotStart));
        yield return ("TeamControl.NoLateAct", AgentLoopTests.PauseAfterModelResponseDoesNotDispatchAct);
        yield return ("TeamControl.TransportAck", TeamConversationTests.PauseControlHasExplicitAcknowledgement);
        yield return ("TeamChat.ReadOnly", AgentLoopTests.TeamChat_CannotActEvenWithPlayIntent);
        yield return ("TeamChat.NextDecision", AgentLoopTests.TeamSuggestion_ReachesNextPlayDecision);
        yield return ("TeamChat.BoundedHistory", () => Task.Run(TeamConversationTests.HistoryIsBoundedAndCleared));
        yield return ("TeamChat.SessionAuthorization", () => Task.Run(TeamConversationTests.SessionTokensAreRequiredAndDistinct));
        yield return ("TeamChat.Transport", TeamConversationTests.TransportChecksIdentityAndSendsBoundedBody);
        yield return ("TeamChat.ReusedPort", TeamConversationTests.ReusedPortDoesNotReceiveMessage);
        yield return ("CoopStartup.ExcludedRange", () => Task.Run(CompanionStartupTests.ExcludedRangeUsesDynamicPort));
        yield return ("CoopStartup.BindRace", () => Task.Run(CompanionStartupTests.BindRaceReselectsDynamicPort));
        yield return ("CoopStartup.ExplicitPort", () => Task.Run(CompanionStartupTests.ExplicitPortNeverChanges));
        yield return ("CoopStartup.ExplicitReserved", () => Task.Run(CompanionStartupTests.ExplicitReservedPortFailsClearly));
        yield return ("CoopStartup.BoundedFailure", () => Task.Run(CompanionStartupTests.ExhaustionIsBounded));
        yield return ("CoopStartup.UnexpectedFailure", () => Task.Run(CompanionStartupTests.UnexpectedFailureIsNotHidden));
        yield return ("CoopStartup.RealHttp", CompanionStartupTests.RealLoopbackListenerResponds);
        yield return ("CoopStartup.Identity", () => Task.Run(CompanionStartupTests.HealthRequiresExactCompanionIdentity));
        yield return ("CoopStartup.Preconditions", () => Task.Run(CompanionStartupTests.LaunchPreconditionsProtectHumanRun));
        yield return ("SettingsStore.RoundTrip", () => Task.Run(SettingsStoreTests.RoundTrip_PreservesEndpointsModelsAndRoles));
        yield return ("SettingsStore.MissingFile", () => Task.Run(SettingsStoreTests.Load_MissingFile_CreatesDefaults));
        yield return ("SettingsStore.MigrateThinking", () => Task.Run(SettingsStoreTests.Load_MigratesGlobalThinkingIntensityOntoModels));
        yield return ("Thinking.gpt-4o", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("gpt-4o", "auto", "prompt")));
        yield return ("Thinking.gpt-5", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("gpt-5", "auto", "reasoning_effort")));
        yield return ("Thinking.o3-mini", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("o3-mini", "auto", "reasoning_effort")));
        yield return ("Thinking.deepseek", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("deepseek-chat", "auto", "deepseek")));
        yield return ("Thinking.explicit", () => Task.Run(() => ThinkingRequestBuilderTests.Infer("anything", "reasoning_effort", "reasoning_effort")));
        yield return ("Thinking.off", () => Task.Run(ThinkingRequestBuilderTests.Off_DisablesDeepSeekThinking));
        yield return ("OpenAI.ResolveUrl", () => Task.Run(OpenAiCompatibleClientTests.ResolveCompletionsUrl_NormalizesBase));
        yield return ("OpenAI.ParseCompletion", () => Task.Run(OpenAiCompatibleClientTests.ParseCompletion_ReadsToolCallsAndReasoning));
        yield return ("OpenAI.PostBody", OpenAiCompatibleClientTests.CompleteAsync_PostsOpenAiCompatibleBody);
        yield return ("OpenAI.DeepSeekExtraBody", OpenAiCompatibleClientTests.CompleteAsync_PostsDeepSeekThinkingInExtraBody);
        yield return ("OpenAI.ParseSse", () => Task.Run(OpenAiCompatibleClientTests.ParseSse_AccumulatesContentAndToolCalls));
        yield return ("GameData.DetectScene", () => Task.Run(GameDataFilterTests.DetectScene_MatchesGuidedMcpRules));
        yield return ("GameData.ProjectRelevant", () => Task.Run(GameDataFilterTests.ProjectRelevant_KeepsCombatCardFields));
        yield return ("PlayIntent.Detect", () => Task.Run(PlayIntentTests.DetectsPlayPhrasesAndIgnoresQuestions));
        yield return ("ActIndex.Validate", () => Task.Run(ActIndexValidatorTests.RejectsMissingAndStaleIndexes));
        yield return ("ActIndex.Unsettled", () => Task.Run(ActIndexValidatorTests.DetectsUnsettledActResults));
        yield return ("Reflection.PrivateBaseField", () => Task.Run(ReflectionMemberAccessorTests.ReadsPrivateBaseFieldFromDerivedInstance));
        yield return ("Reflection.PrivateBaseProperty", () => Task.Run(ReflectionMemberAccessorTests.ReadsPrivateBasePropertyFromDerivedInstance));
        yield return ("Reflection.DerivedPrecedence", () => Task.Run(ReflectionMemberAccessorTests.PrefersDerivedMemberWithSameName));
        yield return ("Reflection.ThrowingDerived", () => Task.Run(ReflectionMemberAccessorTests.DoesNotFallBackWhenDerivedGetterThrows));
        yield return ("UnlockConfirm.Reflected", () => Task.Run(UnlockConfirmResolutionPolicyTests.PrefersUsableReflectedCandidate));
        yield return ("UnlockConfirm.Fallback", () => Task.Run(UnlockConfirmResolutionPolicyTests.SkipsUnusableCandidatesBeforeUsableFallback));
        yield return ("UnlockConfirm.Session", () => Task.Run(UnlockConfirmResolutionPolicyTests.ProbeSignatureIncludesScreenInstance));
        yield return ("UnlockScreen.MixedCardGrid", () => Task.Run(UnlockScreenContractTests.UnlockCardsScreenWithVisibleGridReportsOnlyUnlockAction));
        yield return ("GameOver.ContinueAction", () => Task.Run(GameOverContractTests.DedicatedContinueActionIsWiredEndToEnd));
        yield return ("GameOver.ReturnGate", () => Task.Run(GameOverContractTests.ReturnActionRequiresVisibleAndEnabledMainMenuButton));
        yield return ("GameOver.NativeButtons", () => Task.Run(GameOverContractTests.ContinueAndReturnUseNativeButtonsWithoutSkippingSummary));
        yield return ("GameOver.SummaryReady", () => Task.Run(GameOverContractTests.ContinueWaitsForNativeSummaryReadiness));
        yield return ("GameOver.Phases", () => Task.Run(GameOverContractTests.GameOverPayloadKeepsContinueSummaryAndReturnAsDistinctPhases));
        yield return ("GameOver.SaveContract", () => Task.Run(GameOverContractTests.GameOverPayloadReportsPhysicalProgressSaveVerification));
        yield return ("GameOver.SaveVerified", () => Task.Run(ProgressSaveVerificationTests.MatchingPhysicalFileIsVerified));
        yield return ("GameOver.SaveEquivalentJson", () => Task.Run(ProgressSaveVerificationTests.EquivalentJsonWithDifferentFormattingAndPropertyOrderIsVerified));
        yield return ("GameOver.SaveEquivalentNumbers", () => Task.Run(ProgressSaveVerificationTests.EquivalentNumericRepresentationsAreVerified));
        yield return ("GameOver.SavePersistedJson", () => Task.Run(ProgressSaveVerificationTests.MatchingPersistedJsonIsVerifiedWithoutPhysicalReopen));
        yield return ("GameOver.SaveMismatch", () => Task.Run(ProgressSaveVerificationTests.MismatchedScoreOrUnlockStateCannotReportSuccess));
        yield return ("GameOver.SaveMissingMalformed", () => Task.Run(ProgressSaveVerificationTests.MissingOrMalformedFileCannotReportSuccess));
        yield return ("GameOver.SaveReadFailure", () => Task.Run(ProgressSaveVerificationTests.ReadFailureCannotReportSuccess));
        yield return ("DeckSelection.PayloadProgress", () => Task.Run(DeckSelectionContractTests.DeckGridPayloadReportsNativeSelectionProgress));
        yield return ("DeckSelection.ClickSettle", () => Task.Run(DeckSelectionContractTests.DeckGridClickSettlesInEitherDirectionBeforeConfirming));
        yield return ("CombatDiagnostics.CanPlay", () => Task.Run(CombatDiagnosticsContractTests.HandPayloadKeepsNativeCanPlayEvidence));
        yield return ("CombatDiagnostics.Readiness", () => Task.Run(CombatDiagnosticsContractTests.CombatPayloadDistinguishesQueueModalAndSnapshotLocks));
        yield return ("AgentLoop.PlayOnce", AgentLoopTests.PlayOnce_ExecutesSingleValidatedAct);
        yield return ("AgentLoop.CrystalArgs", AgentLoopTests.PlayOnce_ForwardsCrystalSphereArguments);
        yield return ("AgentTools.CrystalSchema", () => Task.Run(AgentLoopTests.ActToolSchema_IncludesCrystalSphereArguments));
        yield return ("AgentLoop.NotActionable", AgentLoopTests.PlayOnce_SkipsWhenNotActionable);
        yield return ("AgentLoop.RejectStaleIndex", AgentLoopTests.PlayOnce_RejectsIndexNotInLatestPayload);
        yield return ("AgentLoop.WaitPending", AgentLoopTests.PlayOnce_WaitsWhenActIsPending);
        yield return ("AgentLoop.NoVisionCapture", AgentLoopTests.PlayOnce_DoesNotCaptureWithoutVision);
        yield return ("AgentLoop.PerModelThinking", AgentLoopTests.PlayOnce_UsesPerModelThinkingIntensity);
        yield return ("AgentLoop.JsonActNoTools", AgentLoopTests.PlayOnce_TextOnlyJsonActWithoutTools);
        yield return ("AgentLoop.CrystalJsonNoTools", AgentLoopTests.PlayOnce_TextOnlyCrystalJsonForwardsCoordinatesAndNullTool);
        yield return ("AgentLoop.WaitTool", AgentLoopTests.PlayOnce_WaitUntilActionableTool);
        yield return ("AgentLoop.ParseActJson", () => Task.Run(AgentLoopTests.ParsesActJsonFromMarkdownFence));
        yield return ("AgentLoop.ChatNoAct", AgentLoopTests.Chat_DoesNotExecuteAct);
        yield return ("AgentLoop.ChatPlayIntent", AgentLoopTests.Chat_AllowsActWhenUserAsks);
        yield return ("AgentLoop.ChatAdviceQuestion", AgentLoopTests.Chat_IgnoresPlayACardAdviceQuestion);
        yield return ("AgentLoop.JsonIgnoredWithTools", AgentLoopTests.PlayOnce_IgnoresJsonWhenToolsEnabled);
        yield return ("AgentLoop.RetryFailedAct", AgentLoopTests.PlayOnce_RetriesAfterFailedAct);
        yield return ("AgentLoop.CancelPropagates", AgentLoopTests.PlayOnce_PropagatesCancellation);
        yield return ("McpLauncher.DetectRoot", () => Task.Run(AgentLoopTests.McpRoot_DetectsValidLayout));
        yield return ("CrystalSettle.Progress", () => Task.Run(CrystalSphereSettlePolicyTests.RequiresObservedProgressOnSameScreen));
        yield return ("CrystalSettle.FinalProceed", () => Task.Run(CrystalSphereSettlePolicyTests.WaitsForProceedAfterFinalDivination));
        yield return ("CrystalSettle.ScreenChange", () => Task.Run(CrystalSphereSettlePolicyTests.AcceptsChildScreenButNotMissingMinigame));
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
    }
}
