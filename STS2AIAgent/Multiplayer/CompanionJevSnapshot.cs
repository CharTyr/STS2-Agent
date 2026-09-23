using System.Text.Json.Serialization;

namespace STS2AIAgent.Multiplayer;

/// <summary>Non-secret execution reading returned by the token-protected companion status route.</summary>
internal sealed record CompanionJevSnapshot
{
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    [JsonPropertyName("probabilities")]
    public string? Probabilities { get; init; }

    [JsonPropertyName("danger")]
    public string? Danger { get; init; }

    [JsonPropertyName("latency")]
    public string? Latency { get; init; }

    [JsonPropertyName("dual_layer_coop_enabled")]
    public bool DualLayerCoopEnabled { get; init; }
}
