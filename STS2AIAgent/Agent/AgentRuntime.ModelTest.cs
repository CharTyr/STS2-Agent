using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The per-model verification the settings page's per-card test button drives: a real connectivity
/// ping plus a real tool-calling probe, recorded per model so the card can wear its own badge.
/// </summary>
/// <remarks>
/// Its own partial because of what these members touch: the settings model list, the probe client,
/// and the per-model test records -- nothing in the play loop or the session plumbing reaches them,
/// and they reach nothing back except a settings save.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>
    /// The miniature play decision the streaming probe sends. It asks for a <c>play_card</c> with both
    /// index fields: a gateway that truncates the streamed arguments after the action name produces a
    /// valid JSON object with neither field, which is exactly the failure players hit.
    /// </summary>
    private const string StreamProbePrompt =
        "You are playing Slay the Spire 2. Current state: COMBAT, 3 energy. Hand: card_index 0 is "
        + "Strike and playable; there is one enemy at target_index 0 with 12 HP. Available actions: "
        + "[\"play_card\"]. Call the act tool with action \"play_card\", card_index 0, target_index 0 "
        + "and a one-sentence reason. Do not answer with plain text.";

    private static readonly string[] StreamProbeRequiredFields = { "action", "card_index", "target_index" };

    /// <summary>
    /// Tests one configured model end to end: first a chat ping, then a tool-calling probe whose
    /// only correct answer is a tool call. The result is recorded against the model (and the
    /// model's <c>SupportsTools</c> flag is corrected to the probe's answer), so the badge on the
    /// card is the truth about this model, not a role summary lines away.
    /// </summary>
    private readonly SemaphoreSlim _modelVerificationGate = new(1, 1);

    public async Task<string> TestModelAsync(string modelId, CancellationToken cancellationToken)
    {
        if (!await _modelVerificationGate.WaitAsync(0, cancellationToken))
            return Loc.T("已有测试在进行，请稍候或取消。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await TestModelCoreAsync(modelId, deadline.Token); }
        finally { _modelVerificationGate.Release(); }
    }

    private async Task<string> TestModelCoreAsync(string modelId, CancellationToken cancellationToken)
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
        Exception? failure = null;

        try
        {
            var client = _loop.CreateClient(endpoint);
            await client.PingAsync(model.Model, cancellationToken);
            // The probe is a miniature play decision against the real act tool: a mock combat frame
            // whose only sensible answer is calling act with end_turn. A provider that passes the
            // ping but cannot drive the actual play schema fails here, which is the difference
            // between "can chat" and "can play".
            var tools = await client.ProbeToolCallingAsync(
                model.Model,
                AgentTools.Play[^1],
                "You are playing Slay the Spire 2. Current state: COMBAT, you have 0 energy, your hand "
                + "is empty, one enemy has 12 HP. Available actions: [\"end_turn\"]. Call the act tool "
                + "with action \"end_turn\" and a one-sentence reason. Do not answer with plain text.",
                cancellationToken);
            record.Status = "verified";
            // A clean "no tool call came back" is the only unsupported verdict: the provider took the
            // request and chose not to call. A thrown 4xx means the schema itself was rejected. Both
            // differ from a transient failure, which must not downgrade a working model.
            record.Tools = tools ? "supported" : "unsupported";
            if (record.Tools == "supported")
            {
                // One extra request, and only for a model that can call tools: the same decision sent
                // down the streaming path, because a gateway can truncate the streamed arguments while
                // its non-streaming body is complete (measured live 2026-10-28).
                var streamed = await ProbeStreamToolArgsAsync(client, model.Model, cancellationToken);
                if (streamed?.Verdict is { } verdict && verdict != StreamingToolArgsVerdict.NotRun)
                {
                    record.StreamToolArgs = ProviderCapability.FromStreamVerdict(verdict);
                    record.MaxTokensField = MaxTokensFieldForProbe(streamed);
                    record.ProbeVersion = ProviderCapability.ProbeVersion;
                }

                if (!string.IsNullOrWhiteSpace(streamed?.Error))
                {
                    NoteEvent("stream capability probe: " + DiagnosticExport.Redact(streamed.Error));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LlmToolProbeUnsupportedException ex)
        {
            record.Status = "verified";
            record.Tools = "unsupported";
            record.Error = DiagnosticExport.Redact(ex.Message);
        }
        catch (Exception ex)
        {
            record.Status = "failed";
            record.Tools = "unknown";
            record.Error = DiagnosticExport.Redact(ex.Message);
            failure = ex;
        }

        var playRoleVerified = false;
        lock (_gate)
        {
            var current = _settings;
            var target = current.FindModel(model.Id);
            var currentEndpoint = target == null ? null : current.FindEndpoint(target.EndpointId);
            if (target == null || currentEndpoint == null || !currentEndpoint.Enabled
                || !string.Equals(ModelRoleProbe.Fingerprint(new ResolvedModel(currentEndpoint, target)), fingerprint,
                    StringComparison.OrdinalIgnoreCase))
                return Loc.T("配置已变更，请重新测试。");

            current.ModelTests.RemoveAll(item => string.Equals(item.ModelId, model.Id, StringComparison.OrdinalIgnoreCase));
            current.ModelTests.Add(record);
            // The probe is the authority only when it actually answered: a transient failure leaves
            // SupportsTools where it was rather than downgrading a working model on a bad day.
            if (record.Status == "verified")
            {
                target.SupportsTools = record.Tools == "supported";
            }

            // The card test is the one the first-run guide tells the player to run, so it must also
            // settle the role records the invite gate, the teammate resume gate and the first-run
            // verdict read. Without this a green badge still launched a teammate that never played.
            foreach (var role in new[] { ModelRoleNames.Conversation, ModelRoleNames.Play, ModelRoleNames.Vision })
            {
                var roleModel = ModelRoleProbe.Resolve(current, role);
                if (roleModel == null
                    || !string.Equals(ModelRoleProbe.Fingerprint(roleModel), fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (record.Status == "verified")
                {
                    ModelRoleProbe.Upsert(current, ModelRoleProbe.FromSuccess(role, roleModel, record.Tools == "supported"));
                    playRoleVerified |= role == ModelRoleNames.Play && record.Tools == "supported";
                }
                else if (failure != null)
                {
                    ModelRoleProbe.Upsert(current, ModelRoleProbe.FromException(role, roleModel, failure));
                }
            }
        }

        if (playRoleVerified)
        {
            ClearModelTestFailure();
            MarkFirstRunGuideSeen();
        }

        try
        {
            // The record was already written into the live settings under the gate; this persists
            // the same object. Done outside the gate: SaveSettings takes it and does file IO.
            SaveSettings(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteEvent("model test result could not be saved: " + ex.Message);
        }

        RaiseChanged();
        if (record.Status != "verified")
        {
            return Loc.T("测试失败：{0}", record.Error);
        }

        if (record.Tools != "supported")
        {
            return Loc.T("连通成功，但该模型未通过工具调用检测，无法用于自动游玩。");
        }

        // Telling the player about the slower-but-working path is the difference between "the test
        // said this is broken" and "the test fixed how it is asked".
        return record.StreamToolArgs == ProviderCapability.Unsupported
            ? Loc.T("连通成功，工具调用可用。此服务商的流式工具参数不完整，已改用非流式请求。")
            : Loc.T("连通成功，工具调用可用。");
    }

    /// <summary>
    /// Runs the streaming half of the capability probe. A test fake that does not implement
    /// <see cref="IStreamingToolProbe"/> returns null, which records nothing rather than claiming a
    /// verdict it never measured.
    /// </summary>
    private static async Task<StreamingToolArgsProbeResult?> ProbeStreamToolArgsAsync(
        ILlmClient client,
        string model,
        CancellationToken cancellationToken)
    {
        if (client is not IStreamingToolProbe probe)
        {
            return null;
        }

        try
        {
            return await probe.ProbeStreamingToolCallAsync(
                model,
                AgentTools.Play[^1],
                StreamProbePrompt,
                StreamProbeRequiredFields,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transient provider failure says nothing about streaming: keep the capability unknown
            // instead of downgrading a working model on a bad day.
            return new StreamingToolArgsProbeResult(StreamingToolArgsVerdict.NotRun, Error: ex.Message);
        }
    }

    /// <summary>
    /// Which output-cap field the probe proved for this route. A verdict that got as far as a tool call
    /// means <c>max_tokens</c> was accepted; a 400 naming the other field records that instead.
    /// </summary>
    private static string? MaxTokensFieldForProbe(StreamingToolArgsProbeResult result) => result.Verdict switch
    {
        StreamingToolArgsVerdict.Complete or StreamingToolArgsVerdict.Incomplete
            or StreamingToolArgsVerdict.NoToolCall => MaxTokensField.Standard,
        StreamingToolArgsVerdict.MaxTokensRejected =>
            result.MaxTokensParameter ?? MaxTokensField.CompletionTokens,
        _ => result.MaxTokensParameter
    };

    /// <summary>
    /// Persists the verdict a live truncation proved for this route (W7.3), so the next session starts
    /// non-streaming instead of paying for one broken stream per turn. Called once per route and
    /// session by <see cref="AgentLoop"/>; a provider that fixes its gateway gets re-probed by the
    /// model test, which replaces the same record.
    /// </summary>
    private void RecordStreamToolArgsUnsupported(ResolvedModel resolved)
    {
        var changed = false;
        lock (_gate)
        {
            var current = _settings;
            var model = current.FindModel(resolved.Model.Id);
            var endpoint = model == null ? null : current.FindEndpoint(model.EndpointId);
            if (model == null || endpoint == null)
            {
                return;
            }

            changed = ProviderCapability.MarkStreamToolArgsUnsupported(
                current, new ResolvedModel(endpoint, model));
        }

        if (!changed)
        {
            return;
        }

        try
        {
            // Written straight through instead of SaveSettings: this learning happens mid-turn and
            // must not cancel the strategy refresh or replace the settings object under the turn.
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteEvent("stream capability could not be saved: " + ex.Message);
        }

        RaiseChanged();
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
