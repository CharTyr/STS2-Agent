using STS2AIAgent.Config;

namespace STS2AIAgent.Multiplayer;

internal enum CompanionSettingsSyncOutcome { Disconnected, Confirmed, Unconfirmed, Superseded }

internal readonly record struct CompanionSettingsSnapshot(CompanionConnection? Connection, AgentSettings Settings);

/// <summary>Serializes settings pushes and only caches an acknowledgement for its original snapshot.</summary>
internal sealed class CompanionSettingsSynchronizer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Confirmation(CompanionConnection Connection, string Fingerprint);
    private volatile Confirmation? _confirmed;

    public void Reset() => _confirmed = null;

    public async Task<CompanionSettingsSyncOutcome> SynchronizeAsync(
        Func<CompanionSettingsSnapshot> readCurrent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Read after acquiring the gate: queued saves coalesce to the latest configuration.
            var requested = readCurrent();
            if (requested.Connection == null)
            {
                Reset();
                return CompanionSettingsSyncOutcome.Disconnected;
            }
            var patch = CompanionSettingsPatch.From(requested.Settings);
            var fingerprint = patch.Fingerprint();
            var confirmed = _confirmed;
            if (confirmed != null && ReferenceEquals(confirmed.Connection, requested.Connection) && confirmed.Fingerprint == fingerprint)
                return CompanionSettingsSyncOutcome.Confirmed;

            Reset();
            var enabled = await requested.Connection.ApplyJevSettingsAsync(patch, cancellationToken);
            var current = readCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(current.Connection, requested.Connection)
                || fingerprint != CompanionSettingsPatch.From(current.Settings).Fingerprint())
                return CompanionSettingsSyncOutcome.Superseded;
            if (enabled == patch.DualLayerCoopEnabled)
                _confirmed = new Confirmation(requested.Connection, fingerprint);
            return enabled == patch.DualLayerCoopEnabled
                ? CompanionSettingsSyncOutcome.Confirmed : CompanionSettingsSyncOutcome.Unconfirmed;
        }
        finally { _gate.Release(); }
    }
}
