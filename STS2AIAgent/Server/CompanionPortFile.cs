namespace STS2AIAgent.Server;

internal static class CompanionPortFile
{
    public static string PathFor(int pid) =>
        Path.Combine(Path.GetTempPath(), $"sts2-ai-agent-companion-{pid}.port");

    public static void Write(int pid, int port)
    {
        File.WriteAllText(PathFor(pid), port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static int? TryRead(int pid)
    {
        try
        {
            var text = File.ReadAllText(PathFor(pid)).Trim();
            return int.TryParse(text, out var port) && port is > 0 and <= 65535 ? port : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }

    public static void Delete(int pid)
    {
        try
        {
            File.Delete(PathFor(pid));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static IEnumerable<int> EnumerateCandidates(int preferredPort, int pid)
    {
        var seen = new HashSet<int>();
        if (preferredPort is > 0 and <= 65535 && seen.Add(preferredPort))
        {
            yield return preferredPort;
        }

        var published = TryRead(pid);
        if (published is > 0 and <= 65535 && seen.Add(published.Value))
        {
            yield return published.Value;
        }

        var nearby = Math.Min(LoopbackListener.NearbyPortCount, Math.Max(0, 65536 - preferredPort));
        for (var offset = 1; offset < nearby; offset++)
        {
            var port = preferredPort + offset;
            if (seen.Add(port))
            {
                yield return port;
            }
        }
    }
}
