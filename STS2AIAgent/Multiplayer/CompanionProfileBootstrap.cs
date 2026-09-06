using System.Text.Json;
using System.Text.Json.Nodes;

namespace STS2AIAgent.Multiplayer;

internal static class CompanionProfileBootstrap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string EnsureModsAgreed(string? existingJson)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existingJson)
                ? new JsonObject()
                : JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        if (root["schema_version"] is null)
        {
            root["schema_version"] = 8;
        }

        var modSettings = root["mod_settings"] as JsonObject ?? new JsonObject();
        modSettings["mods_enabled"] = true;

        var list = modSettings["mod_list"] as JsonArray ?? new JsonArray();
        var found = false;
        foreach (var item in list)
        {
            if (item is JsonObject obj &&
                string.Equals(obj["id"]?.GetValue<string>(), "STS2AIAgent", StringComparison.OrdinalIgnoreCase))
            {
                obj["is_enabled"] = true;
                if (obj["source"] is null)
                {
                    obj["source"] = "mods_directory";
                }

                found = true;
            }
        }

        if (!found)
        {
            list.Add(new JsonObject
            {
                ["id"] = "STS2AIAgent",
                ["is_enabled"] = true,
                ["source"] = "mods_directory"
            });
        }

        modSettings["mod_list"] = list;
        root["mod_settings"] = modSettings;
        root["seen_ea_disclaimer"] = true;
        root["skip_intro_logo"] = true;
        return root.ToJsonString(JsonOptions);
    }

    public static string CompanionSavePath(string userRoot, string clientId)
    {
        return Path.Combine(userRoot, "default", clientId, "settings.save");
    }

    public static string? FindSteamSettingsPath(string userRoot)
    {
        var steamRoot = Path.Combine(userRoot, "steam");
        if (!Directory.Exists(steamRoot))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(steamRoot, "settings.save", SearchOption.AllDirectories))
        {
            return file;
        }

        return null;
    }

    public static string WriteCompanionSave(string userRoot, string clientId)
    {
        var dest = CompanionSavePath(userRoot, clientId);
        var directory = Path.GetDirectoryName(dest);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string? existing = null;
        if (File.Exists(dest))
        {
            existing = File.ReadAllText(dest);
        }
        else
        {
            var steam = FindSteamSettingsPath(userRoot);
            if (steam != null && File.Exists(steam))
            {
                existing = File.ReadAllText(steam);
            }
        }

        var json = EnsureModsAgreed(existing);
        File.WriteAllText(dest, json);
        return dest;
    }

    public static string DefaultUserRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "SlayTheSpire2");
    }
}
