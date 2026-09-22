using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The per-model verification the settings page's per-card test button drives: a real connectivity
/// ping plus a real tool-calling probe, recorded per model so the card can wear its own badge.
/// </summary>
/// <remarks>
/// Split from <c>AgentRuntime.cs</c> for the same reason as the other partials: the base file is at
/// its size budget, and the per-model probe grows with the settings redesign rather than with the
/// runtime's session plumbing.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>
    /// Tests one configured model end to end: first a chat ping, then a tool-calling probe whose
    /// only correct answer is a tool call. The result is recorded against the model (and the
    /// model's <c>SupportsTools</c> flag is corrected to the probe's answer), so the badge on the
    /// card is the truth about this model, not a role summary lines away.
    /// </summary>
    public async Task<string> TestModelAsync(string modelId, CancellationToken cancellationToken)
    {
        AgentSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        var model = settings.FindModel(modelId);
        if (model == null)
        {
            return Loc.T("找不到该模型。");
        }

        var endpoint = settings.FindEndpoint(model.EndpointId);
        if (endpoint == null || !endpoint.Enabled)
        {
            return Loc.T("该模型绑定的端点不可用。");
        }

        var resolved = new ResolvedModel(endpoint, model);
        var fingerprint = ModelRoleProbe.Fingerprint(resolved);
        var record = new ModelTestRecord
        {
            ModelId = model.Id,
            Fingerprint = fingerprint,
            TestedAt = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            var client = _loop.CreateProbeClient(endpoint);
            await client.PingAsync(model.Model, cancellationToken);
            var tools = await client.ProbeToolCallingAsync(model.Model, cancellationToken);
            record.Status = "verified";
            record.Tools = tools ? "supported" : "unsupported";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            record.Status = "failed";
            record.Tools = "unknown";
            record.Error = DiagnosticExport.Redact(ex.Message);
        }

        lock (_gate)
        {
            var current = _settings;
            current.ModelTests.RemoveAll(item => string.Equals(item.ModelId, model.Id, StringComparison.OrdinalIgnoreCase));
            current.ModelTests.Add(record);
            var target = current.FindModel(model.Id);
            if (target != null && record.Status == "verified")
            {
                // The probe is the authority: a model that cannot call tools cannot play, and the
                // checkbox it replaces used to let the player claim otherwise.
                target.SupportsTools = record.Tools == "supported";
            }

            try
            {
                SaveSettings(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NoteEvent("model test result could not be saved: " + ex.Message);
            }
        }

        RaiseChanged();
        return record.Status == "verified"
            ? record.Tools == "supported"
                ? Loc.T("连通成功，工具调用可用。")
                : Loc.T("连通成功，但该模型未通过工具调用检测，无法用于自动游玩。")
            : Loc.T("测试失败：{0}", record.Error);
    }

    /// <summary>The stored test record for a model, or null when it was never tested.</summary>
    public ModelTestRecord? ModelTestFor(string modelId)
    {
        lock (_gate)
        {
            return _settings.ModelTests.FirstOrDefault(item =>
                string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// True when the model's stored record is a fresh match for its current configuration -- the
    /// badge's condition. Editing the endpoint, model name or key changes the fingerprint and the
    /// badge goes back to unverified.
    /// </summary>
    public bool IsModelVerified(string modelId)
    {
        lock (_gate)
        {
            var record = _settings.ModelTests.FirstOrDefault(item =>
                string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
            if (record == null || record.Status != "verified")
            {
                return false;
            }

            var model = _settings.FindModel(modelId);
            var endpoint = model == null ? null : _settings.FindEndpoint(model.EndpointId);
            if (model == null || endpoint == null)
            {
                return false;
            }

            return string.Equals(
                record.Fingerprint,
                ModelRoleProbe.Fingerprint(new ResolvedModel(endpoint, model)),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
