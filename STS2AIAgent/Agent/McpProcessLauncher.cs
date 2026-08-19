using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace STS2AIAgent.Agent;

internal sealed class McpLaunchResult
{
    public bool Ok { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Port { get; init; }

    public string? Url { get; init; }

    public Process? Process { get; init; }
}

internal static class McpProcessLauncher
{
    public const int DefaultPort = 8765;

    public static bool IsMcpRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "pyproject.toml")) &&
               File.Exists(Path.Combine(path, "src", "sts2_mcp", "server.py"));
    }

    public static string? FindMcpRoot(string? configuredPath)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            if (IsMcpRoot(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            var nested = Path.Combine(candidate, "mcp_server");
            if (IsMcpRoot(nested))
            {
                return Path.GetFullPath(nested);
            }
        }

        return null;
    }

    public static string? FindUv()
    {
        foreach (var name in new[] { "uv.exe", "uv" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath != null)
            {
                return fromPath;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, ".local", "bin", "uv.exe"),
                     Path.Combine(home, ".local", "bin", "uv"),
                     Path.Combine(home, ".cargo", "bin", "uv.exe"),
                     Path.Combine(local, "Programs", "uv", "uv.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static async Task<McpLaunchResult> StartAsync(
        string mcpRoot,
        string apiBaseUrl,
        int preferredPort,
        CancellationToken cancellationToken)
    {
        if (!IsMcpRoot(mcpRoot))
        {
            return new McpLaunchResult { Ok = false, Message = "mcp_server 目录无效。需要包含 pyproject.toml 和 src/sts2_mcp/server.py。" };
        }

        var uv = FindUv();
        if (uv == null)
        {
            return new McpLaunchResult
            {
                Ok = false,
                Message = "未找到 uv。请先安装 https://docs.astral.sh/uv/ 并确保 uv 在 PATH 中。"
            };
        }

        int port;
        try
        {
            port = FindFreePort(preferredPort > 0 ? preferredPort : DefaultPort);
        }
        catch (Exception ex)
        {
            return new McpLaunchResult { Ok = false, Message = "找不到空闲 MCP 端口：" + ex.Message };
        }

        var api = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://127.0.0.1:8080"
            : apiBaseUrl.Trim().TrimEnd('/');
        var start = new ProcessStartInfo
        {
            FileName = uv,
            Arguments =
                $"run sts2-network-mcp-server --host 127.0.0.1 --port {port} --path /mcp --api-base-url {api}",
            WorkingDirectory = mcpRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["STS2_API_BASE_URL"] = api;

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new McpLaunchResult { Ok = false, Message = "启动 MCP 失败：" + ex.Message };
        }

        var ready = await WaitForHealthAsync(port, TimeSpan.FromSeconds(40), cancellationToken);
        if (!ready)
        {
            var stderr = SafeReadTail(process);
            TryStop(process);
            return new McpLaunchResult
            {
                Ok = false,
                Port = port,
                Message = string.IsNullOrWhiteSpace(stderr)
                    ? $"MCP 进程已启动但 http://127.0.0.1:{port}/healthz 未就绪。请确认已 uv sync。"
                    : "MCP 启动失败：" + stderr
            };
        }

        return new McpLaunchResult
        {
            Ok = true,
            Port = port,
            Url = $"http://127.0.0.1:{port}/mcp",
            Process = process,
            Message = $"MCP 已启动：http://127.0.0.1:{port}/mcp"
        };
    }

    public static void TryStop(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    public static int FindFreePort(int startPort)
    {
        var port = startPort is > 0 and <= 65535 ? startPort : DefaultPort;
        for (var candidate = port; candidate <= Math.Min(65535, port + 32); candidate++)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, candidate);
                listener.Start();
                return candidate;
            }
            catch (SocketException)
            {
            }
            finally
            {
                listener?.Stop();
            }
        }

        throw new InvalidOperationException("No free MCP port in range.");
    }

    private static IEnumerable<string> EnumerateCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var env = Environment.GetEnvironmentVariable("STS2_MCP_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
        }

        foreach (var root in new[]
                 {
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var current = Path.GetFullPath(root);
            for (var i = 0; i < 6; i++)
            {
                yield return current;
                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private static async Task<bool> WaitForHealthAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        var url = $"http://127.0.0.1:{port}/healthz";
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync(url, cancellationToken);
                if (json.Contains("sts2-network-mcp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(400, cancellationToken);
        }

        return false;
    }

    private static string SafeReadTail(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                return string.Empty;
            }

            var text = process.StandardError.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = process.StandardOutput.ReadToEnd();
            }

            text = text.Trim();
            return text.Length <= 280 ? text : text[..280];
        }
        catch
        {
            return string.Empty;
        }
    }
}
