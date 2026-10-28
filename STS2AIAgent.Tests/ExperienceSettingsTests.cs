using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// The overhaul switches: on by default, survive a legacy file, normalise junk, survive the settings
/// editor's clone, and reach the teammate only where the teammate plays by them.
/// </summary>
internal static class ExperienceSettingsTests
{
    private static readonly JsonSerializerOptions StoreLike = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void DefaultsAreOn()
    {
        var settings = AgentSettings.CreateDefault();
        settings.EnsureValidShape();
        Assert.Equal(AgentSettings.TurboInstant, settings.TurboMode);
        Assert.True(settings.AutopilotEnabled);
        Assert.True(settings.TurnPlansEnabled);
        Assert.True(settings.CombatMathEnabled);
        Assert.Equal(AgentSettings.VisionOnDemand, settings.VisionMode);
        Assert.Equal(AgentSettings.HudFull, settings.HudMode);
        Assert.Equal("F7", settings.PauseHotkey);
        Assert.True(settings.RunJournalEnabled);
        Assert.Null(settings.TurboRestoreFastMode);
    }

    public static void LegacyFileGetsTheNewDefaults()
    {
        // A v0.17.0 file has none of the new keys.
        const string legacy = """{"endpoints":[{"id":"e","baseUrl":"https://x/v1","apiKey":"k","enabled":true}],"models":[{"id":"m","endpointId":"e","model":"x"}],"conversationModelId":"m"}""";
        var settings = JsonSerializer.Deserialize<AgentSettings>(legacy, StoreLike)!;
        settings.EnsureValidShape();
        Assert.Equal(AgentSettings.TurboInstant, settings.TurboMode);
        Assert.True(settings.AutopilotEnabled);
        Assert.True(settings.TurnPlansEnabled);
        Assert.Equal(AgentSettings.VisionOnDemand, settings.VisionMode);
    }

    public static void JunkIsNormalised()
    {
        var settings = AgentSettings.CreateDefault();
        settings.TurboMode = " FAST ";
        settings.VisionMode = "sometimes";
        settings.HudMode = null!;
        settings.PauseHotkey = null!;
        settings.TurboRestoreFastMode = "Turbo";
        settings.EnsureValidShape();
        Assert.Equal(AgentSettings.TurboFast, settings.TurboMode);
        Assert.Equal(AgentSettings.VisionOnDemand, settings.VisionMode);
        Assert.Equal(AgentSettings.HudFull, settings.HudMode);
        Assert.Equal(string.Empty, settings.PauseHotkey);
        Assert.Null(settings.TurboRestoreFastMode);

        settings.TurboRestoreFastMode = " Normal ";
        settings.EnsureValidShape();
        Assert.Equal("Normal", settings.TurboRestoreFastMode);
    }

    public static void CloneCarriesEveryNonDefault()
    {
        var source = AgentSettings.CreateDefault();
        source.TurboMode = AgentSettings.TurboOff;
        source.TurboRestoreFastMode = "Fast";
        source.AutopilotEnabled = false;
        source.TurnPlansEnabled = false;
        source.CombatMathEnabled = false;
        source.VisionMode = AgentSettings.VisionEveryStep;
        source.HudMode = AgentSettings.HudMinimal;
        source.PauseHotkey = "F6";
        source.RunJournalEnabled = false;

        var clone = SettingsClone.Clone(source);
        Assert.Equal(AgentSettings.TurboOff, clone.TurboMode);
        Assert.Equal("Fast", clone.TurboRestoreFastMode);
        Assert.False(clone.AutopilotEnabled);
        Assert.False(clone.TurnPlansEnabled);
        Assert.False(clone.CombatMathEnabled);
        Assert.Equal(AgentSettings.VisionEveryStep, clone.VisionMode);
        Assert.Equal(AgentSettings.HudMinimal, clone.HudMode);
        Assert.Equal("F6", clone.PauseHotkey);
        Assert.False(clone.RunJournalEnabled);
    }

    public static void TeammateGetsPlaySwitchesButNotLocalOnes()
    {
        var host = AgentSettings.CreateDefault();
        host.TurboMode = AgentSettings.TurboFast;
        host.AutopilotEnabled = false;
        host.TurnPlansEnabled = false;
        host.CombatMathEnabled = false;
        host.VisionMode = AgentSettings.VisionOff;
        host.RunJournalEnabled = false;
        host.HudMode = AgentSettings.HudOff;
        host.PauseHotkey = "F6";
        host.TurboRestoreFastMode = "Normal";

        var wire = JsonSerializer.Serialize(CompanionPlaySettings.From(host));
        var received = JsonSerializer.Deserialize<CompanionPlaySettings>(wire)!;
        Assert.True(received.IsValid);

        var companion = AgentSettings.CreateDefault();
        received.ApplyTo(companion);
        Assert.Equal(AgentSettings.TurboFast, companion.TurboMode);
        Assert.False(companion.AutopilotEnabled);
        Assert.False(companion.TurnPlansEnabled);
        Assert.False(companion.CombatMathEnabled);
        Assert.Equal(AgentSettings.VisionOff, companion.VisionMode);
        Assert.False(companion.RunJournalEnabled);
        // Instance-local: the teammate window has no overlay and owns its own speed restore record.
        Assert.Equal(AgentSettings.HudFull, companion.HudMode);
        Assert.Equal("F7", companion.PauseHotkey);
        Assert.Null(companion.TurboRestoreFastMode);
        Assert.False(wire.Contains("TurboRestoreFastMode", StringComparison.Ordinal), wire);
    }

    public static void TeammateRejectsAnUnknownSpeed()
    {
        Assert.False(new CompanionPlaySettings { TurboMode = "ludicrous" }.IsValid);
        Assert.False(new CompanionPlaySettings { VisionMode = "always" }.IsValid);
        // A payload from a sender that predates the switches keeps the shipped defaults.
        var old = JsonSerializer.Deserialize<CompanionPlaySettings>("""{"ReplyLanguage":"auto"}""")!;
        Assert.True(old.IsValid);
        Assert.Equal(AgentSettings.TurboInstant, old.TurboMode);
        Assert.True(old.AutopilotEnabled);
    }
}
