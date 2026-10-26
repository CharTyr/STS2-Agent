using System.Security.Cryptography;
using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

/// <summary>Normalizes both release layouts and upgrades only an unchanged, managed companion copy.</summary>
internal static class CompanionModStager
{
    internal static readonly string[] Files = { "STS2AIAgent.dll", "STS2AIAgent.pck", "STS2AIAgent.json" };
    internal const string Marker = ".sts2-companion-managed.json";

    public static string Prepare(string sourceDirectory, string gameDirectory)
    {
        var source = Artifacts(sourceDirectory);
        var mods = Path.Combine(gameDirectory, "mods");
        var nested = Path.Combine(mods, "STS2AIAgent");
        var flat = File.Exists(Path.Combine(mods, Files[0]));
        if (flat && Directory.Exists(nested))
            throw new IOException(Loc.T("发现两份本地模组，请只保留一份完整安装：{0}", mods));
        var destination = flat ? mods : nested;
        var replacing = flat || Directory.Exists(nested);
        if (replacing)
        {
            var current = Artifacts(destination);
            if (Files.All(file => Digest(source[file]) == Digest(current[file]))) return destination;
            if (flat || !IsUnchangedManagedCopy(destination, current))
                throw new IOException(Loc.T("本地模组与主窗口版本不同，文件已保留。请更新或移走这份手动安装：{0}", destination));
        }

        Directory.CreateDirectory(mods);
        // Siblings of mods, on the same volume: neither staging nor rollback is another loadable mod.
        var staging = Path.Combine(gameDirectory, ".sts2-companion-stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(gameDirectory, ".sts2-companion-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in Files) File.Copy(source[file], Path.Combine(staging, file));
            File.WriteAllText(Path.Combine(staging, Marker), JsonSerializer.Serialize(
                Files.ToDictionary(file => file, file => Digest(Path.Combine(staging, file)))));
            if (replacing) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }
            // A cleanup failure must not turn a successfully installed copy into a launch failure.
            try { if (Directory.Exists(backup)) Directory.Delete(backup, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static Dictionary<string, string> Artifacts(string directory)
    {
        var files = Files.ToDictionary(file => file, file => Path.Combine(directory, file));
        if (!File.Exists(files[Files[2]])) files[Files[2]] = Path.Combine(directory, "mod_id.json");
        foreach (var path in files.Values)
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException(Loc.T("模组文件缺失或为空：{0}。请重新安装完整发布包。", path));
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(files[Files[2]]));
            if (manifest.RootElement.GetProperty("id").GetString() != "STS2AIAgent")
                throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new IOException(Loc.T("模组清单不属于 STS2 AI Agent 或已损坏：{0}", files[Files[2]]));
        }
        return files;
    }

    private static bool IsUnchangedManagedCopy(string directory, Dictionary<string, string> files)
    {
        try
        {
            var path = Path.Combine(directory, Marker);
            if (new FileInfo(path).Length > 4096) return false;
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return hashes?.Count == Files.Length && Files.All(file => hashes.TryGetValue(file, out var hash) && hash == Digest(files[file]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
