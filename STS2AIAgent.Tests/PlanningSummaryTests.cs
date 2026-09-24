using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The planner's state summary must stay a valid JSON document within a bounded size: the old
/// character cut produced text that could not be parsed at all, which is why plan scope was always
/// empty and the planner saw garbage. These tests pin the JSON-aware trimming.
/// </summary>
internal static class PlanningSummaryTests
{
    public static void UnderCapPassesThrough()
    {
        var summary = PlanningSummary.Trim("{\"screen\":\"COMBAT\",\"turn\":2}", 4000);
        Assert.Equal("{\"screen\":\"COMBAT\",\"turn\":2}", summary);
    }

    public static void OverCapStaysValidJson()
    {
        var deck = Enumerable.Range(0, 500).Select(i => $"\"CARD_{i}\"");
        var json = "{\"screen\":\"COMBAT\",\"turn\":3,\"run\":{\"deck\":[" + string.Join(",", deck) + "]}}";
        var summary = PlanningSummary.Trim(json, 2000);

        // The whole point: it parses, and the planner's own field names survive.
        using var document = JsonDocument.Parse(summary);
        Assert.Equal("COMBAT", document.RootElement.GetProperty("screen").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("turn").GetInt32());
        Assert.True(summary.Length <= 2000, $"summary is {summary.Length} characters over a 2000 cap");
    }

    public static void CombatFieldsSurviveTrimming()
    {
        var enemies = Enumerable.Range(0, 40).Select(i =>
            "{\"i\":" + i + ",\"name\":\"Slime " + i + "\",\"intents\":[{\"type\":\"attack\",\"damage\":9}]}");
        var glossary = string.Join(",", Enumerable.Range(0, 200).Select(i => "\"TERM_" + i + "\":\"definition text\""));
        var json = "{\"screen\":\"COMBAT\",\"turn\":1,\"combat\":{\"player\":{\"hp\":\"25/87\",\"energy\":2},\"enemies\":[" + string.Join(",", enemies) + "]},\"glossary\":{" + glossary + "}}";

        var summary = PlanningSummary.Trim(json, 3000);
        using var document = JsonDocument.Parse(summary);
        Assert.Equal("COMBAT", document.RootElement.GetProperty("screen").GetString());
        Assert.True(document.RootElement.GetProperty("combat").GetProperty("enemies").GetArrayLength() > 0);
        Assert.False(document.RootElement.TryGetProperty("glossary", out _), "the glossary is the first thing trimmed");
    }

    public static void NonJsonInputPassesThrough()
    {
        // A bridge that returned text instead of JSON is the caller's problem; the summary must not
        // fail it further. Truncate only when it is JSON.
        var summary = PlanningSummary.Trim("not json", 100);
        Assert.Equal("not json", summary);
    }

    /// <summary>
    /// An extreme frame (a huge fight at high resolution) can exceed the cap even after every trim:
    /// the answer must degrade to a minimal skeleton the planner can still steer from, never "{}".
    /// </summary>
    public static void ExtremeFrameDegradesToASkeletonNotEmpty()
    {
        var enemies = Enumerable.Range(0, 80).Select(i =>
            "{\"i\":" + i + ",\"name\":\"Slime King " + i + " of the Great Slime Dynasty\",\"intents\":[{\"type\":\"attack\",\"damage\":99,\"hits\":3,\"total_damage\":297}]}");
        var hand = Enumerable.Range(0, 40).Select(i =>
            "{\"i\":" + i + ",\"line\":\"A very verbose card description for card " + i + " costing much text\",\"playable\":true}");
        var json = "{\"screen\":\"COMBAT\",\"turn\":7,\"session\":{\"mode\":\"singleplayer\"},"
            + "\"combat\":{\"player\":{\"hp\":\"25/87\",\"block\":5,\"energy\":2},\"enemies\":[" + string.Join(",", enemies)
            + "],\"hand\":[" + string.Join(",", hand) + "]},\"glossary\":{\"A\":\"x\"}}";

        var summary = PlanningSummary.Trim(json, 500);
        using var document = JsonDocument.Parse(summary);
        Assert.Equal("COMBAT", document.RootElement.GetProperty("screen").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("turn").GetInt32());
        Assert.True(summary.Length <= 500, $"skeleton is {summary.Length} chars over cap 500");
        Assert.True(summary.Contains("player", StringComparison.Ordinal),
            "the skeleton keeps the player's hp/energy so the plan still has something to steer by");
        Assert.False(summary.Contains("Slime King", StringComparison.Ordinal),
            "the skeleton drops verbose enemy detail rather than blowing the cap");
    }
}
