using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class CoopHardeningTests
{
    private static void ExpectIo(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new Exception("Expected an installation error");
    }

    public static void StagingUsesLoadedFilesAndPreservesConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "another-steam-library", "workshop");
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(source);
            foreach (var file in CompanionModStager.Files) File.WriteAllText(Path.Combine(source, file), file.EndsWith(".json") ? "{\"id\":\"STS2AIAgent\"}" : "current-" + file);
            var staged = CompanionModStager.Prepare(source, game);
            Assert.Equal(staged, CompanionModStager.Prepare(source, game));
            var dll = Path.Combine(staged, "STS2AIAgent.dll");
            File.WriteAllText(dll, "old");
            ExpectIo(() => CompanionModStager.Prepare(source, game));
            Assert.Equal("old", File.ReadAllText(dll));
            File.Delete(Path.Combine(staged, "STS2AIAgent.pck"));
            ExpectIo(() => CompanionModStager.Prepare(source, game));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void StartupRejectsWrongProcessAndPartialWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            CompanionStartupFile.Write(path, new(123, "listening", 20000, null));
            Assert.Equal(20000, CompanionStartupFile.Read(path, 123)?.Port);
            Assert.Null(CompanionStartupFile.Read(path, 124));
            File.WriteAllText(path, "{");
            Assert.Null(CompanionStartupFile.Read(path, 123));
        }
        finally { File.Delete(path); }
    }

    public static async Task DiscoveryHonorsTotalDeadlineAndCancellation()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await CompanionDiscovery.WaitAsync(() => Enumerable.Range(8081, 33),
            async (_, token) => { await Task.Delay(10000, token); return false; },
            () => false, TimeSpan.FromMilliseconds(70), CancellationToken.None);
        Assert.Null(result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            await CompanionDiscovery.WaitAsync(() => new[] { 8081 }, (_, _) => Task.FromResult(false),
                () => false, TimeSpan.FromSeconds(5), cancel.Token);
            throw new Exception("Expected caller cancellation");
        }
        catch (OperationCanceledException) { }
        Assert.Equal(19000, await CompanionDiscovery.WaitAsync(() => new[] { 19000, 8081 },
            (port, _) => Task.FromResult(port == 19000), () => false, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    public static void ToolProbeDoesNotDowngradeAuthenticationOrUnrelatedErrors()
    {
        Assert.Equal(STS2AIAgent.Agent.StopKindPolicy.Failed, STS2AIAgent.Agent.StopKindPolicy.Classify("failed reading player 1404299"));
        Assert.Equal(STS2AIAgent.Agent.StopKindPolicy.Configuration, STS2AIAgent.Agent.StopKindPolicy.Classify("HTTP 401 Unauthorized"));
        Assert.False(OpenAiCompatibleClient.IsUnsupportedToolError(new LlmException("HTTP 401 tools unauthorized", statusCode: 401)));
        Assert.False(OpenAiCompatibleClient.IsUnsupportedToolError(new LlmException("HTTP 400 invalid model", statusCode: 400)));
        Assert.False(OpenAiCompatibleClient.IsUnsupportedToolError(new LlmException("tool_choice required incompatible with thinking", statusCode: 400)));
        Assert.True(OpenAiCompatibleClient.IsUnsupportedToolError(new LlmException("tools are not supported", statusCode: 400)));
        var settings = AgentSettings.CreateDefault();
        var model = settings.ResolveConversationModel();
        var before = ModelRoleProbe.Fingerprint(model);
        model.Endpoint.ThinkingProtocol = "deepseek";
        Assert.False(before == ModelRoleProbe.Fingerprint(model));
        Assert.Equal("deepseek", SettingsClone.Clone(settings).Endpoints[0].ThinkingProtocol);
    }
}
