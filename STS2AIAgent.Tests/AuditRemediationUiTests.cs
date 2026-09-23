using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Ui;

namespace STS2AIAgent.Tests;

internal static class AuditRemediationUiTests
{
    public static void ModeSwitchRequiresConfirmedPause()
    {
        Assert.False(PlayModeSwitchPolicy.ShouldCommit("solo", "coop", false));
        Assert.True(PlayModeSwitchPolicy.ShouldCommit("solo", "coop", true));
        Assert.False(PlayModeSwitchPolicy.ShouldCommit("coop", "coop", true));
        Assert.False(PlayModeSwitchPolicy.ShouldCommit("unknown", "solo", true));
    }

    public static void CompanionPatchOnlyChangesJevFields()
    {
        var original = AgentSettings.CreateDefault();
        original.McpEnabled = true;
        original.MaxSessionRequests = 4;
        original.ConversationModelId = "main";
        var next = SettingsClone.Clone(original);
        var patch = new CompanionSettingsPatch
        {
            DualLayerCoopEnabled = true,
            JevBaseUrl = "http://127.0.0.1:9999",
            JevApiKey = "private-test-value",
            JevModel = "jev-test",
            JevConfidenceThreshold = 0.4,
            JevRequestTimeoutSeconds = 9
        };
        Assert.True(patch.IsValid);
        patch.ApplyTo(next);
        Assert.True(next.DualLayerCoopEnabled);
        Assert.Equal("private-test-value", next.JevApiKey);
        Assert.Equal(original.McpEnabled, next.McpEnabled);
        Assert.Equal(original.MaxSessionRequests, next.MaxSessionRequests);
        Assert.Equal(original.ConversationModelId, next.ConversationModelId);
        Assert.True(patch.HasSameValues(CompanionSettingsPatch.From(next)));
        Assert.Equal(patch.Fingerprint(), CompanionSettingsPatch.From(next).Fingerprint());
    }

    public static void CompanionPatchRejectsUnsafeValuesBeforeMutation()
    {
        var original = AgentSettings.CreateDefault();
        var patch = new CompanionSettingsPatch
        {
            DualLayerCoopEnabled = true,
            JevBaseUrl = "url",
            JevModel = "jev-test",
            JevConfidenceThreshold = double.NaN,
            JevRequestTimeoutSeconds = -1
        };
        Assert.False(patch.IsValid);
        var rejected = false;
        try { patch.ApplyTo(original); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected);
        Assert.False(original.DualLayerCoopEnabled);
    }

    public static void CompanionStatusDoesNotReturnKey()
    {
        var status = new CompanionJevSnapshot
        {
            Choice = "play_card",
            Probabilities = "a 50%",
            Danger = "0.3",
            Latency = "120 ms",
            DualLayerCoopEnabled = true
        };
        var json = JsonSerializer.Serialize(status);
        Assert.True(json.Contains("\"dual_layer_coop_enabled\":true", StringComparison.Ordinal));
        Assert.False(json.Contains("jev_api_key", StringComparison.Ordinal));
        Assert.Equal("play_card", JsonSerializer.Deserialize<CompanionJevSnapshot>(json)?.Choice);
    }
}
