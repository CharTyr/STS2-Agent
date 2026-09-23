using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    private readonly SemaphoreSlim _companionSettingsGate = new(1, 1);

    /// <summary>Applies a bounded settings-only patch on the companion; the caller authenticates first.</summary>
    /// <remarks>SaveSettings persists a clone before publishing it to readers. Nothing logs the patch.</remarks>
    public bool ApplyCompanionSettings(CompanionSettingsPatch patch)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion accepts live settings.");
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.IsValid) throw new ArgumentException("Invalid companion Jev settings.", nameof(patch));
        if (!_companionSettingsGate.Wait(0)) throw new InvalidOperationException("Companion settings update already in progress.");
        try
        {
            // SaveSettings writes the clone before publishing it to concurrent readers. This gate
            // serializes incoming patches; it is not held across arbitrary cancellation callbacks.
            var copy = SettingsClone.Clone(Settings);
            patch.ApplyTo(copy);
            SaveSettings(copy);
            return copy.DualLayerCoopEnabled;
        }
        finally { _companionSettingsGate.Release(); }
    }

    /// <summary>Settings sync status for the host. No secret is included in either property.</summary>
    public CompanionJevSnapshot CompanionJevStatus()
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion exposes this reading.");
        lock (_gate)
        {
            return new CompanionJevSnapshot
            {
                Choice = LastJevChoice,
                Probabilities = LastJevProbabilities,
                Danger = LastJevDanger,
                Latency = LastJevLatency,
                DualLayerCoopEnabled = _settings.DualLayerCoopEnabled
            };
        }
    }
}
