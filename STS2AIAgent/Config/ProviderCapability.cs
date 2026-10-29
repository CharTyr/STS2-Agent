using STS2AIAgent.Llm;

namespace STS2AIAgent.Config;

/// <summary>
/// What the per-model test learnt about a provider route, and how the play loop reads it back (W7).
/// </summary>
/// <remarks>
/// A gateway that truncates streamed tool arguments does it on every request, so the capability has
/// to outlive a single turn: <see cref="AgentSettings.ModelTests"/> is already keyed by model id and
/// a fingerprint of endpoint + model + key + protocol, which is exactly the identity that invalidates
/// it when the player edits the route. The play loop reads the record to decide whether an act turn
/// should stream at all, and writes back when a live truncation proves the probe wrong.
/// </remarks>
internal static class ProviderCapability
{
    /// <summary>Version of the probing this record's stream verdict came from.</summary>
    public const int ProbeVersion = 1;

    /// <summary>The provider was never probed for this capability.</summary>
    public const string Unknown = "unknown";

    /// <summary>Streamed tool arguments arrived complete.</summary>
    public const string Supported = "supported";

    /// <summary>Streamed tool arguments were truncated, missing fields, or absent.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>
    /// The record for this exact route, or null when it was never tested or the route changed.
    /// </summary>
    public static ModelTestRecord? Find(AgentSettings settings, ResolvedModel model)
    {
        if (settings.ModelTests is not { Count: > 0 } || string.IsNullOrWhiteSpace(model.Model.Id))
        {
            return null;
        }

        var fingerprint = ModelRoleProbe.Fingerprint(model);
        return settings.ModelTests.FirstOrDefault(record =>
            record != null
            && string.Equals(record.ModelId, model.Model.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether this route proved that streamed act arguments cannot be trusted.</summary>
    public static bool StreamToolArgsUnreliable(AgentSettings settings, ResolvedModel model) =>
        string.Equals(Find(settings, model)?.StreamToolArgs, Unsupported, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The output-cap field this route accepts. The probe records it; an untested route keeps the
    /// broadly compatible <c>max_tokens</c> and the client still renames it once if the server says so.
    /// </summary>
    public static string MaxTokensFieldFor(AgentSettings settings, ResolvedModel model) =>
        string.Equals(Find(settings, model)?.MaxTokensField, MaxTokensField.CompletionTokens,
            StringComparison.OrdinalIgnoreCase)
            ? MaxTokensField.CompletionTokens
            : MaxTokensField.Standard;

    /// <summary>
    /// Writes back "streamed tool arguments are unusable" for this route, creating a record when the
    /// player never pressed Test for it. Returns false when it was already recorded, so the caller
    /// does not persist the same fact twice.
    /// </summary>
    public static bool MarkStreamToolArgsUnsupported(AgentSettings settings, ResolvedModel model)
    {
        settings.ModelTests ??= new List<ModelTestRecord>();
        var record = Find(settings, model);
        if (record == null)
        {
            record = new ModelTestRecord
            {
                ModelId = model.Model.Id,
                Fingerprint = ModelRoleProbe.Fingerprint(model),
                // Learning a stream limitation says nothing about whether the model plays: keep the
                // badge honest rather than promoting an untested route to verified.
                Status = "unverified",
                Tools = "unknown"
            };
            settings.ModelTests.Add(record);
        }

        var changed = !string.Equals(record.StreamToolArgs, Unsupported, StringComparison.OrdinalIgnoreCase);
        record.StreamToolArgs = Unsupported;
        record.ProbeVersion = ProbeVersion;
        record.TestedAt ??= DateTimeOffset.UtcNow.ToString("o");
        return changed;
    }

    /// <summary>Maps a streaming probe verdict onto the persisted capability strings.</summary>
    public static string FromStreamVerdict(StreamingToolArgsVerdict verdict) => verdict switch
    {
        StreamingToolArgsVerdict.Complete => Supported,
        StreamingToolArgsVerdict.Incomplete or StreamingToolArgsVerdict.NoToolCall
            or StreamingToolArgsVerdict.Unsupported => Unsupported,
        _ => Unknown
    };

    /// <summary>Repairs a record read from a hand-edited or older settings file.</summary>
    public static void NormalizeRecord(ModelTestRecord record)
    {
        record.ModelId = record.ModelId?.Trim() ?? string.Empty;
        record.StreamToolArgs = NormalizeStreamToolArgs(record.StreamToolArgs);
        record.MaxTokensField = NormalizeMaxTokensField(record.MaxTokensField);
        if (record.ProbeVersion < 0)
        {
            record.ProbeVersion = 0;
        }
    }

    private static string NormalizeStreamToolArgs(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is Supported or Unsupported ? trimmed : Unknown;
    }

    private static string? NormalizeMaxTokensField(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, MaxTokensField.CompletionTokens, StringComparison.OrdinalIgnoreCase))
        {
            return MaxTokensField.CompletionTokens;
        }

        return string.Equals(trimmed, MaxTokensField.Standard, StringComparison.OrdinalIgnoreCase)
            ? MaxTokensField.Standard
            : null;
    }
}
