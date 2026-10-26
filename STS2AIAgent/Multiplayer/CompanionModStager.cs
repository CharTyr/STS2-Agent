using System.Security.Cryptography;

namespace STS2AIAgent.Multiplayer;

/// <summary>Prepares the exact loaded mod for the Steam-off child without replacing user files.</summary>
internal static class CompanionModStager
{
    internal static readonly string[] Files = { "STS2AIAgent.dll", "STS2AIAgent.pck", "STS2AIAgent.json" };

    public static string Prepare(string sourceDirectory, string gameDirectory)
    {
        RequireComplete(sourceDirectory);
        var mods = Path.Combine(gameDirectory, "mods");
        var nested = Path.Combine(mods, "STS2AIAgent");
        var flatExists = File.Exists(Path.Combine(mods, Files[0]));
        if (flatExists && Directory.Exists(nested))
            throw new IOException("Both flat and nested STS2AIAgent installations exist. Keep one complete copy matching the host before inviting.");
        var destination = flatExists ? mods : nested;
        if (flatExists || Directory.Exists(nested))
        {
            RequireComplete(destination);
            foreach (var file in Files)
            {
                using var source = File.OpenRead(Path.Combine(sourceDirectory, file));
                using var target = File.OpenRead(Path.Combine(destination, file));
                if (!SHA256.HashData(source).SequenceEqual(SHA256.HashData(target)))
                    throw new IOException($"Companion mod differs from the host: {Path.Combine(destination, file)}. Update the complete local mod or move it aside; no files were overwritten.");
            }
            return destination;
        }

        Directory.CreateDirectory(mods);
        var staging = Path.Combine(mods, ".sts2-companion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in Files)
                File.Copy(Path.Combine(sourceDirectory, file), Path.Combine(staging, file));
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void RequireComplete(string directory)
    {
        foreach (var file in Files)
            if (!File.Exists(Path.Combine(directory, file)) || new FileInfo(Path.Combine(directory, file)).Length == 0)
                throw new IOException($"Companion needs a complete host mod; missing or empty file: {Path.Combine(directory, file)}");
    }
}
