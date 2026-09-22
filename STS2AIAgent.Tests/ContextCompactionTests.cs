using STS2AIAgent.Agent;
using STS2AIAgent.Config;

namespace STS2AIAgent.Tests;

internal static class ContextCompactionTests
{
    public static void DefaultWindowIs256000AndUnsetMeansDefault()
    {
        Assert.Equal(256_000, ContextCompaction.DefaultContextWindow);
        Assert.Equal(256_000, ContextCompaction.ResolveWindow(new LlmModelConfig()));
        Assert.Equal(128_000, ContextCompaction.ResolveWindow(new LlmModelConfig { ContextWindow = 128_000 }));
    }

    public static void CompactionStartsAtEightyPercentOfTheModelWindow()
    {
        Assert.False(ContextCompaction.ShouldCompact(204_799, 256_000));
        Assert.True(ContextCompaction.ShouldCompact(204_800, 256_000));
        Assert.False(ContextCompaction.ShouldCompact(102_399, 128_000));
        Assert.True(ContextCompaction.ShouldCompact(102_400, 128_000));
    }

    public static void SummaryReplacesOlderDecisionsAndKeepsRecentOnes()
    {
        var decisions = Enumerable.Range(1, 12)
            .Select(id => new DecisionLogEntry(id, "t", "agent_loop", "play_card", "reason " + id, null, 1, 10, "run"))
            .ToArray();

        var summary = ContextCompaction.Format(new LlmModelConfig { ContextWindow = 1000 }, decisions, promptTokens: 900);
        Assert.NotNull(summary);
        Assert.Contains("1. play_card", summary!);
        Assert.Contains("6. play_card", summary);
        Assert.Contains("Recent decisions, kept verbatim:", summary);
        Assert.Contains("12. play_card", summary);
        Assert.Contains("never reuse an index", summary);
    }

    public static void BelowTheThresholdKeepsTheWholeHistory()
    {
        var decisions = Enumerable.Range(1, 12)
            .Select(id => new DecisionLogEntry(id, "t", "agent_loop", "play_card", "reason " + id, null, 1, 10, "run"))
            .ToArray();
        Assert.Null(ContextCompaction.Format(new LlmModelConfig(), decisions, promptTokens: 204_799));
    }

    public static void ShortHistoryIsNotSummarized()
    {
        var decisions = new[]
        {
            new DecisionLogEntry(1, "t", "agent_loop", "proceed", "go", null, 1, 10, "run")
        };
        Assert.Null(ContextCompaction.Format(new LlmModelConfig(), decisions, promptTokens: 300_000));
    }
}
