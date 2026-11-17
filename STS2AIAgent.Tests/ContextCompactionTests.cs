using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using System.Text.Json;

namespace STS2AIAgent.Tests;

internal static class ContextCompactionTests
{
    public static void DefaultWindowIs256000AndUnsetMeansDefault()
    {
        Assert.Equal(256_000, ContextCompaction.ResolveWindow(new LlmModelConfig()));
        Assert.Equal(128_000, ContextCompaction.ResolveWindow(new LlmModelConfig { ContextWindow = 128_000 }));
    }

    public static void FixedMemoryIgnoresPreviousUsageAndKeepsRecent()
    {
        var decisions = Enumerable.Range(1, 40).Select(id => new DecisionLogEntry(id, "t", "agent", "play_card",
            new string('x', 500), null, 1, 10, "run")).ToArray();
        var missing = ContextCompaction.Format(decisions);
        var high = ContextCompaction.Format(decisions);
        Assert.Equal(missing, high);
        Assert.True(missing!.Length <= ContextCompaction.MaxMemoryCharacters);
        Assert.Contains("40. play_card", missing);
        Assert.False(missing.Contains("34. play_card"));
        Assert.Contains("never reuse an index", missing);
        Assert.Null(ContextCompaction.Format(Array.Empty<DecisionLogEntry>()));
    }

    public static void RemovesHistoryButPreservesStateAndToolProtocol()
    {
        var call = new LlmToolCall { Id = "id", Name = "read", ArgumentsJson = "{}" };
        var messages = new List<LlmMessage> {
            LlmMessage.System("rules", ContextPart.Static),
            LlmMessage.User(new string('h', 3000), part: ContextPart.History),
            LlmMessage.User("player guidance and legal actions", part: ContextPart.State),
            LlmMessage.Assistant(null, new[] { call }, "reasoning echo"),
            LlmMessage.Tool("id", "result")
        };
        var stats = ContextBudget.Prepare(messages, null, 1000, 100, 2);
        Assert.True(ContextBudget.Fits(stats));
        Assert.True(stats.Trimmed);
        Assert.Equal(4, messages.Count);
        Assert.Equal("reasoning echo", messages[2].Reasoning);
        Assert.Equal("id", messages[2].ToolCalls![0].Id);
        Assert.Equal("id", messages[3].ToolCallId);
        Assert.True(stats.State > 0 && stats.Tools > 0);
        Assert.Equal(0L, stats.History);
        Assert.Equal(1, stats.RemovedMessages);
        Assert.True(stats.RemovedEstimate > 1000);
    }

    public static void HugeRequiredResultAndImagesAreBudgeted()
    {
        var messages = new List<LlmMessage> { LlmMessage.User("latest", new byte[4096]),
            LlmMessage.Assistant(null, new[] { new LlmToolCall { Id = "a", Name = "x", ArgumentsJson = "{}" } }, "r"),
            LlmMessage.Tool("a", new string('x', 10000)) };
        var tool = new LlmTool { Name = "x", Description = "find it", Parameters = new { type = "object", properties = new { a = new { type = "string" } } } };
        var stats = ContextBudget.Prepare(messages, new[] { tool }, 6000, 100, 1);
        Assert.False(ContextBudget.Fits(stats));
        Assert.True(stats.Images >= 4096);
        Assert.True(stats.Tools > 5000);
        Assert.Equal(3, messages.Count);
        Assert.Equal(100L, stats.OutputReserve);
        Assert.True(stats.Margin > 0);
    }

    public static void UsagePresenceAndCacheAreHonest()
    {
        using var noCache = JsonDocument.Parse("""{"usage":{"prompt_tokens":42,"completion_tokens":5}}""");
        var usage = OpenAiCompatibleClient.ReadUsage(noCache.RootElement)!;
        Assert.True(usage.PromptTokensReported && usage.CompletionTokensReported);
        Assert.False(usage.CachedPromptTokensReported);
        using var cache = JsonDocument.Parse("""{"usage":{"prompt_tokens":42,"completion_tokens":5,"prompt_tokens_details":{"cached_tokens":0}}}""");
        Assert.True(OpenAiCompatibleClient.ReadUsage(cache.RootElement)!.CachedPromptTokensReported);
        using var absent = JsonDocument.Parse("""{"choices":[]}""");
        Assert.Null(OpenAiCompatibleClient.ReadUsage(absent.RootElement));

        using var zero = JsonDocument.Parse("""{"usage":{"prompt_tokens":0,"completion_tokens":0,"prompt_tokens_details":{"cached_tokens":0}}}""");
        var zeroUsage = OpenAiCompatibleClient.ReadUsage(zero.RootElement)!;
        Assert.NotNull(zeroUsage);
        Assert.True(zeroUsage.PromptTokensReported && zeroUsage.CompletionTokensReported && zeroUsage.CachedPromptTokensReported);
        Assert.Equal(0, zeroUsage.TotalTokens);

        using var partial = JsonDocument.Parse("""{"usage":{"prompt_tokens":null,"completion_tokens":3,"prompt_tokens_details":{"cached_tokens":null}}}""");
        var partialUsage = OpenAiCompatibleClient.ReadUsage(partial.RootElement)!;
        Assert.False(partialUsage.PromptTokensReported || partialUsage.CachedPromptTokensReported);
        Assert.True(partialUsage.CompletionTokensReported);
        Assert.Equal(3, partialUsage.CompletionTokens);

        using var invalid = JsonDocument.Parse("""{"usage":{"prompt_tokens":"42","completion_tokens":-1}}""");
        Assert.Null(OpenAiCompatibleClient.ReadUsage(invalid.RootElement));
    }
}
