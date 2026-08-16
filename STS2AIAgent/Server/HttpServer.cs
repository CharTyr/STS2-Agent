using System.Net;
using System.Net.Sockets;
using System.Threading;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Server;

public sealed class HttpServer
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 8080;
    private const int AutoIncrementSpan = 32;
    private const string LogPrefix = "[STS2AIAgent.HttpServer]";
    private const int StartRetryCount = 20;
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromMilliseconds(250);

    private static readonly Lazy<HttpServer> LazyInstance = new(() => new HttpServer());

    private readonly object _gate = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;

    public static HttpServer Instance => LazyInstance.Value;

    public string Host => DefaultHost;

    public int Port { get; private set; } = DefaultPort;

    public string Prefix { get; private set; } = $"http://{DefaultHost}:{DefaultPort}/";

    public bool PortWasAutoIncremented { get; private set; }

    private HttpServer()
    {
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_listener != null)
            {
                Log.Info($"{LogPrefix} Already started on {Prefix}");
                return;
            }

            var preferredPort = ResolvePreferredPort();
            var allowIncrement = !IsExplicitPortConfigured();
            var started = StartListener(preferredPort, allowIncrement);
            _listener = started.Listener;
            Port = started.Port;
            PortWasAutoIncremented = started.Port != preferredPort;
            Prefix = $"http://{DefaultHost}:{Port}/";

            _cts = new CancellationTokenSource();
            _listenLoopTask = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
            Log.Info($"{LogPrefix} Listening on {Prefix}");
        }
    }

    public static int FindFreePort(int startPort)
    {
        var port = startPort is > 0 and <= 65535 ? startPort : DefaultPort;
        for (var candidate = port; candidate <= Math.Min(65535, port + AutoIncrementSpan); candidate++)
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

        throw new InvalidOperationException($"No free loopback port found in {startPort}..{startPort + AutoIncrementSpan}.");
    }

    public void Stop()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        Task? listenLoopTask;

        lock (_gate)
        {
            if (_listener == null && _cts == null && _listenLoopTask == null)
            {
                return;
            }

            listener = _listener;
            cts = _cts;
            listenLoopTask = _listenLoopTask;
            _listener = null;
            _cts = null;
            _listenLoopTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Failed to cancel listener token: {ex}");
        }

        try
        {
            if (listener?.IsListening == true)
            {
                listener.Stop();
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            Log.Info($"{LogPrefix} Listener stop completed with shutdown exception: {ex.Message}");
        }

        try
        {
            listener?.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            Log.Info($"{LogPrefix} Listener close completed with shutdown exception: {ex.Message}");
        }

        try
        {
            listenLoopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException or HttpListenerException or ObjectDisposedException))
        {
            Log.Info($"{LogPrefix} Listener loop stopped during shutdown.");
        }
        finally
        {
            cts?.Dispose();
        }

        Log.Info($"{LogPrefix} Stopped");
    }

    private static async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;

            try
            {
                context = await listener.GetContextAsync();
                _ = Task.Run(() => Router.HandleAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix} Listener loop failed: {ex}");

                if (context != null)
                {
                    await Router.WriteErrorAsync(context.Response, 500, "listener_error", "HTTP listener failed.");
                }
            }
        }
    }

    private static (HttpListener Listener, int Port) StartListener(int preferredPort, bool allowIncrement)
    {
        HttpListenerException? lastConflict = null;
        var maxPort = allowIncrement
            ? Math.Min(65535, preferredPort + AutoIncrementSpan)
            : preferredPort;

        for (var port = preferredPort; port <= maxPort; port++)
        {
            var prefix = $"http://{DefaultHost}:{port}/";
            var attempts = allowIncrement ? 2 : StartRetryCount;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);

                try
                {
                    listener.Start();
                    if (port != preferredPort)
                    {
                        Log.Info($"{LogPrefix} Preferred port {preferredPort} was busy; bound {prefix}");
                    }

                    return (listener, port);
                }
                catch (HttpListenerException ex) when (IsPrefixConflict(ex))
                {
                    listener.Close();
                    lastConflict = ex;
                    if (attempt < attempts)
                    {
                        Log.Warn($"{LogPrefix} Prefix still busy, retrying start ({attempt}/{attempts - 1})...");
                        Thread.Sleep(StartRetryDelay);
                    }
                }
            }
        }

        if (lastConflict != null)
        {
            throw lastConflict;
        }

        throw new InvalidOperationException(
            $"Failed to bind STS2 AI Agent HTTP API starting at port {preferredPort}.");
    }

    private static bool IsExplicitPortConfigured()
    {
        var rawPort = Environment.GetEnvironmentVariable("STS2_API_PORT");
        return !string.IsNullOrWhiteSpace(rawPort) &&
            int.TryParse(rawPort.Trim(), out var port) &&
            port is > 0 and <= 65535;
    }

    private static int ResolvePreferredPort()
    {
        var rawPort = Environment.GetEnvironmentVariable("STS2_API_PORT");
        if (!string.IsNullOrWhiteSpace(rawPort) &&
            int.TryParse(rawPort.Trim(), out var port) &&
            port is > 0 and <= 65535)
        {
            return port;
        }

        return DefaultPort;
    }

    private static bool IsPrefixConflict(HttpListenerException ex)
    {
        return ex.ErrorCode == 183 ||
            ex.NativeErrorCode == 183 ||
            ex.Message.Contains("conflicts with an existing registration", StringComparison.OrdinalIgnoreCase);
    }
}
