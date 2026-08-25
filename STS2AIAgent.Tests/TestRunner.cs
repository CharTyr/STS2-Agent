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
        yield return ("AgentLoop.PlayOnce", AgentLoopTests.PlayOnce_ExecutesSingleValidatedAct);
        yield return ("AgentLoop.NotActionable", AgentLoopTests.PlayOnce_SkipsWhenNotActionable);
        yield return ("AgentLoop.RejectStaleIndex", AgentLoopTests.PlayOnce_RejectsIndexNotInLatestPayload);
        yield return ("AgentLoop.WaitPending", AgentLoopTests.PlayOnce_WaitsWhenActIsPending);
        yield return ("AgentLoop.NoVisionCapture", AgentLoopTests.PlayOnce_DoesNotCaptureWithoutVision);
        yield return ("AgentLoop.PerModelThinking", AgentLoopTests.PlayOnce_UsesPerModelThinkingIntensity);
        yield return ("AgentLoop.JsonActNoTools", AgentLoopTests.PlayOnce_TextOnlyJsonActWithoutTools);
        yield return ("AgentLoop.WaitTool", AgentLoopTests.PlayOnce_WaitUntilActionableTool);
        yield return ("AgentLoop.ParseActJson", () => Task.Run(AgentLoopTests.ParsesActJsonFromMarkdownFence));
        yield return ("AgentLoop.ChatNoAct", AgentLoopTests.Chat_DoesNotExecuteAct);
        yield return ("AgentLoop.ChatPlayIntent", AgentLoopTests.Chat_AllowsActWhenUserAsks);
        yield return ("AgentLoop.ChatAdviceQuestion", AgentLoopTests.Chat_IgnoresPlayACardAdviceQuestion);
        yield return ("AgentLoop.JsonIgnoredWithTools", AgentLoopTests.PlayOnce_IgnoresJsonWhenToolsEnabled);
        yield return ("AgentLoop.RetryFailedAct", AgentLoopTests.PlayOnce_RetriesAfterFailedAct);
        yield return ("AgentLoop.CancelPropagates", AgentLoopTests.PlayOnce_PropagatesCancellation);
        yield return ("McpLauncher.DetectRoot", () => Task.Run(AgentLoopTests.McpRoot_DetectsValidLayout));
    }
}
