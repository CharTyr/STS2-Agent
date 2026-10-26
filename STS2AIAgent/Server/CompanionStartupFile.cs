using System.Text.Json;

namespace STS2AIAgent.Server;

/// <summary>A host-selected path shared with one child, independent of its TEMP directory.</summary>
internal static class CompanionStartupFile
{
    public const string EnvironmentName = "STS2_COMPANION_STARTUP_PATH";
    internal sealed record State(int Pid, string Phase, int? Port, string? Error);

    public static void Publish(string phase, int? port = null, string? error = null)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("STS2_AGENT_ROLE"), "companion", StringComparison.OrdinalIgnoreCase)) return;
        var path = Environment.GetEnvironmentVariable(EnvironmentName);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Write(path, new State(Environment.ProcessId, phase, port, error)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* HTTP discovery remains available. */ }
    }

    internal static void Write(string path, State state)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static State? Read(string path, int pid)
    {
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            return state?.Pid == pid ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
