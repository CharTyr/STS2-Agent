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
        Assert.Equal("--windowed --force-steam off --clientId 1", CoopLaunchPolicy.CompanionArguments("off", null));

        Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionArguments("off", ulong.MaxValue.ToString()));
        Expect<InvalidOperationException>(() => CoopLaunchPolicy.CompanionArguments("off", "not-a-number"));

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
            Expect<InvalidOperationException>(() => CoopLaunchPolicy.SeedCompanionSettings(isolated, isolated));

            File.WriteAllText(isolated, "{\"main\":true}");
            Assert.False(File.Exists(derived));
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.True(File.Exists(derived));
            Assert.Equal("{\"main\":true}", File.ReadAllText(derived));

            File.WriteAllText(derived, "{\"companion\":true}");
            File.WriteAllText(isolated, "{\"main\":updated}");
            CoopLaunchPolicy.SeedCompanionSettings(isolated, derived);
            Assert.Equal("{\"main\":updated}", File.ReadAllText(derived));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", previous);
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static void CompanionPortFileRoundTrip()
    {
        var pid = 424242;
        CompanionPortFile.Delete(pid);
        Assert.True(CompanionPortFile.TryRead(pid) == null);
        CompanionPortFile.Write(pid, 18081);
        Assert.Equal(18081, CompanionPortFile.TryRead(pid));
        var ports = CompanionPortFile.EnumerateCandidates(18080, pid).ToArray();
        Assert.Equal(18080, ports[0]);
        Assert.True(ports.Contains(18081));
        CompanionPortFile.Delete(pid);
        Assert.True(CompanionPortFile.TryRead(pid) == null);
    }

    public static void CompanionBootstrapDoesNotHostLobby()
    {
        var source = File.ReadAllText(FindSource("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs"));
        var start = source.IndexOf("RunCompanionBootstrapAsync", StringComparison.Ordinal);
        var open = source.IndexOf('{', start);
        var depth = 0;
        var end = open;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                end = i;
                break;
            }
        }

        var body = source[open..(end + 1)];
        Assert.False(body.Contains("OpenMultiplayerTestAsync", StringComparison.Ordinal));
        Assert.Contains("join_multiplayer_lobby", body, StringComparison.Ordinal);
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

    private static string FindSource(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(relativePath);
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
