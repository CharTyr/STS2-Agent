using System.Net;
using System.Net.Sockets;
using System.Threading;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Server;

public sealed class HttpServer
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 8080;
    private const string LogPrefix = "[STS2AIAgent.HttpServer]";

    private static readonly Lazy<HttpServer> LazyInstance = new(() => new HttpServer());

    private readonly object _gate = new();
    private HttpListener? _listener;
    private LocalSocketHttpServer? _socketServer;
    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;

    public static HttpServer Instance => LazyInstance.Value;

    public string Host => DefaultHost;

    public int Port { get; private set; } = DefaultPort;

    public string Prefix { get; private set; } = $"http://{DefaultHost}:{DefaultPort}/";

    public bool PortWasAutoIncremented { get; private set; }

    /// <summary>True only while a transport is bound and dispatching requests.</summary>
    public bool IsListening => _listener != null || _socketServer != null;

    /// <summary>
    /// Why no transport is bound, for the overlay and diagnostics. Null while listening.
    /// </summary>
    public string? Unavailable { get; private set; }

    /// <summary>The transport currently answering, for diagnostics: "http.sys" or "socket".</summary>
    public string Transport => _socketServer != null ? "socket" : "http.sys";

    /// <summary>
    /// The transport could not start at all. Callers that can keep working without the API catch
    /// this; anything else must still see it as a startup failure.
    /// </summary>
    public sealed class StartupException(string message, Exception? inner) : Exception(message, inner);

    private HttpServer()
    {
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_listener != null || _socketServer != null)
            {
                Log.Info($"{LogPrefix} Already started on {Prefix}");
                return;
            }

            var preferredPort = ResolvePreferredPort();
            var allowIncrement = !IsExplicitPortConfigured() || AllowFallback();
            try
            {
                var started = LoopbackListener.Start(preferredPort, allowIncrement);
                _listener = started.Listener;
                BeginServing(started.Port, preferredPort, transport: "http.sys");
                return;
            }
            catch (HttpListenerException ex) when (ex.NativeErrorCode == InvalidHandleErrorCode)
            {
                // The HTTP Server API refused to create its request queue (Windows error 6). Plain
                // loopback TCP still works in that process, so serve the same routes over a socket.
                Log.Warn($"{LogPrefix} HTTP.sys refused the request queue ({ex.Message}); using the loopback socket transport.");
            }
            catch (InvalidOperationException ex) when (!allowIncrement)
            {
                // A pinned port that cannot be registered is a configuration dead end, not a bug in
                // the routes: callers that work without the API must be able to keep going.
                Unavailable = ex.Message;
                throw new StartupException("The configured local API port is unavailable.", ex);
            }

            try
            {
                var socketPort = BindSocketPort(preferredPort, allowIncrement);
                _socketServer = new LocalSocketHttpServer(socketPort, (context, token) => Router.HandleAsync(context, token));
                _socketServer.Start();
                BeginServing(_socketServer.Port, preferredPort, transport: "socket");
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException)
            {
                _socketServer?.Dispose();
                _socketServer = null;
                Unavailable = ex.Message;
                Log.Error($"{LogPrefix} No loopback transport could start: {ex.Message}");
                throw new StartupException("The local HTTP API could not start on any transport.", ex);
            }
        }
    }

    private const int InvalidHandleErrorCode = 6;

    private static int BindSocketPort(int preferredPort, bool allowIncrement)
    {
        var nearby = allowIncrement ? Math.Min(LoopbackListener.NearbyPortCount, 65536 - preferredPort) : 1;
        for (var offset = 0; offset < nearby; offset++)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, preferredPort + offset);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }
            catch (SocketException)
            {
                // Try the next nearby port, exactly like the HTTP.sys path.
            }
        }

        if (!allowIncrement)
        {
            throw new InvalidOperationException(
                $"Configured loopback HTTP port {preferredPort} is unavailable for the socket transport.");
        }

        var dynamic = new TcpListener(IPAddress.Loopback, 0);
        dynamic.Start();
        var allocated = ((IPEndPoint)dynamic.LocalEndpoint).Port;
        dynamic.Stop();
        return allocated;
    }

    private void BeginServing(int port, int preferredPort, string transport)
    {
        Port = port;
        PortWasAutoIncremented = port != preferredPort;
        Prefix = $"http://{DefaultHost}:{Port}/";
        Unavailable = null;
        InstanceEndpointFile.Publish(Port);
        PublishCompanionPort(Port);
        CompanionStartupFile.Publish("listening", Port);

        _cts = new CancellationTokenSource();
        if (_listener != null)
        {
            _listenLoopTask = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
        }

        Log.Info($"{LogPrefix} Listening on {Prefix} via {transport}");
    }

    public static int FindFreePort(int startPort)
    {
        var port = startPort is > 0 and <= 65535 ? startPort : DefaultPort;
        var selected = LoopbackListener.Start(port, allowFallback: true);
        selected.Listener.Close();
        return selected.Port;
    }

    public void Stop()
    {
        HttpListener? listener;
        LocalSocketHttpServer? socketServer;
        CancellationTokenSource? cts;
        Task? listenLoopTask;

        lock (_gate)
        {
            if (_listener == null && _socketServer == null && _cts == null && _listenLoopTask == null)
            {
                return;
            }

            listener = _listener;
            socketServer = _socketServer;
            cts = _cts;
            listenLoopTask = _listenLoopTask;
            _listener = null;
            _socketServer = null;
            _cts = null;
            _listenLoopTask = null;
        }

        try { socketServer?.Dispose(); }
        catch (Exception ex) { Log.Warn($"{LogPrefix} Socket transport stop failed: {ex.Message}"); }

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

        InstanceEndpointFile.Delete();
        ClearCompanionPort();
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
                    await Router.WriteErrorAsync(new HttpListenerResponseData(context.Response), 500, "listener_error", "HTTP listener failed.");
                }
            }
        }
    }

    internal static bool AllowFallback()
    {
        var raw = Environment.GetEnvironmentVariable("STS2_API_ALLOW_FALLBACK");
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitPortConfigured()
    {
        var rawPort = Environment.GetEnvironmentVariable("STS2_API_PORT");
        return !string.IsNullOrWhiteSpace(rawPort) &&
            int.TryParse(rawPort.Trim(), out var port) &&
            port is > 0 and <= 65535;
    }

    private static void PublishCompanionPort(int port)
    {
        var role = Environment.GetEnvironmentVariable("STS2_AGENT_ROLE");
        if (!string.Equals(role, "companion", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            CompanionPortFile.Write(Environment.ProcessId, port);
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Failed to publish companion port: {ex.Message}");
        }
    }

    private static void ClearCompanionPort()
    {
        try
        {
            CompanionPortFile.Delete(Environment.ProcessId);
        }
        catch
        {
        }
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

}
