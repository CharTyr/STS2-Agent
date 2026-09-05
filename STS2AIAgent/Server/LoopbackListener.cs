using System.Net;
using System.Net.Sockets;

namespace STS2AIAgent.Server;

// Keep port selection independent of Godot so startup failures can be reproduced
// without launching a game. A successful TCP probe alone does not prove that
// HTTP.sys can bind the port; the final decision always belongs to HttpListener.
internal static class LoopbackListener
{
    internal const int NearbyPortCount = 33;
    internal const int DynamicPortAttempts = 16;
    internal const int ExplicitPortAttempts = 20;

    public static (HttpListener Listener, int Port) Start(int preferredPort, bool allowFallback)
    {
        return Select(
            preferredPort,
            allowFallback,
            Bind,
            AllocateDynamicPort,
            () => Thread.Sleep(TimeSpan.FromMilliseconds(250)));
    }

    internal static (T Listener, int Port) Select<T>(
        int preferredPort,
        bool allowFallback,
        Func<int, T> bind,
        Func<int> allocateDynamicPort,
        Action waitBeforeRetry)
    {
        if (preferredPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredPort));
        }

        HttpListenerException? lastFailure = null;
        var nearbyCount = allowFallback ? Math.Min(NearbyPortCount, 65536 - preferredPort) : 1;
        for (var offset = 0; offset < nearbyCount; offset++)
        {
            var port = preferredPort + offset;
            var attempts = allowFallback ? 1 : ExplicitPortAttempts;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    return (bind(port), port);
                }
                catch (HttpListenerException ex) when (
                    allowFallback ? IsUnavailablePort(ex) : IsRegistrationConflict(ex))
                {
                    lastFailure = ex;
                    if (attempt + 1 < attempts)
                    {
                        waitBeforeRetry();
                    }
                }
                catch (HttpListenerException ex) when (!allowFallback && IsUnavailablePort(ex))
                {
                    throw new InvalidOperationException(
                        $"Configured loopback HTTP port {preferredPort} is unavailable. Change STS2_API_PORT or free that port.", ex);
                }
            }
        }

        if (allowFallback)
        {
            // Windows may reserve the entire nearby range. Let the OS nominate
            // a dynamic loopback port, then retry the real HTTP bind a bounded
            // number of times in case another process wins the release/bind race.
            for (var attempt = 0; attempt < DynamicPortAttempts; attempt++)
            {
                var port = allocateDynamicPort();
                if (port is < 1 or > 65535)
                {
                    throw new InvalidOperationException("The OS returned an invalid loopback port.");
                }

                try
                {
                    return (bind(port), port);
                }
                catch (HttpListenerException ex) when (IsUnavailablePort(ex))
                {
                    lastFailure = ex;
                }
            }
        }

        throw new InvalidOperationException(
            allowFallback
                ? $"Unable to bind a loopback HTTP port starting at {preferredPort}, including dynamically selected ports."
                : $"Configured loopback HTTP port {preferredPort} is unavailable. Change STS2_API_PORT or free that port.",
            lastFailure);
    }

    private static HttpListener Bind(int port)
    {
        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            return listener;
        }
        catch
        {
            listener.Close();
            throw;
        }
    }

    private static int AllocateDynamicPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    internal static bool IsUnavailablePort(HttpListenerException ex)
    {
        // HTTP.sys excluded/reserved ranges can report sharing violation (32)
        // or access denied (5), even when no ordinary TCP listener owns the port.
        return ex.NativeErrorCode is 5 or 32 or 183 or 10013 or 10048 || IsRegistrationConflict(ex);
    }

    private static bool IsRegistrationConflict(HttpListenerException ex)
    {
        return ex.NativeErrorCode == 183 ||
            ex.Message.Contains("conflicts with an existing registration", StringComparison.OrdinalIgnoreCase);
    }
}
