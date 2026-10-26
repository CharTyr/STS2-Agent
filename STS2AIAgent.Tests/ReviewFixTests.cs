using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// Regressions for the 2026-10 full bug review: the REST browser guard, settings recovery from a
/// parseable-but-unusable file, diagnostic redaction gaps, and the shared timeout argument.
/// </summary>
internal static class ReviewFixTests
{
    public static void GuardAllowsLocalClients()
    {
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080));
        Assert.True(LocalRequestGuard.IsTrusted("localhost:8080", "", 8080));
        Assert.True(LocalRequestGuard.IsTrusted("LOCALHOST:8080", null, 8080));
        Assert.True(LocalRequestGuard.IsTrusted("[::1]:8080", null, 8080));
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1", null, 8080));
        Assert.True(LocalRequestGuard.IsTrusted(null, null, 8080), "HTTP/1.0 clients may omit Host.");
    }

    public static void GuardRefusesRebindingHosts()
    {
        Assert.False(LocalRequestGuard.IsTrusted("evil.example:8080", null, 8080));
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1.evil.example:8080", null, 8080));
        Assert.False(LocalRequestGuard.IsTrusted("localhost.evil.example", null, 8080));
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:9999", null, 8080), "Another port is another origin.");
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:abc", null, 8080));
        Assert.False(LocalRequestGuard.IsTrusted("[::1", null, 8080));
        Assert.False(LocalRequestGuard.IsTrusted("192.168.1.5:8080", null, 8080));
    }

    public static void GuardRefusesBrowserOrigins()
    {
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:8080", "https://evil.example", 8080));
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:8080", "http://127.0.0.1:8080", 8080),
            "REST routes serve no page, so no browser origin is legitimate.");
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:8080", "null", 8080));
        // A cross-site <img>/<script> GET carries no Origin but always a Sec-Fetch-Site label.
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080, fetchSite: "cross-site"));
        Assert.False(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080, fetchSite: "same-site"));
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080, fetchSite: "none"));
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080, fetchSite: "same-origin"));
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1:8080", null, 8080, allowOrigin: true, fetchSite: "cross-site"),
            "The MCP route applies its own browser policy.");
        // The MCP route applies its own Origin policy, so only Host is enforced for it.
        Assert.True(LocalRequestGuard.IsTrusted("127.0.0.1:8080", "http://127.0.0.1:8080", 8080, allowOrigin: true));
        Assert.False(LocalRequestGuard.IsTrusted("evil.example", "http://127.0.0.1:8080", 8080, allowOrigin: true));
    }

    public static void RouterRunsTheGuardFirst()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var guard = source.IndexOf("if (!LocalRequestGuard.IsTrusted(", StringComparison.Ordinal);
        Assert.True(guard > 0, "Router must branch on the guard's result.");
        var refusal = source.IndexOf("throw new ApiException(403, \"untrusted_origin\"", guard, StringComparison.Ordinal);
        var firstRoute = source.IndexOf("\"/session/control\"", StringComparison.Ordinal);
        Assert.True(refusal > guard && refusal < firstRoute,
            "A refused request must be rejected with 403 before any route is matched.");
        Assert.Contains("fetchSite: request.Headers[\"Sec-Fetch-Site\"]", source);
    }

    public static void SettingsWithNullEntriesAreRecovered()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sts2-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(path);
            store.Save(AgentSettings.CreateDefault());
            File.WriteAllText(path, "{\"endpoints\":[null],\"models\":[null],\"roleTests\":[null]}");

            var loaded = store.Load();

            // Filtering the null entries is what makes the file usable; the backup-and-restore path
            // would also yield non-null lists, so assert that path was not taken.
            Assert.Equal("ok", store.LastNotice.Kind);
            Assert.True(Directory.GetFiles(directory, "settings.json.corrupt-*").Length == 0,
                "A file with only null entries is repaired in place, not treated as corrupt.");
            Assert.NotEmpty(loaded.Endpoints);
            Assert.True(loaded.Endpoints.All(endpoint => endpoint != null));
            Assert.True(loaded.Models.All(model => model != null));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void UnusableFileFallsBackInsteadOfThrowing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sts2-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "[1,2,3]");
            var store = new SettingsStore(path);

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.True(store.LastNotice.HasMessage, "The player must be told the file was replaced.");
            Assert.True(Directory.GetFiles(directory, "settings.json.corrupt-*").Length == 1,
                "The unusable file must be kept as a backup.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void RedactionCoversCredentialSchemesAndEnvNames()
    {
        foreach (var (input, secret) in new[]
        {
            ("Authorization: Basic dXNlcjpwYXNzd29yZA==", "dXNlcjpwYXNzd29yZA=="),
            ("Authorization: Token abcdef123456", "abcdef123456"),
            ("OPENAI_API_KEY=abc123xyz", "abc123xyz"),
            ("GOOGLE_API_KEY=AIzaSyA1234567890", "AIzaSyA1234567890"),
            ("secret_key=hunter2hunter2", "hunter2hunter2"),
            ("callback?access_token%3Dabc987def", "abc987def"),
            ("password: \"my pass phrase\"", "pass phrase"),
            ("{\"apiKey\":\"abc def ghi\"}", "def ghi"),
            ("AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG", "wJalrXUtnFEMI/K7MDENG"),
            ("password: \"it's a long phrase\"", "long phrase"),
            ("password: 'say \"hi\" to all'", "to all"),
            ("{\"token\":\"a \\\"quoted\\\" secret\"}", "secret"),
        })
        {
            var redacted = DiagnosticExport.Redact(input);
            Assert.False(redacted.Contains(secret, StringComparison.Ordinal),
                $"'{input}' still exposes '{secret}': {redacted}");
        }
    }

    public static void RedactionLeavesOrdinaryTextAlone()
    {
        foreach (var text in new[] { "max_tokens: 100", "session_tokens=12", "monkey=banana", "hotkey F8", "status=ok" })
        {
            Assert.Equal(text, DiagnosticExport.Redact(text));
        }
    }

    public static void TimeoutArgumentIsFiniteAndCultureInvariant()
    {
        static double Read(string json) => ToolTimeout.ReadSeconds(JsonDocument.Parse(json).RootElement);

        Assert.Equal(20d, Read("{}"));
        Assert.Equal(5d, Read("{\"timeout_seconds\":5}"));
        Assert.Equal(1.5d, Read("{\"timeout_seconds\":\"1.5\"}"));
        Assert.Equal(20d, Read("{\"timeout_seconds\":\"NaN\"}"));
        Assert.Equal(20d, Read("{\"timeout_seconds\":\"Infinity\"}"));
        Assert.Equal(120d, Read("{\"timeout_seconds\":9999}"));
        Assert.Equal(1d, Read("{\"timeout_seconds\":-3}"));
        Assert.Equal(20d, Read("{\"timeout_seconds\":null}"));
        Assert.Equal(20d, Read("[]"));

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(1.5d, Read("{\"timeout_seconds\":\"1.5\"}"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    public static void BothToolSurfacesShareTheTimeoutReader()
    {
        foreach (var file in new[] { "STS2AIAgent/Agent/AgentLoop.Arguments.cs", "STS2AIAgent/Server/NativeMcpServer.Tools.cs" })
        {
            var body = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(file), "ReadTimeoutSeconds");
            Assert.Contains("ToolTimeout.FromValue", body);
        }
    }
}
