using System.Diagnostics;

namespace STS2AIAgent.Multiplayer;

/// <summary>
/// The companion's link to the host process that launched it. Nothing else ended the AI teammate
/// when the host went away: closing, crashing or force-killing the host window left the second game
/// running with no UI (companions skip the overlay), its own API port, and -- with auto-play on --
/// its own LLM loop still spending the player's budget. The launcher hands the host PID over in
/// <see cref="EnvironmentName"/>; the companion polls it and quits once the host is gone. A watchdog
/// on the companion side also covers the host dying without running any managed shutdown hook.
/// </summary>
internal static class CompanionHostWatch
{
    public const string EnvironmentName = "STS2_AGENT_HOST_PID";

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <summary>Reads the host PID from the environment value; false when absent or malformed.</summary>
    public static bool TryReadHostPid(string? raw, out int pid)
    {
        pid = 0;
        return int.TryParse(raw?.Trim(), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out pid)
            && pid > 0;
    }

    /// <summary>The host's start time, captured once so a recycled PID is not mistaken for it.</summary>
    public static DateTime? TryReadStartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True once the host process is gone: no process with that id, it has exited, or the id now
    /// belongs to a process started after the host we were launched by.
    /// </summary>
    public static bool IsHostGone(int pid, DateTime? hostStartTime)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return true;
            }

            return IsDifferentProcess(hostStartTime, process.StartTime);
        }
        catch (ArgumentException)
        {
            // No process with this id.
            return true;
        }
        catch
        {
            // Access denied or a racing exit: not proof the host is gone.
            return false;
        }
    }

    /// <summary>A process whose start time differs from the recorded host's is a recycled PID.</summary>
    public static bool IsDifferentProcess(DateTime? hostStartTime, DateTime currentStartTime)
    {
        return hostStartTime is { } known && (currentStartTime - known).Duration() > TimeSpan.FromSeconds(2);
    }
}
