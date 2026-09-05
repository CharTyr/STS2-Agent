using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Helpers;
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
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);
    private static Process? _companionProcess;
    public static CompanionConnection? Connection { get; private set; }

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
        if (!await LaunchGate.WaitAsync(0, cancellationToken))
        {
            return new DualLaunchResult { Ok = false, Message = "正在邀请 AI 队友，请等待连接结果。" };
        }

        try
        {
            if (_companionProcess is { HasExited: false })
            {
                return new DualLaunchResult
                {
                    Ok = false,
                    CompanionPid = _companionProcess.Id,
                    Message = "AI 队友窗口已经在运行。请查看该窗口；若要重新组队，请先正常关闭它。"
                };
            }

            _companionProcess?.Dispose();
            _companionProcess = null;
            Connection = null;
            return await LaunchCoreAsync(cancellationToken);
        }
        finally
        {
            LaunchGate.Release();
        }
    }

    private static async Task<DualLaunchResult> LaunchCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            Arguments = CoopLaunchPolicy.CompanionArguments(CommandLineHelper.GetValue("force-steam"), CommandLineHelper.GetValue("clientId")),
            UseShellExecute = false
        };

        startInfo.Environment["STS2_API_PORT"] = companionPort.ToString();
        startInfo.Environment["STS2_AGENT_ROLE"] = InstanceRole.Companion;
        startInfo.Environment["STS2_MULTIPLAYER_HOST_IP"] = "127.0.0.1";
        startInfo.Environment["STS2_AGENT_AUTOPLAY"] = "1";
        var sessionToken = CompanionConnection.CreateToken();
        startInfo.Environment[CompanionConnection.TokenEnvironment] = sessionToken;

        Process process;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DualLaunchResult
            {
                Ok = false,
                Message = "启动第二实例失败。Steam 可能阻止了双开：" + ex.Message
            };
        }

        // Retain the process handle after timeout/cancellation too. Retrying the
        // invite must not start a third game while this child is still alive.
        _companionProcess = process;
        Connection = new CompanionConnection(companionPort, process.Id, sessionToken);
        var ready = await WaitForHealthAsync(companionPort, process, TimeSpan.FromSeconds(90), cancellationToken);
        if (!ready)
        {
            return new DualLaunchResult
            {
                Ok = false,
                CompanionPort = companionPort,
                CompanionPid = process.Id,
                Message = process.HasExited
                    ? $"AI 队友进程已退出（退出码 {process.ExitCode}）。请检查游戏日志与 Steam 双开限制后重试。"
                    : $"AI 队友进程仍在运行（PID {process.Id}），但未能确认连接。请检查队友窗口和游戏日志，不要重复启动。"
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

    private static async Task<bool> WaitForHealthAsync(int port, Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        var url = $"http://127.0.0.1:{port}/health";
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                var json = await http.GetStringAsync(url, cancellationToken);
                if (CompanionHealth.IsExpectedProcess(json, port, process.Id))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }

            await Task.Delay(1000, cancellationToken);
        }

        return false;
    }
}
