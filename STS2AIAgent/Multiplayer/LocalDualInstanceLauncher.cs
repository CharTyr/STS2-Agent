using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Helpers;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
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
    private static string? _startupPath;
    private static string? _sessionToken;
    public static CompanionConnection? Connection { get; private set; }

    public static bool CompanionProcessAlive => _companionProcess is { HasExited: false };

    public static bool CompanionProcessExited => _companionProcess is { HasExited: true };

    /// <summary>
    /// Best-effort, non-blocking close of the companion window when the host shuts down normally.
    /// The companion's own host watchdog is the backstop for crashes and force-kills.
    /// </summary>
    public static void TryCloseCompanion()
    {
        try
        {
            if (_companionProcess is { HasExited: false } process)
            {
                process.CloseMainWindow();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Could not close the companion window: {ex.Message}");
        }
    }

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

        var configured = Environment.GetEnvironmentVariable("STS2_GAME_EXE");
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
    }

    internal static void StageWorkshopModForOfflineCompanion(string exePath)
    {
        var source = Path.GetDirectoryName(typeof(LocalDualInstanceLauncher).Assembly.Location)
            ?? throw new IOException("Cannot locate the loaded STS2AIAgent assembly.");
        var game = Path.GetDirectoryName(exePath)
            ?? throw new IOException("Cannot locate the game directory.");
        var staged = CompanionModStager.Prepare(source, game);
        Log.Info($"{LogPrefix} Verified companion mod at {staged}");
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

    /// <summary>
    /// <paramref name="companionAutoPlay"/> is written to the child as <c>STS2_AGENT_AUTOPLAY</c>.
    /// False is the external-takeover route: the companion joins the run and then waits, because
    /// nothing in that route calls the play model.
    /// </summary>
    public static async Task<DualLaunchResult> LaunchCompanionAsync(CancellationToken cancellationToken, bool companionAutoPlay = true)
    {
        if (!await LaunchGate.WaitAsync(0, cancellationToken))
        {
            return new DualLaunchResult { Ok = false, Message = Loc.T("正在邀请 AI 队友，请等待连接结果。") };
        }

        try
        {
            if (_companionProcess is { HasExited: false } existing)
            {
                if (Connection == null && _sessionToken != null)
                {
                    var port = await WaitForHealthAsync(HttpServer.Instance.Port < 65535 ? HttpServer.Instance.Port + 1 : 8081,
                        existing, TimeSpan.FromSeconds(10), cancellationToken);
                    if (port != null)
                    {
                        Connection = new CompanionConnection(port.Value, existing.Id, _sessionToken);
                        return new DualLaunchResult { Ok = true, CompanionPid = existing.Id, CompanionPort = port.Value,
                            Message = Loc.T("第二实例已就绪：PID {0}，API {1}", existing.Id, port.Value) };
                    }
                }
                return new DualLaunchResult
                {
                    Ok = false,
                    CompanionPid = _companionProcess.Id,
                    Message = Loc.T("AI 队友窗口已经在运行。请查看该窗口；若要重新组队，请先正常关闭它。")
                };
            }

            _companionProcess?.Dispose();
            _companionProcess = null;
            if (_startupPath != null) { try { File.Delete(_startupPath); } catch (IOException) { } }
            _startupPath = null;
            _sessionToken = null;
            Connection = null;
            return await LaunchCoreAsync(cancellationToken, companionAutoPlay);
        }
        finally
        {
            LaunchGate.Release();
        }
    }

    private static async Task<DualLaunchResult> LaunchCoreAsync(CancellationToken cancellationToken, bool companionAutoPlay)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exe = ResolveGameExe();
        if (exe == null)
        {
            return new DualLaunchResult { Ok = false, Message = Loc.T("找不到游戏可执行文件。") };
        }

        try
        {
            StageWorkshopModForOfflineCompanion(exe);
        }
        catch (Exception ex)
        {
            return new DualLaunchResult { Ok = false, Message = "Companion mod preparation failed: " + ex.Message };
        }

        var hostPort = HttpServer.Instance.Port;
        var companionPort = hostPort is > 0 and < 65535 ? hostPort + 1 : 8081;

        var hostClientId = CommandLineHelper.GetValue("clientId");
        if (!CoopLaunchPolicy.TryGetCompanionArguments(
                CommandLineHelper.GetValue("force-steam"),
                hostClientId,
                out var companionArguments,
                out var argumentError))
        {
            return new DualLaunchResult
            {
                Ok = false,
                Message = Loc.T("无法计算队友启动参数：{0}", argumentError)
            };
        }

        var companionClientId = CoopLaunchPolicy.ResolveCompanionClientId(hostClientId);
        try
        {
            CompanionProfileBootstrap.WriteCompanionSave(
                CompanionProfileBootstrap.DefaultUserRoot(),
                companionClientId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} companion profile bootstrap: {ex.Message}");
        }

        string companionSettingsPath;
        try
        {
            var mainPath = SettingsStore.DefaultPath();
            var explicitCompanion = System.Environment.GetEnvironmentVariable("STS2_COMPANION_SETTINGS_PATH");
            companionSettingsPath = CoopLaunchPolicy.CompanionSettingsPath(mainPath, explicitCompanion);
            CoopLaunchPolicy.SeedCompanionSettings(mainPath, companionSettingsPath);
        }
        catch (Exception ex)
        {
            return new DualLaunchResult
            {
                Ok = false,
                Message = Loc.T("无法配置队友设置文件路径：{0}", ex.Message)
            };
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
            Arguments = companionArguments,
            UseShellExecute = false
        };

        startInfo.Environment["STS2_API_PORT"] = companionPort.ToString();
        startInfo.Environment["STS2_API_ALLOW_FALLBACK"] = "1";
        startInfo.Environment["STS2_AGENT_ROLE"] = InstanceRole.Companion;
        startInfo.Environment["STS2_MULTIPLAYER_HOST_IP"] = "127.0.0.1";
        startInfo.Environment["STS2_MULTIPLAYER_NET_ID"] = companionClientId
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["STS2_AGENT_AUTOPLAY"] = companionAutoPlay ? "1" : "0";
        startInfo.Environment["STS2_AGENT_SETTINGS_PATH"] = companionSettingsPath;
        // The companion quits by itself once this host is gone (see CompanionHostWatch), so closing
        // or crashing the host never leaves an invisible AI teammate running and spending.
        startInfo.Environment[CompanionHostWatch.EnvironmentName] = System.Environment.ProcessId
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sessionToken = CompanionConnection.CreateToken();
        startInfo.Environment[CompanionConnection.TokenEnvironment] = sessionToken;
        _sessionToken = sessionToken;
        _startupPath = Path.Combine(Path.GetTempPath(), "sts2-companion-startup-" + Guid.NewGuid().ToString("N") + ".json");
        startInfo.Environment[CompanionStartupFile.EnvironmentName] = _startupPath;

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
                Message = Loc.T("启动第二实例失败。Steam 可能阻止了双开：{0}", ex.Message)
            };
        }

        // Retain the process handle after timeout/cancellation too. Retrying the
        // invite must not start a third game while this child is still alive.
        _companionProcess = process;
        var readyPort = await WaitForHealthAsync(companionPort, process, TimeSpan.FromSeconds(90), cancellationToken);
        if (readyPort == null)
        {
            Connection = null;
            return new DualLaunchResult
            {
                Ok = false,
                CompanionPort = companionPort,
                CompanionPid = process.Id,
                Message = process.HasExited
                    ? Loc.T("AI 队友进程已退出（退出码 {0}）。请检查游戏日志与 Steam 双开限制后重试。", process.ExitCode)
                    : Loc.T("AI 队友进程仍在运行（PID {0}），但未能确认连接。请检查队友窗口和游戏日志，不要重复启动。", process.Id)
            };
        }

        Connection = new CompanionConnection(readyPort.Value, process.Id, sessionToken);
        return new DualLaunchResult
        {
            Ok = true,
            CompanionPort = readyPort.Value,
            CompanionPid = process.Id,
            Message = Loc.T("第二实例已就绪：PID {0}，API {1}", process.Id, readyPort.Value)
        };
    }

    private static async Task<int?> WaitForHealthAsync(int preferredPort, Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        IEnumerable<int> Candidates()
        {
            var startup = _startupPath == null ? null : CompanionStartupFile.Read(_startupPath, process.Id);
            if (startup?.Phase == "failed")
                throw new IOException("Companion initialization failed: " + startup.Error);
            if (startup?.Port is > 0 and <= 65535) yield return startup.Port.Value;
            foreach (var port in CompanionPortFile.EnumerateCandidates(preferredPort, process.Id)) yield return port;
        }
        return await CompanionDiscovery.WaitAsync(Candidates, async (port, cancellationToken) =>
        {
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/health", cancellationToken);
                return CompanionHealth.IsExpectedProcess(json, port, process.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
        }, () => process.HasExited, timeout, cancellationToken);
    }
}
