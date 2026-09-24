using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>Persists completed turn facts separately from live autoplay UI updates.</summary>
/// <remarks>
/// This partial also owns the conversation stream the overlay renders: the history buffer, the cap
/// that trims it, and the reasoning/action turns a completed turn appends to it. They live here
/// because of what they touch -- every one of them is written by the same receipt path that accounts
/// a turn, and the history is itself a per-turn record.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>
    /// How many conversation turns are kept. The overlay renders at most this many, and the per-run
    /// session file persists the same tail, so "what is on screen" and "what a continued run gets
    /// back" are one number rather than two that drift.
    /// </summary>
    internal const int ChatHistoryLimit = PlaySessionStore.MaxChatTurns;

    /// <summary>How much of a turn's reasoning a single "thought" bubble carries.</summary>
    /// <remarks>
    /// The live stream bubble uses the same budget, deliberately: the bubble a player is watching grow
    /// becomes the recorded bubble, character for character, rather than a different clip appearing
    /// when the turn lands.
    /// </remarks>
    private const int ThoughtBubbleChars = LiveThoughtBuffer.MaxChars;

    /// <summary>How much of it the "action" bubble repeats beside the action name.</summary>
    private const int ActionBubbleReasonChars = 140;

    /// <summary>
    /// The reasoning of the request currently in flight, streamed from the provider as it arrives. This
    /// is display-only state that is never persisted and never replayed; see
    /// <see cref="LiveThoughtBuffer"/> for why it is emptied where it is.
    /// </summary>
    private readonly LiveThoughtBuffer _liveThought = new();

    /// <summary>Turns dropped off the front of the history, so the page can say how many.</summary>
    private int _historyTrimmed;

    /// <summary>The execution model's last reading, for the Jev panel. Null before its first turn.</summary>
    private string? _lastJevChoice;
    private string? _lastJevProbabilities;
    private string? _lastJevDanger;
    private string? _lastJevLatency;
    // Dual-layer split for the Jev panel's fallback rate, reset with the session stats.
    private long _jevAcceptedTurns;
    private long _jevFallbackTurns;

    /// <summary>How many earlier turns the cap has dropped; 0 means the log starts at the beginning.</summary>
    public int HistoryTrimmedCount
    {
        get { lock (_gate) return _historyTrimmed; }
    }

    /// <summary>What the execution layer last chose, as one line of display text.</summary>
    public string LastJevChoice => _lastJevChoice ?? "-";

    /// <summary>Its confidence and per-option scores, as one line of display text.</summary>
    public string LastJevProbabilities => _lastJevProbabilities ?? "-";

    public string LastJevDanger => _lastJevDanger ?? "-";

    public string LastJevLatency => _lastJevLatency ?? "-";

    private void RecordTurnReceipt(AgentTurnResult result, bool recordBudget = false)
    {
        AccountTurn(result, recordBudget);
        RecordJevReading(result);
        if (!string.IsNullOrWhiteSpace(result.Acted))
        {
            RecordDecision(
                // Only a dual-layer turn carries a confidence; that is how the log tells "Jev clicked
                // this" from "the LLM did", which is the whole observability contract for the mode.
                result.Confidence != null ? "jev" : "agent_loop",
                result.Acted,
                result.Reasoning,
                result.StateFingerprint,
                result.RequestsSpent,
                result.Usage?.TotalTokens,
                confidence: result.Confidence,
                optionIds: result.OfferedOptionIds,
                probabilities: result.Probabilities,
                danger: result.DangerScore,
                strategyUpdatedAt: result.StrategyUpdatedAt,
                // The LLM finishing a turn Jev could not commit is itself a Jev attempt: the empty
                // receipt already spent the request, and this row is where that attempt becomes visible.
                jevAttempt: result.Confidence == null && result.JevElapsedMilliseconds != null);
        }
    }

    /// <summary>
    /// Appends the turn's reasoning and its action to the conversation, so the page reads as the
    /// decision flow rather than as a chat the model had on the side.
    /// </summary>
    /// <remarks>
    /// The reasoning is gated on the player's own switch -- it is the model's private scratchpad and
    /// is long enough to bury the conversation when unwanted. The action is not: what the agent did
    /// is the record the player came to this page for. These append without notifying, because the
    /// caller repaints anyway (its own <c>AddHistory</c>, or the status line it sets next), and a
    /// turn should not cost three full refresh passes.
    ///
    /// The streamed partial is dropped here, before the recorded bubble is added: a turn that streamed
    /// its reasoning and then records it must not leave two reasoning bubbles in the log.
    /// </remarks>
    private void AppendTurnTraces(AgentTurnResult result)
    {
        _liveThought.Reset();

        if (ShowsThinkingInChat() && !string.IsNullOrWhiteSpace(result.Reasoning))
        {
            AddHistoryCore("thought", Clip(result.Reasoning, ThoughtBubbleChars), notify: false);
        }

        if (string.IsNullOrWhiteSpace(result.Acted))
        {
            return;
        }

        var reason = Clip(result.Reasoning, ActionBubbleReasonChars);
        var action = reason.Length == 0 ? result.Acted : result.Acted + " — " + reason;
        var outcome = DescribeActResult(result.ActResultJson);
        if (outcome.Length > 0) action += " · " + outcome;
        AddHistoryCore("action", action, notify: false);
    }

    /// <summary>
    /// Adds one turn to the conversation, trims the cap off the front, and repaints the page.
    /// </summary>
    private void AddHistory(string role, string text) => AddHistoryCore(role, text, notify: true);

    private void AddHistoryCore(string role, string text, bool notify)
    {
        lock (_gate)
        {
            _history.Add(new ChatTurn { Role = role, Text = text });
            if (_history.Count > ChatHistoryLimit)
            {
                var dropped = _history.Count - ChatHistoryLimit;
                _history.RemoveRange(0, dropped);
                _historyTrimmed += dropped;
            }

            _sessionDirty = true;
            _sessionRevision++;
        }

        if (notify)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// Drops the whole conversation at the player's request. It lives beside the history it clears --
    /// see this file's remarks -- because that is the state the Clear button owns.
    /// </summary>
    public void ClearChat()
    {
        lock (_gate)
        {
            ResetHistoryLocked();
            MarkSessionDirty();
        }

        FlushSessionIfDirty();
        RaiseChanged();
    }

    /// <summary>
    /// Empties the conversation and its trim count. Callers hold <c>_gate</c>: a clear is always part
    /// of a larger edit (a player's Clear, or a run boundary swapping the whole context).
    /// </summary>
    private void ResetHistoryLocked()
    {
        _history.Clear();
        _historyTrimmed = 0;
        // A cleared conversation, or a run boundary swapping the whole context, takes the in-flight
        // partial with it: nothing about the previous context is left to reappear under the next one.
        _liveThought.Reset();
    }

    private bool ShowsThinkingInChat()
    {
        lock (_gate)
        {
            return _settings.ShowThinkingInChat;
        }
    }

    /// <summary>
    /// The reasoning of the request in flight, so the conversation can draw it while the model is still
    /// thinking. Empty whenever nothing is streaming, which is the normal state outside a turn.
    /// </summary>
    public string LiveThought => _liveThought.Text;

    /// <summary>
    /// Takes one streamed reasoning partial from the model client. The player's own switch is the gate:
    /// with reasoning display off (the default) nothing is stored at all, so a thinking model's
    /// scratchpad neither reaches the overlay nor sits in memory for one.
    /// </summary>
    /// <remarks>
    /// Called from the provider's read loop, which is not the game thread. Nothing here touches a Godot
    /// node -- the overlay picks the text up on its own tick -- so the callback stays a plain field
    /// write and a measured setting read.
    /// </remarks>
    private void ReportReasoningDelta(string accumulated)
    {
        _liveThought.Report(accumulated, ShowsThinkingInChat());
    }

    /// <summary>
    /// Drops the streamed partial when a request ends without a recorded bubble to replace it (a pause,
    /// a cancel, a provider failure). Without this the panel would keep a "thinking…" bubble for a turn
    /// that is already over.
    /// </summary>
    private void ClearLiveThought()
    {
        _liveThought.Reset();
    }

    /// <summary>
    /// Keeps the Jev panel's reading current. The elapsed field identifies an attempted Jev turn
    /// even when a low-confidence choice handed the actual move to the regular model.
    /// </summary>
    private void RecordJevReading(AgentTurnResult result)
    {
        if (result.JevElapsedMilliseconds == null && result.Confidence == null)
        {
            return;
        }

        var choice = Clip(result.Acted ?? (result.JevElapsedMilliseconds == null ? result.Reasoning : null), 90);
        var probabilities = FormatJevReading(result);
        lock (_gate)
        {
            // Split the dual-layer turns: Jev committed (confidence carried through) vs the LLM
            // finished after Jev could not. This is the panel's per-session fallback rate.
            if (result.JevElapsedMilliseconds != null)
            {
                if (result.Confidence != null) _jevAcceptedTurns++;
                else _jevFallbackTurns++;
            }

            _lastJevChoice = choice.Length == 0 ? "-" : choice;
            _lastJevProbabilities = probabilities;
            _lastJevDanger = result.DangerScore is { } danger && double.IsFinite(danger)
                ? Loc.T("危险度 {0}", danger.ToString("0.##")) : "-";
            _lastJevLatency = result.JevElapsedMilliseconds is { } elapsed
                ? Loc.T("耗时 {0} 毫秒", elapsed) : "-";
        }
    }

    /// <summary>The session's dual-layer split for the Jev panel ("Jev 执行 X 次 · 回退 LLM Y 次").</summary>
    public string JevFallbackRate
    {
        get
        {
            lock (_gate) return PlayerFacingSession.FormatJevRate(_jevAcceptedTurns, _jevFallbackTurns);
        }
    }

    private static string FormatJevReading(AgentTurnResult result)
    {
        var parts = new List<string>();
        if (result.Confidence is { } confidence)
        {
            parts.Add(Loc.T("置信度 {0}%", (confidence * 100).ToString("0")));
        }

        if (result.Probabilities is { Count: > 0 } probabilities)
        {
            foreach (var pair in probabilities.OrderByDescending(pair => pair.Value).Take(3))
            {
                parts.Add(pair.Key + " " + (pair.Value * 100).ToString("0") + "%");
            }
        }

        return parts.Count == 0 ? "-" : string.Join(" · ", parts);
    }

    /// <summary>Summarize only safe action envelope fields, not the raw state or provider response.</summary>
    private static string DescribeActResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return string.Empty;
            var status = root.TryGetProperty("status", out var phase) && phase.ValueKind == JsonValueKind.String
                ? phase.GetString() : null;
            if (status == null && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("status", out var nested) && nested.ValueKind == JsonValueKind.String)
                status = nested.GetString();
            if (status is not ("completed" or "pending" or "failed" or "rejected" or "outcome_unknown"))
                return string.Empty;
            return Clip(status, 32);
        }
        catch (JsonException) { return string.Empty; }
    }

    /// <summary>One turn's text as a single display line, clipped on a character budget.</summary>
    private static string Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private void AccountTurn(AgentTurnResult result, bool recordBudget = false)
    {
        lock (_gate)
        {
            if (result.Usage != null)
            {
                _sessionUsage = LlmUsage.Combine(_sessionUsage, result.Usage) ?? LlmUsage.Empty;
                _sessionUsageKnown = true;
                if (result.Usage.PromptTokens > 0)
                {
                    _lastPromptTokens = result.Usage.PromptTokens;
                }
            }

            _sessionRequests += Math.Max(0, result.RequestsSpent);
            if (recordBudget)
            {
                _budgetGuard.Observe(result);
            }
        }
    }

    private void ApplyPlayResult(AgentTurnResult result)
    {
        RecordTurnReceipt(result);
        _waitingForGame = result.WaitingForGame;
        _waitingForPlayer = result.WaitingForPlayer;
        _requestingModel = false;

        if (!string.IsNullOrWhiteSpace(result.Acted)) _lastAction = result.Acted;

        _lastThought = result.Reasoning ?? result.AssistantText ?? _lastThought;
        AppendTurnTraces(result);
        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            AddHistory("assistant", result.AssistantText);
        }

        if (result.RequiresConfiguration)
        {
            ClassifyStop(result.Error ?? Loc.T("配置错误"), ModelRoleNames.Play);
        }

        SetStatus(result.Error == null
            ? (result.Acted != null ? Loc.T("已执行 {0}", result.Acted) : result.WaitingForGame ? Loc.T("等待游戏可操作") : Loc.T("等待可操作状态"))
            : DiagnosticExport.Redact(result.Error));
    }
}
