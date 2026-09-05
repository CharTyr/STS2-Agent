using System.Net;
using System.Text.Json;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Config;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class CompanionStartupTests
{
    public static void OfflineLaunchKeepsAccountsIsolated()
    {
        Assert.Equal("--windowed", CoopLaunchPolicy.CompanionArguments(null, "901"));
        Assert.Equal("--windowed --force-steam off --clientId 902", CoopLaunchPolicy.CompanionArguments("off", "901"));
        Assert.Equal("--windowed --force-steam off", CoopLaunchPolicy.CompanionArguments("off", null));

        Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionArguments("off", ulong.MaxValue.ToString()));

        Assert.True(CoopLaunchPolicy.TryGetCompanionArguments("off", "901", out var args, out var err));
        Assert.Equal("--windowed --force-steam off --clientId 902", args);
        Assert.True(err == null);

        Assert.False(CoopLaunchPolicy.TryGetCompanionArguments("off", ulong.MaxValue.ToString(), out var badArgs, out var badErr));
        Assert.Equal(string.Empty, badArgs);
        Assert.True(badErr != null && badErr.Contains("adjacent companion ID"));
    }

    public static void SettingsPathCanBeIsolated()
    {
        var previous = Environment.GetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), "sts2-coop-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var isolated = Path.Combine(tempDir, "settings.json");
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", isolated);
            Assert.Equal(Path.GetFullPath(isolated), new SettingsStore().Path);
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", "relative.json");
            Expect<InvalidOperationException>(() => new SettingsStore());

            var derived = CoopLaunchPolicy.CompanionSettingsPath(isolated);
            Assert.Equal(Path.Combine(tempDir, "settings.companion.json"), derived);

            var explicitCustom = Path.Combine(tempDir, "custom.companion.json");
            Assert.Equal(explicitCustom, CoopLaunchPolicy.CompanionSettingsPath(isolated, explicitCustom));
            Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionSettingsPath(isolated, "relative.companion.json"));
            Expect<ArgumentException>(() => CoopLaunchPolicy.CompanionSettingsPath(""));

            File.WriteAllText(isolated, "{\"main\":true}");
            Assert.False(File.Exists(derived));
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.True(File.Exists(derived));
            Assert.Equal("{\"main\":true}", File.ReadAllText(derived));

            File.WriteAllText(derived, "{\"companion\":true}");
            File.WriteAllText(isolated, "{\"main\":updated}");
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.Equal("{\"companion\":true}", File.ReadAllText(derived));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", previous);
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

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

    public static void HealthRequiresExactCompanionIdentity()
    {
        string Health(int pid = 123, int port = 8081, string role = "companion", string service = "sts2-ai-agent", string status = "ready") =>
            JsonSerializer.Serialize(new { ok = true, data = new { process_id = pid, api_port = port, instance_role = role, service, status } });
        Assert.True(CompanionHealth.IsExpectedProcess(Health(), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(pid: 456), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(port: 8082), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(role: "human"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(service: "other"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess(Health(status: "not ready"), 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("{\"ok\":false,\"status\":\"ready\"}", 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("{\"ok\":true,\"data\":[]}", 8081, 123));
        Assert.True(!CompanionHealth.IsExpectedProcess("not JSON", 8081, 123));
    }

    public static void LaunchPreconditionsProtectHumanRun()
    {
        var settings = AgentSettings.CreateDefault();
        var model = settings.ResolvePlayModel();
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model) == null);
        Assert.Contains("主窗口", CoopLaunchPolicy.GetError(true, false, "MAIN_MENU", model));
        Assert.Contains("暂停", CoopLaunchPolicy.GetError(false, true, "MAIN_MENU", model));
        Assert.Contains("主菜单", CoopLaunchPolicy.GetError(false, false, "COMBAT", model));
        Assert.Contains("模型", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", null));
        model.Endpoint.BaseUrl = "file:///tmp/model";
        Assert.Contains("地址", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model));
        model.Endpoint.BaseUrl = "http://localhost:1234/v1";
        model.Endpoint.ApiKey = "";
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", model) == null);
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
