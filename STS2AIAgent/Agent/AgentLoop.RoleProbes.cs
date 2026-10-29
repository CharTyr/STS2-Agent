using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The footer "test connection" path: one ping per configured role, recorded as role test records.
/// </summary>
/// <remarks>
/// Moved out of <c>AgentLoop.cs</c> on 2026-10-28 (bold overhaul, W0) without changing a line of
/// behavior: that file sat exactly at its 1,000-line budget and the decision loop needs room for the
/// forced-move, plan and intent seams. Nothing here takes part in a play turn.
/// </remarks>
internal sealed partial class AgentLoop
{
    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var results = await TestConfiguredRolesAsync(force: true, cancellationToken);
        var play = results.FirstOrDefault(item => item.Role == ModelRoleNames.Play);
        return play.Record.Status == "verified" ? "ok" : play.Record.Error ?? "failed";
    }

    public async Task<IReadOnlyList<ModelRoleProbeResult>> TestConfiguredRolesAsync(bool force, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var results = new List<ModelRoleProbeResult>();
        var cache = new Dictionary<string, ModelRoleTestRecord>(StringComparer.Ordinal);
        foreach (var role in new[] { ModelRoleNames.Conversation, ModelRoleNames.Play, ModelRoleNames.Vision })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ModelRoleProbe.Resolve(settings, role);
            if (role == ModelRoleNames.Vision && resolved == null)
            {
                results.Add(new ModelRoleProbeResult(role, ModelRoleProbe.Unused(role), false));
                continue;
            }

            if (resolved == null)
            {
                results.Add(new ModelRoleProbeResult(role, ModelRoleProbe.Unverified(role, null), false));
                continue;
            }

            var current = ModelRoleProbe.Current(settings, role);
            var fingerprint = ModelRoleProbe.Fingerprint(resolved);
            if (!force && current.Status == "verified" && current.Fingerprint == fingerprint)
            {
                results.Add(new ModelRoleProbeResult(role, current, true));
                continue;
            }

            if (cache.TryGetValue(fingerprint, out var cached))
            {
                results.Add(new ModelRoleProbeResult(role, CopyForRole(cached, role), false));
                continue;
            }

            try
            {
                var client = CreateClient(resolved.Endpoint);
                await client.PingAsync(resolved.Model.Model, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var record = ModelRoleProbe.FromSuccess(role, resolved);
                cache[fingerprint] = record;
                results.Add(new ModelRoleProbeResult(role, record, false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var record = ModelRoleProbe.FromException(role, resolved, ex);
                cache[fingerprint] = record;
                results.Add(new ModelRoleProbeResult(role, record, false));
            }
        }

        return results;
    }

    private static ModelRoleTestRecord CopyForRole(ModelRoleTestRecord source, string role)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = source.Status,
            CapabilityStatus = source.CapabilityStatus,
            EndpointId = source.EndpointId,
            EndpointName = source.EndpointName,
            ModelId = source.ModelId,
            ModelName = source.ModelName,
            Fingerprint = source.Fingerprint,
            StatusCode = source.StatusCode,
            Error = source.Error,
            NextStep = source.NextStep,
            TestedAt = source.TestedAt
        };
    }
}
