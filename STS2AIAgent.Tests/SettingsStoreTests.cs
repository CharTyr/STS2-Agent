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
        settings.Models[0].ThinkingIntensity = "high";
        settings.PlayModelId = settings.Models[0].Id;
        settings.OverlayLeft = 48;
        settings.OverlayTop = 96;
        settings.AttachStateInChat = false;
        settings.AttachScreenshotInChat = true;
        settings.McpServerPath = @"C:\mods\mcp_server";
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal("sk-test", loaded.Endpoints[0].ApiKey);
        Assert.True(loaded.Models[0].SupportsVision);
        Assert.Equal("high", loaded.Models[0].ThinkingIntensity);
        Assert.Equal(settings.Models[0].Id, loaded.PlayModelId);
        Assert.Equal(settings.ConversationModelId, loaded.ConversationModelId);
        Assert.Equal(48f, loaded.OverlayLeft);
        Assert.Equal(96f, loaded.OverlayTop);
        Assert.False(loaded.AttachStateInChat);
        Assert.True(loaded.AttachScreenshotInChat);
        Assert.Equal(@"C:\mods\mcp_server", loaded.McpServerPath);
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

    public static void Load_MigratesGlobalThinkingIntensityOntoModels()
    {
        var path = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "legacy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "endpoints": [{ "id": "e1", "name": "ep", "baseUrl": "https://api.openai.com/v1", "apiKey": "", "enabled": true }],
          "models": [{ "id": "m1", "endpointId": "e1", "model": "gpt-4o", "displayName": "gpt-4o", "supportsTools": true, "thinkingMode": "auto" }],
          "conversationModelId": "m1",
          "thinkingIntensity": "high"
        }
        """);
        var loaded = new SettingsStore(path).Load();
        Assert.Equal("high", loaded.Models[0].ThinkingIntensity);
    }
}
