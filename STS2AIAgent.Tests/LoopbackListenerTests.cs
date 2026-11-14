using System.Net;
using System.Net.Sockets;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class LoopbackListenerTests
{
    public static void ExcludedRangeUsesDynamicPort()
    {
        var attempted = new List<int>();
        var selected = LoopbackListener.Select(8080, true, port =>
        {
            attempted.Add(port);
            if (port < 49000) throw new HttpListenerException(32);
            return "listening";
        }, () => 49000, () => throw new Exception("Automatic selection should not sleep."));
        Assert.Equal(49000, selected.Port);
        Assert.Equal(LoopbackListener.NearbyPortCount + 1, attempted.Count);
        Assert.Equal("listening", selected.Listener);
    }

    public static void BindRaceReselectsDynamicPort()
    {
        var allocated = 49000;
        var selected = LoopbackListener.Select(65535, true, port =>
        {
            if (port != 49002) throw new HttpListenerException(183);
            return port;
        }, () => ++allocated, () => { });
        Assert.Equal(49002, selected.Port);
    }

    public static void ExplicitPortNeverChanges()
    {
        var attempts = 0;
        var waits = 0;
        var error = Expect<InvalidOperationException>(() => LoopbackListener.Select<int>(8080, false, port =>
        {
            Assert.Equal(8080, port);
            attempts++;
            throw new HttpListenerException(183);
        }, () => throw new Exception("Explicit port cannot fall back."), () => waits++));
        Assert.Equal(LoopbackListener.ExplicitPortAttempts, attempts);
        Assert.Equal(attempts - 1, waits);
        Assert.Contains("STS2_API_PORT", error.Message);
    }

    public static void ExplicitReservedPortFailsClearly()
    {
        var error = Expect<InvalidOperationException>(() => LoopbackListener.Select<int>(8080, false,
            _ => throw new HttpListenerException(32),
            () => throw new Exception("Unexpected fallback"), () => throw new Exception("Unexpected retry")));
        Assert.Contains("8080", error.Message);
        Assert.True(error.InnerException is HttpListenerException);
    }

    public static void ExhaustionIsBounded()
    {
        var attempts = 0;
        Expect<InvalidOperationException>(() => LoopbackListener.Select<int>(8080, true, _ =>
        {
            attempts++;
            throw new HttpListenerException(5);
        }, () => 49000, () => { }));
        Assert.Equal(LoopbackListener.NearbyPortCount + LoopbackListener.DynamicPortAttempts, attempts);
    }

    public static void UnexpectedFailureIsNotHidden()
    {
        var error = Expect<HttpListenerException>(() => LoopbackListener.Select<int>(8080, true,
            _ => throw new HttpListenerException(87),
            () => throw new Exception("Unexpected fallback"), () => { }));
        Assert.Equal(87, error.NativeErrorCode);
    }

    public static async Task RealLoopbackListenerResponds()
    {
        RealHttpListenerCapability.Require();
        var started = LoopbackListener.Start(49080, allowFallback: true);
        using var listener = started.Listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var http = new HttpClient();
        var request = http.GetAsync($"http://127.0.0.1:{started.Port}/health", timeout.Token);
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        context.Response.StatusCode = 204;
        context.Response.Close();
        using var response = await request;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static void RunnerDistinguishesFailureAndSkip()
    {
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        int failures;
        try
        {
            Console.SetOut(output);
            failures = TestRunner.Run(new (string Name, Func<Task> Body)[]
            {
                ("passes", () => Task.CompletedTask),
                ("skipped", () => throw new TestSkippedException("probe confirmed unavailable")),
                ("assertion", () => { Assert.True(false, "deliberate assertion failure"); return Task.CompletedTask; })
            });
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var report = output.ToString();
        Assert.Equal(1, failures);
        Assert.Contains("PASS  passes", report);
        Assert.Contains("SKIP  skipped", report);
        Assert.Contains("FAIL  assertion", report);
        Assert.Contains("deliberate assertion failure", report);
        Assert.Contains("SUMMARY Passed=1 Failed=1 Skipped=1", report);
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

internal static class RealHttpListenerCapability
{
    public static void Require()
    {
        if (HttpListener.IsSupported)
        {
            return;
        }

        // Probe the native listener independently, on an OS-selected loopback port.
        // Do not turn failures from the actual product listener or HTTP checks into skips.
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port;
        try
        {
            port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        }
        finally
        {
            tcp.Stop();
        }

        using var probe = new HttpListener();
        probe.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            probe.Start();
        }
        catch (HttpListenerException ex) when (OperatingSystem.IsWindows() && ex.NativeErrorCode == 6)
        {
            throw new TestSkippedException(
                "HttpListener.IsSupported is false and an independent listener probe returned Windows error 6 (invalid handle).");
        }
    }
}
