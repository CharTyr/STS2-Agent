using STS2AIAgent.Config;

namespace STS2AIAgent.Tests;

internal static class SettingsExperienceRegressionTests
{
    public static void EndpointRemovalBlocksDisabledEndpointReferences()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].Enabled = false;

        var impact = SettingsBinding.EndpointRemoval(settings, settings.Endpoints[0].Id);

        Assert.True(impact.Blocked);
        Assert.Contains("模型绑定此端点", impact.Message);
    }

    public static void EndpointRemovalIncludesFallbackPlayReference()
    {
        var settings = AgentSettings.CreateDefault();
        settings.PlayModelId = null;

        var impact = SettingsBinding.EndpointRemoval(settings, settings.Endpoints[0].Id);

        Assert.True(impact.Blocked);
        Assert.Contains("对话模型", impact.Message);
        Assert.Contains("游玩模型", impact.Message);
    }

    public static void ModelRemovalReportsEveryRoleReference()
    {
        var settings = AgentSettings.CreateDefault();
        var model = settings.Models[0];
        settings.VisionModelId = model.Id;
        settings.PlayModelId = model.Id;

        var impact = SettingsBinding.ModelRemoval(settings, model.Id);

        Assert.True(impact.Blocked);
        Assert.Contains("对话模型", impact.Message);
        Assert.Contains("游玩模型", impact.Message);
        Assert.Contains("视觉模型", impact.Message);
    }

    public static void ModelRemovalAllowsUnreferencedModel()
    {
        var settings = AgentSettings.CreateDefault();
        var unreferenced = new LlmModelConfig
        {
            EndpointId = settings.Endpoints[0].Id,
            Model = "unreferenced-model"
        };
        settings.Models.Add(unreferenced);

        var impact = SettingsBinding.ModelRemoval(settings, unreferenced.Id);

        Assert.False(impact.Blocked);
    }
}
