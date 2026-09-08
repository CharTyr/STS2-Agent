using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2AIAgent.Config;

internal sealed record SettingsPersistenceNotice(string Kind, string Message, string? BackupPath = null, string? RestoredFrom = null)
{
    public static SettingsPersistenceNotice None { get; } = new("ok", "");
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public string Path => _path;

    public SettingsPersistenceNotice LastNotice { get; private set; } = SettingsPersistenceNotice.None;

    public static string DefaultPath()
    {
        var configured = Environment.GetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!System.IO.Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("STS2_AGENT_SETTINGS_PATH must be an absolute file path.");
            return System.IO.Path.GetFullPath(configured);
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sts2-ai-agent");
        }

        return System.IO.Path.Combine(root, "STS2AIAgent", "settings.json");
    }

    public AgentSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    var created = AgentSettings.CreateDefault();
                    WriteUnlocked(created);
                    LastNotice = SettingsPersistenceNotice.None;
                    return created;
                }

                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions);
                if (loaded == null)
                {
                    return RecoverFromLoadFailure();
                }

                loaded.EnsureValidShape();
                TryEnsureLastGoodBackup();
                LastNotice = SettingsPersistenceNotice.None;
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return RecoverFromLoadFailure();
            }
        }
    }

    public void Save(AgentSettings settings)
    {
        lock (_gate)
        {
            settings.EnsureValidShape();
            try
            {
                WriteUnlocked(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastNotice = new SettingsPersistenceNotice(
                    "save_failed",
                    "保存失败，原配置文件未被覆盖。");
                throw;
            }
        }
    }

    private AgentSettings RecoverFromLoadFailure()
    {
        var backupPath = TryBackupCorrupt();
        if (backupPath != null)
        {
            if (TryRestoreLastGood(out var restored))
            {
                LastNotice = new SettingsPersistenceNotice(
                    "restored",
                    "配置读取失败，已备份原文件并恢复上次成功保存的配置。",
                    backupPath,
                    LastGoodBackupPath());
                return restored;
            }

            var fallback = AgentSettings.CreateDefault();
            fallback.EnsureValidShape();
            WriteUnlocked(fallback);
            LastNotice = new SettingsPersistenceNotice(
                "fallback",
                "配置读取失败，已备份原文件并改用默认配置。可用备份恢复。",
                backupPath);
            return fallback;
        }

        LastNotice = new SettingsPersistenceNotice(
            "unrecovered",
            "配置读取失败，且未能备份原文件，未覆盖现有配置。");
        var memory = AgentSettings.CreateDefault();
        memory.EnsureValidShape();
        return memory;
    }

    private string? TryBackupCorrupt()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var dest = _path + ".corrupt-" + stamp;
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                dest = _path + ".corrupt-" + stamp + "-" + Guid.NewGuid().ToString("N")[..8];
            }

            File.Copy(_path, dest, overwrite: false);
            return System.IO.Path.GetFileName(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool TryRestoreLastGood(out AgentSettings settings)
    {
        settings = null!;
        var bak = LastGoodBackupPath();
        try
        {
            if (!File.Exists(bak))
            {
                return false;
            }

            var json = File.ReadAllText(bak);
            var loaded = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions);
            if (loaded == null)
            {
                return false;
            }

            loaded.EnsureValidShape();
            File.Copy(bak, _path, overwrite: true);
            settings = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryEnsureLastGoodBackup()
    {
        try
        {
            var bak = LastGoodBackupPath();
            if (!File.Exists(bak) && File.Exists(_path))
            {
                File.Copy(_path, bak, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string LastGoodBackupPath() => _path + ".bak";

    private void WriteUnlocked(AgentSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, LastGoodBackupPath());
            }
            else
            {
                File.Move(tempPath, _path);
                TryEnsureLastGoodBackup();
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }
}
