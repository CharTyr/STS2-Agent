using STS2AIAgent.Agent;
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
        settings.McpEnabled = true;
        settings.HasSeenFirstRunGuide = true;
        settings.ProactiveChatEnabled = true;
        settings.ProactiveChatTone = ProactiveChatTones.Terse;
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!));
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
        Assert.True(loaded.McpEnabled);
        Assert.True(loaded.HasSeenFirstRunGuide);
        Assert.Equal("verified", ModelRoleProbe.Current(loaded, ModelRoleNames.Play).Status);
        Assert.True(loaded.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Terse, loaded.ProactiveChatTone);
    }


    public static void Load_VerifiedPlayFingerprint_IsCaseInsensitive()
    {
        var path = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].BaseUrl = "https://api.example.com/v1";
        settings.Endpoints[0].ApiKey = "sk-test";
        settings.PlayModelId = settings.Models[0].Id;
        var resolved = settings.TryResolvePlayModel()!;
        var record = ModelRoleProbe.FromSuccess(ModelRoleNames.Play, resolved);
        record.Fingerprint = record.Fingerprint!.ToLowerInvariant();
        ModelRoleProbe.Upsert(settings, record);
        new SettingsStore(path).Save(settings);

        var loaded = new SettingsStore(path).Load();
        Assert.Equal("verified", ModelRoleProbe.Current(loaded, ModelRoleNames.Play).Status);
        Assert.True(FirstRunSetup.Evaluate(loaded).ReadyToInvite);
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


    public static void ProactiveChat_LegacyFileLoadsDisabledWithDefaultTone()
    {
        // A settings file written before the feature existed has no proactive keys. Loading it
        // must not enable proactive speech, and an unknown stored tone must fall back.
        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "endpoints": [{ "id": "e1", "name": "Local", "baseUrl": "http://localhost:1234/v1", "apiKey": "", "enabled": true }],
          "models": [{ "id": "m1", "endpointId": "e1", "model": "gpt-4o", "displayName": "gpt-4o", "supportsVision": false, "supportsTools": true, "thinkingMode": "auto", "thinkingIntensity": "medium" }],
          "conversationModelId": "m1",
          "attachStateInChat": true,
          "maxSessionRequests": 12
        }
        """);

        var loaded = new SettingsStore(path).Load();

        Assert.False(loaded.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Default, loaded.ProactiveChatTone);
        // The pre-existing values are untouched by the new fields.
        Assert.Equal(12, loaded.MaxSessionRequests);
        Assert.Equal("m1", loaded.ConversationModelId);
    }

    public static void ProactiveChat_UnknownStoredToneIsRepaired()


    {
        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = AgentSettings.CreateDefault();
        settings.ProactiveChatEnabled = true;
        settings.ProactiveChatTone = "dramatic";
        new SettingsStore(path).Save(settings);

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(ProactiveChatTones.Default, loaded.ProactiveChatTone);
        Assert.True(loaded.ProactiveChatEnabled);
    }

    public static void Load_CorruptJson_BacksUpOriginalAndDoesNotLoseSecret()
    {
        var path = NewSettingsPath();
        const string secret = "sk-test-preserve";
        var original = "broken-json apiKey=" + secret;
        File.WriteAllText(path, original);
        var store = new SettingsStore(path);
        var loaded = store.Load();
        store.Save(loaded);

        var backups = CorruptBackups(path);
        Assert.True(backups.Length > 0, "expected a lossless corrupt backup before overwrite");
        Assert.True(File.ReadAllText(backups[0]).Contains(secret));
        Assert.False((store.LastNotice.Message ?? "").Contains(secret));
        Assert.False((store.LastNotice.BackupPath ?? "").Contains(secret));
        Assert.False((store.LastNotice.BackupPath ?? "").Contains(Path.DirectorySeparatorChar));
        Assert.False((store.LastNotice.BackupPath ?? "").Contains(Path.AltDirectorySeparatorChar));
    }

    public static void Load_CorruptJson_RestoresLastGoodBackup()
    {
        var path = NewSettingsPath();
        var store = new SettingsStore(path);
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "sk-test-restore";
        settings.MaxSessionTokens = 4000;
        store.Save(settings);
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, "{ this is not json, apiKey: sk-test-restore }");

        var restoredStore = new SettingsStore(path);
        var loaded = restoredStore.Load();
        Assert.Equal("sk-test-restore", loaded.Endpoints[0].ApiKey);
        Assert.Equal(4000, loaded.MaxSessionTokens);
        Assert.Equal("restored", restoredStore.LastNotice.Kind);
    }

    public static void Save_UnwritableTemp_LeavesOriginalIntact()
    {
        var path = NewSettingsPath();
        var store = new SettingsStore(path);
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "sk-test-keep";
        store.Save(settings);
        var original = File.ReadAllText(path);
        var tempDir = path + ".tmp";
        Directory.CreateDirectory(tempDir);
        try
        {
            var threw = false;
            try
            {
                settings.Endpoints[0].ApiKey = "sk-test-changed";
                store.Save(settings);
            }
            catch (Exception)
            {
                threw = true;
            }

            Assert.True(threw, "save should fail when temp path is not writable");
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public static void Load_CorruptJson_NoticeDoesNotContainFileBody()
    {
        var path = NewSettingsPath();
        const string secret = "sk-test-notice";
        File.WriteAllText(path, "broken-json apiKey=" + secret);
        var store = new SettingsStore(path);
        store.Load();
        var notice = store.LastNotice;
        Assert.True(notice.HasMessage);
        Assert.False((notice.Message + " " + (notice.BackupPath ?? "")).Contains(secret));
        Assert.False(notice.Message.Contains("{"));
    }

    private static string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string[] CorruptBackups(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        return Directory.GetFiles(directory, name + ".corrupt-*");
    }

    public static void Clone_CarriesEveryEditableField()
    {
        // The overlay edits a clone, so a field missing here is dropped on the next save.
        var source = AgentSettings.CreateDefault();
        source.ProactiveChatEnabled = true;
        source.ProactiveChatTone = ProactiveChatTones.Terse;
        source.AttachScreenshotInChat = true;
        source.McpEnabled = true;
        source.McpPort = 9001;
        source.MaxSessionTokens = 1234;
        source.MaxSessionRequests = 7;
        source.Hotkey = "F9";
        source.OverlayLeft = 12;
        source.OverlayTop = 34;
        source.Endpoints[0].ApiKey = "sk-clone";
        source.CompanionAutoSelectCharacter = false;

        var clone = SettingsClone.Clone(source);

        Assert.True(clone.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Terse, clone.ProactiveChatTone);
        Assert.True(clone.AttachScreenshotInChat);
        Assert.True(clone.McpEnabled);
        Assert.Equal(9001, clone.McpPort);
        Assert.Equal(1234, clone.MaxSessionTokens);
        Assert.Equal(7, clone.MaxSessionRequests);
        Assert.Equal("F9", clone.Hotkey);
        Assert.Equal(12f, clone.OverlayLeft);
        Assert.Equal(34f, clone.OverlayTop);
        Assert.Equal("sk-clone", clone.Endpoints[0].ApiKey);
        Assert.False(clone.CompanionAutoSelectCharacter);
        Assert.Equal(source.ConversationModelId, clone.ConversationModelId);
    }

    public static void Clone_IsDeep()
    {
        var source = AgentSettings.CreateDefault();
        var clone = SettingsClone.Clone(source);
        clone.Endpoints[0].Name = "changed";
        clone.Models[0].Model = "changed";
        clone.ProactiveChatEnabled = true;
        Assert.True(!string.Equals(source.Endpoints[0].Name, "changed", StringComparison.Ordinal));
        Assert.True(!string.Equals(source.Models[0].Model, "changed", StringComparison.Ordinal));
        Assert.False(source.ProactiveChatEnabled);
    }
}

