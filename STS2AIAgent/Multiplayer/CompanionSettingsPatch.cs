using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using STS2AIAgent.Config;

namespace STS2AIAgent.Multiplayer;

/// <summary>Whitelisted play and Jev settings accepted from the authenticated host.</summary>
/// <remarks>The API key is a transient input: never include this object in logs, responses or diagnostics.</remarks>
internal sealed class CompanionSettingsPatch
{
    [JsonPropertyName("dual_layer_coop_enabled")]
    public bool DualLayerCoopEnabled { get; init; }

    [JsonPropertyName("jev_base_url")]
    public string JevBaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("jev_api_key")]
    public string JevApiKey { get; init; } = string.Empty;

    [JsonPropertyName("jev_model")]
    public string JevModel { get; init; } = string.Empty;

    [JsonPropertyName("jev_confidence_threshold")]
    public double JevConfidenceThreshold { get; init; }

    [JsonPropertyName("jev_request_timeout_seconds")]
    public int? JevRequestTimeoutSeconds { get; init; }

    [JsonPropertyName("play_settings")]
    public CompanionPlaySettings? PlaySettings { get; init; }

    public static CompanionSettingsPatch From(AgentSettings settings) => new()
    {
        PlaySettings = CompanionPlaySettings.From(settings),
        DualLayerCoopEnabled = settings.DualLayerCoopEnabled,
        JevBaseUrl = settings.JevBaseUrl,
        JevApiKey = settings.JevApiKey,
        JevModel = settings.JevModel,
        JevConfidenceThreshold = settings.JevConfidenceThreshold,
        JevRequestTimeoutSeconds = settings.JevRequestTimeoutSeconds
    };

    /// <summary>Comparison key only; never export the serialized patch or its key.</summary>
    public string Fingerprint()
    {
        var content = System.Text.Json.JsonSerializer.Serialize(new
        {
            DualLayerCoopEnabled, JevBaseUrl, JevApiKey, JevModel, JevConfidenceThreshold, JevRequestTimeoutSeconds, PlaySettings
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    public bool HasSameValues(CompanionSettingsPatch other) =>
        other != null && DualLayerCoopEnabled == other.DualLayerCoopEnabled
        && JevBaseUrl == other.JevBaseUrl && JevApiKey == other.JevApiKey && JevModel == other.JevModel
        && JevConfidenceThreshold == other.JevConfidenceThreshold
        && JevRequestTimeoutSeconds == other.JevRequestTimeoutSeconds
        && System.Text.Json.JsonSerializer.Serialize(PlaySettings) == System.Text.Json.JsonSerializer.Serialize(other.PlaySettings);

    public bool IsValid => JevBaseUrl is { Length: <= 2048 } && JevApiKey is { Length: <= 4096 }
        && JevModel is { Length: <= 256 } && double.IsFinite(JevConfidenceThreshold)
        && JevConfidenceThreshold is >= 0 and <= 1
        && (JevRequestTimeoutSeconds is null or (> 0 and <= 3600))
        && (PlaySettings == null || PlaySettings.IsValid);

    public void ApplyTo(AgentSettings settings)
    {
        if (!IsValid) throw new ArgumentException("Invalid companion Jev settings.");
        PlaySettings?.ApplyTo(settings);
        settings.DualLayerCoopEnabled = DualLayerCoopEnabled;
        settings.JevBaseUrl = JevBaseUrl;
        settings.JevApiKey = JevApiKey;
        settings.JevModel = JevModel;
        settings.JevConfidenceThreshold = JevConfidenceThreshold;
        settings.JevRequestTimeoutSeconds = JevRequestTimeoutSeconds;
    }
}
