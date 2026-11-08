using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

/// <summary>
/// The prompt is ordered static-first so a provider's prefix cache can serve most of it; these pin
/// that the cache figure is read from both spellings providers use, survives accumulation, and is
/// shown only when reported (W6, 2026-10-28).
/// </summary>
internal static class LlmUsageCacheTests
{
    public static void BothCacheSpellingsAreRead()
    {
        using var openAi = JsonDocument.Parse("""{"usage":{"prompt_tokens":1000,"completion_tokens":20,"total_tokens":1020,"prompt_tokens_details":{"cached_tokens":800}}}""");
        Assert.Equal(800, OpenAiCompatibleClient.ReadUsage(openAi.RootElement)!.CachedPromptTokens);

        using var deepSeek = JsonDocument.Parse("""{"usage":{"prompt_tokens":1000,"completion_tokens":20,"total_tokens":1020,"prompt_cache_hit_tokens":640,"prompt_cache_miss_tokens":360}}""");
        Assert.Equal(640, OpenAiCompatibleClient.ReadUsage(deepSeek.RootElement)!.CachedPromptTokens);

        using var none = JsonDocument.Parse("""{"usage":{"prompt_tokens":1000,"completion_tokens":20,"total_tokens":1020}}""");
        Assert.Equal(0, OpenAiCompatibleClient.ReadUsage(none.RootElement)!.CachedPromptTokens);

        using var bogus = JsonDocument.Parse("""{"usage":{"prompt_tokens":100,"completion_tokens":1,"prompt_cache_hit_tokens":5000}}""");
        Assert.Equal(100, OpenAiCompatibleClient.ReadUsage(bogus.RootElement)!.CachedPromptTokens,
            "A cache figure larger than the prompt is clamped, never reported as a >100% hit.");
    }

    public static void CachedTokensAccumulateAndShowOnlyWhenReported()
    {
        var total = LlmUsage.Combine(
            new LlmUsage { PromptTokens = 1000, CompletionTokens = 10, TotalTokens = 1010, CachedPromptTokens = 900 },
            new LlmUsage { PromptTokens = 1000, CompletionTokens = 10, TotalTokens = 1010, CachedPromptTokens = 500 })!;
        Assert.Equal(1400, total.CachedPromptTokens);
        Assert.Equal(70, PlayerFacingSession.CacheHitPercent(total));
        Assert.Contains("70%", PlayerFacingSession.FormatUsage(true, total, 2));

        var uncached = new LlmUsage { PromptTokens = 1000, CompletionTokens = 10, TotalTokens = 1010 };
        Assert.False(PlayerFacingSession.FormatUsage(true, uncached, 1).Contains('%'),
            "A provider that does not report caching must not read as a 0% hit rate.");
    }
}
