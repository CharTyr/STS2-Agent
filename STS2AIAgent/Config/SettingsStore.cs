using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2AIAgent.Config;

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
                    return created;
                }

                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions) ?? AgentSettings.CreateDefault();
                loaded.EnsureValidShape();
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                var fallback = AgentSettings.CreateDefault();
                fallback.EnsureValidShape();
                return fallback;
            }
        }
    }

    public void Save(AgentSettings settings)
    {
        lock (_gate)
        {
            settings.EnsureValidShape();
            WriteUnlocked(settings);
        }
    }

    private void WriteUnlocked(AgentSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, _path, overwrite: true);
        File.Delete(tempPath);
    }
}
