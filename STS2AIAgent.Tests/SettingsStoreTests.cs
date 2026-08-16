using STS2AIAgent.Config;

namespace STS2AIAgent.Tests;

internal static class SettingsStoreTests
{
    public static void RoundTrip_PreservesEndpointsModelsAndRoles()
    {
        var path = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "sk-test";
        settings.Models[0].SupportsVision = true;
        settings.ThinkingIntensity = "high";
        settings.PlayModelId = settings.Models[0].Id;
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal("sk-test", loaded.Endpoints[0].ApiKey);
        Assert.True(loaded.Models[0].SupportsVision);
        Assert.Equal("high", loaded.ThinkingIntensity);
        Assert.Equal(settings.Models[0].Id, loaded.PlayModelId);
        Assert.Equal(settings.ConversationModelId, loaded.ConversationModelId);
    }

    public static void Load_MissingFile_CreatesDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "missing.json");
        var store = new SettingsStore(path);
        var loaded = store.Load();
        Assert.NotEmpty(loaded.Endpoints);
        Assert.NotEmpty(loaded.Models);
        Assert.False(loaded.OverlayVisibleOnStart);
        Assert.True(File.Exists(path));
    }
}
