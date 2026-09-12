namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the port policy in <c>HttpServer.cs</c>. The file cannot be compiled
/// into this assembly (it references the game's <c>MegaCrit…Log</c> and <c>Router</c>), so the policy
/// that decides whether a preferred port may drift is pinned from source. Each assertion names an
/// exact line, so deleting that line turns the matching test red.
/// </summary>
internal static class HttpServerPortPolicyTests
{
    private const string SourcePath = "STS2AIAgent/Server/HttpServer.cs";

    public static void ExplicitPortNeverDrifts()
    {
        var start = Flat(Body(SourcePath, "public void Start()"));
        var explicitGate = Flat(Body(SourcePath, "private static bool IsExplicitPortConfigured()"));
        var resolve = Flat(Body(SourcePath, "private static int ResolvePreferredPort()"));

        // A configured STS2_API_PORT resolves to that exact port.
        Assert.Contains("Environment.GetEnvironmentVariable(\"STS2_API_PORT\")", resolve);
        Assert.Contains("returnport;", resolve);
        Assert.Contains("returnDefaultPort;", resolve);

        // …and an explicit port only permits fallback when the operator opts in via AllowFallback().
        Assert.Contains("varallowIncrement=!IsExplicitPortConfigured()||AllowFallback();", start);
        Assert.Contains("varstarted=LoopbackListener.Start(preferredPort,allowIncrement);", start);

        // The explicitness test is a validated environment value, not a bare non-empty string.
        Assert.Contains("Environment.GetEnvironmentVariable(\"STS2_API_PORT\")", explicitGate);
        Assert.Contains(
            "return!string.IsNullOrWhiteSpace(rawPort)&&int.TryParse(rawPort.Trim(),outvarport)&&portis>0and<=65535;",
            explicitGate);
    }

    public static void AutoIncrementedPortIsFlagged()
    {
        var source = AgentSourceFixture.Read(SourcePath);
        var start = Flat(AgentSourceFixture.DeclarationBody(source, "public void Start()"));
        var flatSource = AgentSourceFixture.WithoutWhitespace(source);

        Assert.Contains("publicboolPortWasAutoIncremented{get;privateset;}", flatSource);
        Assert.Contains("Port=started.Port;", start);
        Assert.Contains("PortWasAutoIncremented=started.Port!=preferredPort;", start);
    }

    public static void FallbackPolicyGateIsPresent()
    {
        var source = AgentSourceFixture.Read(SourcePath);
        var start = Flat(AgentSourceFixture.DeclarationBody(source, "public void Start()"));
        var allowFallback = Flat(AgentSourceFixture.DeclarationBody(source, "internal static bool AllowFallback()"));

        Assert.Contains("varpreferredPort=ResolvePreferredPort();", start);
        Assert.Contains("AllowFallback();", start);

        // Fallback is opt-in only: "1" or "true" on STS2_API_ALLOW_FALLBACK, case-insensitive.
        Assert.Contains("Environment.GetEnvironmentVariable(\"STS2_API_ALLOW_FALLBACK\")", allowFallback);
        Assert.Contains("string.Equals(raw,\"1\",StringComparison.OrdinalIgnoreCase)", allowFallback);
        Assert.Contains("string.Equals(raw,\"true\",StringComparison.OrdinalIgnoreCase)", allowFallback);
    }

    private static string Body(string relativePath, string declaration)
    {
        return AgentSourceFixture.DeclarationBody(AgentSourceFixture.Read(relativePath), declaration);
    }

    private static string Flat(string source)
    {
        return AgentSourceFixture.WithoutWhitespace(source);
    }
}
