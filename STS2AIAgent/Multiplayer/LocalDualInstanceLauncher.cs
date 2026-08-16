using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Server;

namespace STS2AIAgent.Multiplayer;

internal sealed class DualLaunchResult
{
    public bool Ok { get; init; }

    public string Message { get; init; } = string.Empty;

    public int CompanionPort { get; init; }

    public int? CompanionPid { get; init; }
}

internal static class LocalDualInstanceLauncher
{
    private const string LogPrefix = "[STS2AIAgent.DualInstance]";
    private const string SteamAppId = "2868840";

    public static string? ResolveGameExe()
    {
        try
        {
            var exe = OS.GetExecutablePath();
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            {
                return exe;
            }
        }
        catch
        {
        }

        var steam = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            "Slay the Spire 2",
            "SlayTheSpire2.exe");
        return File.Exists(steam) ? steam : null;
    }

    public static void EnsureSteamAppIdFile(string exePath)
    {
        var directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var appIdPath = Path.Combine(directory, "steam_appid.txt");
        if (!File.Exists(appIdPath))
        {
            File.WriteAllText(appIdPath, SteamAppId);
            Log.Info($"{LogPrefix} Wrote {appIdPath}");
        }
    }

    public static async Task<DualLaunchResult> LaunchCompanionAsync(CancellationToken cancellationToken)
    {
        var exe = ResolveGameExe();
        if (exe == null)
        {
            return new DualLaunchResult { Ok = false, Message = "找不到游戏可执行文件。" };
        }

        int companionPort;
        try
        {
            companionPort = HttpServer.FindFreePort(HttpServer.Instance.Port + 1);
        }
        catch (Exception ex)
        {
            return new DualLaunchResult { Ok = false, Message = "找不到空闲 API 端口：" + ex.Message };
        }

        try
        {
            EnsureSteamAppIdFile(exe);
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} steam_appid.txt: {ex.Message}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? System.Environment.CurrentDirectory,
            Arguments = "--windowed",
            UseShellExecute = false
        };

        startInfo.Environment["STS2_API_PORT"] = companionPort.ToString();
        startInfo.Environment["STS2_AGENT_ROLE"] = InstanceRole.Companion;
        startInfo.Environment["STS2_MULTIPLAYER_HOST_IP"] = "127.0.0.1";
        startInfo.Environment["STS2_AGENT_AUTOPLAY"] = "1";

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new DualLaunchResult
            {
                Ok = false,
                Message = "启动第二实例失败。Steam 可能阻止了双开：" + ex.Message
            };
        }

        var ready = await WaitForHealthAsync(companionPort, TimeSpan.FromSeconds(90), cancellationToken);
        if (!ready)
        {
            return new DualLaunchResult
            {
                Ok = false,
                CompanionPort = companionPort,
                CompanionPid = process.Id,
                Message = $"第二实例已启动（PID {process.Id}，端口 {companionPort}），但 /health 尚未就绪。可能被 Steam 单实例锁挡住。"
            };
        }

        return new DualLaunchResult
        {
            Ok = true,
            CompanionPort = companionPort,
            CompanionPid = process.Id,
            Message = $"第二实例已就绪：PID {process.Id}，API {companionPort}"
        };
    }

    private static async Task<bool> WaitForHealthAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        var url = $"http://127.0.0.1:{port}/health";
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync(url, cancellationToken);
                if (json.Contains("\"status\"", StringComparison.Ordinal) &&
                    json.Contains("ready", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(1000, cancellationToken);
        }

        return false;
    }
}
