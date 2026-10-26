using System.Net;
using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class UsabilityRegressionTests
{
    public static void AliasesDoNotSelectProviderFieldsBySubstring()
    {
        Assert.Equal("prompt", STS2AIAgent.Llm.ThinkingRequestBuilder.InferMode("my-proxy-o1-alias"));
        Assert.Equal("prompt", STS2AIAgent.Llm.ThinkingRequestBuilder.InferMode("not-deepseek-model"));
        Assert.Equal("deepseek", STS2AIAgent.Llm.ThinkingRequestBuilder.InferMode("vendor/deepseek-chat"));
        Assert.Equal("reasoning_effort", STS2AIAgent.Llm.ThinkingRequestBuilder.InferMode("openai/o3-mini"));
        Assert.True(STS2AIAgent.Llm.ThinkingRequestBuilder.Build("custom-name", "deepseek", ThinkingIntensity.Medium).DeepSeekThinking != null);
    }

    public static void ChatOnlyAndLegacyVerificationCannotStartPlay()
    {
        var settings = AgentSettings.CreateDefault();
        var model = settings.TryResolvePlayModel()!;
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, model));
        Assert.False(FirstRunSetup.Evaluate(settings).ReadyToInvite);
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, model, false));
        Assert.Equal("tools_unavailable", FirstRunSetup.Evaluate(settings).Phase);
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, model, true));
        Assert.True(FirstRunSetup.Evaluate(settings).ReadyToInvite);
        model.Model.SupportsTools = false;
        Assert.False(FirstRunSetup.Evaluate(settings).ReadyToInvite);
    }

    public static void QuickSetupBindsOneModelAndInvalidatesOldProvider()
    {
        var settings = AgentSettings.CreateDefault();
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!, true));
        var next = QuickSetup.Apply(settings, 0, "new-key", "deepseek-chat", "");
        Assert.Equal(1, next.Models.Count);
        Assert.Equal(next.ConversationModelId, next.PlayModelId);
        Assert.Equal("https://api.deepseek.com", next.TryResolvePlayModel()!.Endpoint.BaseUrl);
        Assert.Equal("deepseek", next.TryResolvePlayModel()!.Endpoint.ThinkingProtocol);
        Assert.False(FirstRunSetup.Evaluate(next).ReadyToInvite);
        Assert.False(settings.TryResolvePlayModel()!.Endpoint.ApiKey == "new-key");
        var local = QuickSetup.Apply(next, 3, "", "qwen3", "");
        Assert.True(FirstRunSetup.Evaluate(local).ProviderConfigReachable);
        Assert.Equal("", local.TryResolvePlayModel()!.Endpoint.ApiKey);
        Assert.Equal(5, QuickSetup.FindProvider("https://my-provider.test/v1"));
        settings.Endpoints.Add(new LlmEndpoint { Id = "quick-start", BaseUrl = "https://keep.example/v1", ApiKey = "preserve" });
        settings.Models.Add(new LlmModelConfig { Id = "vision", EndpointId = "quick-start", Model = "vision" });
        var advanced = QuickSetup.Apply(settings, 0, "new-key", "deepseek-chat", "");
        Assert.Equal("preserve", advanced.FindEndpoint("quick-start")!.ApiKey);
        Assert.Equal("vision", advanced.FindModel("vision")!.Model);
        Assert.Equal("quick-start-2", advanced.TryResolvePlayModel()!.Endpoint.Id);
        var rejected = false;
        try { QuickSetup.Apply(settings, 5, "", "model", "file:///tmp/model"); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected);
    }

    public static void FullPatchUpdatesPlayWithoutChangingLocalInstanceSettings()
    {
        var host = QuickSetup.Apply(AgentSettings.CreateDefault(), 0, "host-key", "deepseek-chat", "");
        host.NonCombatOnlyEnabled = true;
        host.MaxSessionRequests = 12;
        host.MaxSessionTokens = 1000;
        var patch = CompanionSettingsPatch.From(host);
        var child = AgentSettings.CreateDefault();
        child.McpEnabled = true;
        child.OverlayPlayMode = "coop";
        child.Hotkey = "F9";
        var wire = JsonSerializer.Deserialize<CompanionSettingsPatch>(JsonSerializer.Serialize(patch))!;
        Assert.True(wire.IsValid);
        wire.ApplyTo(child);
        host.Endpoints[0].ApiKey = "changed-after-copy";
        Assert.Equal("host-key", child.TryResolvePlayModel()!.Endpoint.ApiKey);
        Assert.Equal(12, child.MaxSessionRequests);
        Assert.Equal(1000, child.MaxSessionTokens);
        Assert.True(child.NonCombatOnlyEnabled);
        Assert.True(child.McpEnabled);
        Assert.Equal("coop", child.OverlayPlayMode);
        Assert.Equal("F9", child.Hotkey);
        Assert.True(wire.HasSameValues(CompanionSettingsPatch.From(child)));
        child.MaxSessionRequests = 13;
        Assert.False(wire.Fingerprint() == CompanionSettingsPatch.From(child).Fingerprint());
        Assert.False(new CompanionPlaySettings { MaxSessionRequests = -1 }.IsValid);
    }

    public static void NewInstallTrialPreservesExistingUnlimitedChoice()
    {
        Assert.Equal(30, AgentSettings.CreateDefault().MaxSessionRequests);
        var legacy = JsonSerializer.Deserialize<AgentSettings>("{\"MaxSessionRequests\":null}")!;
        legacy.EnsureValidShape();
        Assert.Null(legacy.MaxSessionRequests);
        Assert.Null(SettingsClone.Clone(legacy).MaxSessionRequests);
        Assert.Equal("autoplay", legacy.CompanionControlMode);
        legacy.CompanionControlMode = "external";
        Assert.Equal("external", SettingsClone.Clone(legacy).CompanionControlMode);
    }

    public static void ReleaseLayoutsUpgradeOnlyUnmodifiedManagedCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "release");
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "STS2AIAgent.dll"), "dll-v1");
            File.WriteAllText(Path.Combine(source, "STS2AIAgent.pck"), "pck-v1");
            File.WriteAllText(Path.Combine(source, "mod_id.json"), "{\"id\":\"STS2AIAgent\",\"version\":\"1\"}");
            var installed = CompanionModStager.Prepare(source, game);
            Assert.True(File.Exists(Path.Combine(installed, "STS2AIAgent.json")));
            File.WriteAllText(Path.Combine(source, "STS2AIAgent.dll"), "dll-v2");
            Assert.Equal(installed, CompanionModStager.Prepare(source, game));
            Assert.Equal("dll-v2", File.ReadAllText(Path.Combine(installed, "STS2AIAgent.dll")));
            File.Move(Path.Combine(source, "mod_id.json"), Path.Combine(source, "STS2AIAgent.json"));
            Assert.Equal(installed, CompanionModStager.Prepare(source, game));
            File.Delete(Path.Combine(installed, CompanionModStager.Marker));
            File.WriteAllText(Path.Combine(source, "STS2AIAgent.dll"), "dll-v3");
            var rejected = false;
            try { CompanionModStager.Prepare(source, game); }
            catch (IOException) { rejected = true; }
            Assert.True(rejected);
            Assert.Equal("dll-v2", File.ReadAllText(Path.Combine(installed, "STS2AIAgent.dll")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void NativeLoadGuardLeavesSteamAndUnknownSavesAlone()
    {
        Assert.True(CoopSavePrecheckPolicy.NeedsLocalContinue(new ulong[] { 1, 2 }, 76561198000000000, 1, 2));
        Assert.True(CoopSavePrecheckPolicy.NeedsLocalContinue(new ulong[] { 41, 42, 43 }, 76561198000000000, 41, 42));
        Assert.False(CoopSavePrecheckPolicy.NeedsLocalContinue(new ulong[] { 1, 2 }, 1, 1, 2));
        Assert.False(CoopSavePrecheckPolicy.NeedsLocalContinue(new ulong[] { 76561198000000000, 76561198000000001 }, 76561198000000000, 1, 2));
        Assert.False(CoopSavePrecheckPolicy.NeedsLocalContinue(Array.Empty<ulong>(), 76561198000000000, 1, 2));
    }

    public static async Task LiveSettingsRequireExplicitFullAcknowledgement()
    {
        foreach (var acknowledged in new[] { false, true })
        {
            using var http = new HttpClient(new SettingsHandler(acknowledged));
            var connection = new CompanionConnection(8081, 123, new string('A', 64), http);
            var rejected = false;
            try { await connection.ApplyJevSettingsAsync(CompanionSettingsPatch.From(AgentSettings.CreateDefault()), CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.Equal(!acknowledged, rejected);
        }
    }

    private sealed class SettingsHandler(bool acknowledged) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object data;
            if (request.Method == HttpMethod.Get)
                data = new { service = "sts2-ai-agent", status = "ready", instance_role = "companion", api_port = 8081, process_id = 123 };
            else
            {
                Assert.Equal(new string('A', 64), request.Headers.GetValues(CompanionConnection.TokenHeader).Single());
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.True(body.RootElement.GetProperty("settings").TryGetProperty("play_settings", out _));
                data = new { settings_applied = true, play_settings_applied = acknowledged, dual_layer_coop_enabled = false };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { ok = true, data })) };
        }
    }
}
