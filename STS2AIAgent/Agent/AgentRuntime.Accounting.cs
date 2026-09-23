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
    private const int ThoughtBubbleChars = 600;

    /// <summary>How much of it the "action" bubble repeats beside the action name.</summary>
    private const int ActionBubbleReasonChars = 140;

    /// <summary>Turns dropped off the front of the history, so the page can say how many.</summary>
    private int _historyTrimmed;

    /// <summary>The execution model's last reading, for the Jev panel. Null before its first turn.</summary>
    private string? _lastJevChoice;
    private string? _lastJevProbabilities;

    /// <summary>How many earlier turns the cap has dropped; 0 means the log starts at the beginning.</summary>
    public int HistoryTrimmedCount
    {
        get { lock (_gate) return _historyTrimmed; }
    }

    /// <summary>What the execution layer last chose, as one line of display text.</summary>
    public string LastJevChoice => _lastJevChoice ?? "-";

    /// <summary>Its confidence and per-option scores, as one line of display text.</summary>
    public string LastJevProbabilities => _lastJevProbabilities ?? "-";

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
                confidence: result.Confidence);
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
    /// </remarks>
    private void AppendTurnTraces(AgentTurnResult result)
    {
        if (ShowsThinkingInChat() && !string.IsNullOrWhiteSpace(result.Reasoning))
        {
            AddHistoryCore("thought", Clip(result.Reasoning, ThoughtBubbleChars), notify: false);
        }

        if (string.IsNullOrWhiteSpace(result.Acted))
        {
            return;
        }

        var reason = Clip(result.Reasoning, ActionBubbleReasonChars);
        AddHistoryCore("action", reason.Length == 0 ? result.Acted : result.Acted + " — " + reason, notify: false);
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
        }

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
    }

    private bool ShowsThinkingInChat()
    {
        lock (_gate)
        {
            return _settings.ShowThinkingInChat;
        }
    }

    /// <summary>
    /// Keeps the Jev panel's two lines current. Only a dual-layer turn carries a confidence, so the
    /// panel never reports a reading for a decision the execution model did not make.
    /// </summary>
    private void RecordJevReading(AgentTurnResult result)
    {
        if (result.Confidence == null)
        {
            return;
        }

        var choice = Clip(result.Reasoning ?? result.Acted, 90);
        var probabilities = FormatJevReading(result);
        lock (_gate)
        {
            _lastJevChoice = choice.Length == 0 ? "-" : choice;
            _lastJevProbabilities = probabilities;
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
