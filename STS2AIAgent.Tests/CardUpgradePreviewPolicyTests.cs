using System.Text.Json;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class CardUpgradePreviewPolicyTests
{
    public static void MaxLevelSkipsGeneratingAnUpgrade()
    {
        var generated = false;
        var result = CardUpgradePreviewPolicy.Build(() => true, () => { generated = true; return new object(); });
        Assert.Null(result);
        Assert.False(generated);
    }

    public static void GenerationFailureIsNotReportedAsMaxLevel()
    {
        var result = CardUpgradePreviewPolicy.Build(() => false,
            () => throw new InvalidOperationException("private failure details"));
        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("unavailable", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("upgrade_preview_failed", doc.RootElement.GetProperty("reason").GetString());
        Assert.False(doc.RootElement.GetRawText().Contains("private failure details"));
    }

    public static void LevelLookupFailureIsAlsoUnavailable()
    {
        var generated = false;
        var result = CardUpgradePreviewPolicy.Build(
            () => throw new InvalidOperationException("max level lookup failed"),
            () => { generated = true; return new object(); });
        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("unavailable", doc.RootElement.GetProperty("status").GetString());
        Assert.False(generated);
    }

    public static void AvailableResultPreservesTheGeneratedMetadata()
    {
        var metadata = new { status = "available", cost = 3, description = "Gain 4 Strength.", vars = new[] { new { name = "StrengthPower", current_value = 4 } } };
        var result = CardUpgradePreviewPolicy.Build(() => false, () => metadata);
        Assert.True(ReferenceEquals(metadata, result));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("available", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("cost").GetInt32());
    }
}
