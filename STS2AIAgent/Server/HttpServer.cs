using System.Net;
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
            var started = LoopbackListener.Start(preferredPort, allowIncrement);
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
        var selected = LoopbackListener.Start(port, allowFallback: true);
        selected.Listener.Close();
        return selected.Port;
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
}
