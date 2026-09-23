using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The slow half of the dual-layer engine: asks the LLM for a <see cref="PlayStrategy"/> at a low
/// cadence and writes it to the <see cref="StrategyStore"/> the Jev decider reads every frame.
/// </summary>
/// <remarks>
/// The planner is deliberately decoupled from <see cref="AgentLoop"/>: it takes the client factory and
/// settings directly rather than riding the play loop, because the play loop is the per-frame path and
/// the planner must never sit on it. A plan is requested on a screen/act change or after a run of
/// low-confidence Jev decisions -- the moments the current strategy has stopped being a good answer --
/// and the decider keeps using the previous strategy until the new one lands, so a slow or failed plan
/// never blocks a click.
///
/// The LLM call is read-only: no tools, no acting. It returns prose that should contain one JSON
/// object; <see cref="PlayStrategy.TryParse"/> keeps the defaults for any field the model omits, and a
/// reply with no parseable object leaves the current strategy untouched.
/// </remarks>
internal sealed class StrategyPlanner
{
    /// <summary>How many consecutive low-confidence decisions trigger a replan.</summary>
    private const int LowConfidenceStreakThreshold = 3;

    /// <summary>Refresh once per this many decisions even when the screen has not changed.</summary>
    internal const int PeriodicRefreshTurns = 10;

    private readonly ILlmClientFactory _factory;
    private readonly Func<AgentSettings> _settings;
    private readonly StrategyStore _store;
    private string? _lastContextKey;
    private int _lowConfidenceStreak;
    private int _turnsSinceRefresh;

    public StrategyPlanner(ILlmClientFactory factory, Func<AgentSettings> settings, StrategyStore store)
    {
        _factory = factory;
        _settings = settings;
        _store = store;
    }

    /// <summary>
    /// Notes the outcome of a Jev decision and reports whether the planner should run: on a context
    /// change (a new screen/act/boss, keyed by the caller-supplied string), after a streak of
    /// low-confidence decisions, or every <see cref="PeriodicRefreshTurns"/> turns.
    /// A confident decision resets only the low-confidence streak.
    /// </summary>
    public bool ShouldRefresh(string contextKey, double? confidence, double threshold)
    {
        var contextChanged = !string.Equals(contextKey, _lastContextKey, StringComparison.Ordinal);
        _lastContextKey = contextKey;

        _lowConfidenceStreak = confidence.HasValue && confidence.Value < threshold
            ? _lowConfidenceStreak + 1
            : 0;

        if (_turnsSinceRefresh < PeriodicRefreshTurns) _turnsSinceRefresh++;
        var refresh = contextChanged || _lowConfidenceStreak >= LowConfidenceStreakThreshold
            || _turnsSinceRefresh >= PeriodicRefreshTurns;
        if (refresh)
        {
            _lowConfidenceStreak = 0;
            _turnsSinceRefresh = 0;
        }
        return refresh;
    }

    /// <summary>
    /// Asks the play model for a fresh strategy and stores it. Returns the outcome and the request's
    /// usage, so the caller can account it against the session budget -- a plan the budget never
    /// hears about is an invisible spend. A model failure or an unparseable reply returns false and
    /// leaves the store as it was, so the decider keeps a working strategy rather than losing it to
    /// a bad plan.
    /// </summary>
    public async Task<(bool Adopted, LlmUsage? Usage)> RefreshAsync(string stateSummary, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? tryBeginRequest = null, long? expectedRevision = null,
        IReadOnlyList<DecisionLogEntry>? recentDecisions = null, string? runId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = expectedRevision ?? _store.Revision;
        var settings = _settings();
        var resolved = settings.TryResolvePlayModel() ?? settings.TryResolveConversationModel();
        if (resolved == null)
        {
            return (false, null);
        }

        var request = new LlmRequest
        {
            Model = resolved.Model.Model,
            Stream = false,
            Thinking = resolved.Model.GetThinkingIntensity(),
            ThinkingMode = resolved.Model.ThinkingMode,
            Messages = new[]
            {
                LlmMessage.System(
                    "You are the strategy planner for a Slay the Spire 2 agent. A fast execution model "
                    + "picks each concrete action; you only set the standing strategy it follows. Reply with a "
                    + "single JSON object and nothing else, with these keys: \"posture\" (\"aggressive\", "
                    + "\"defensive\", or \"balanced\"), \"instructions\" (one or two sentences of standing "
                    + "guidance), and \"option_hints\" (an object mapping option kinds to short nudges, may be "
                    + "empty). Do not name specific card indices or targets; they change every frame."),
                LlmMessage.User(BuildContext(stateSummary, _store.Current, recentDecisions, runId))
            }
        };

        // The planner honors the same per-request timeout the play loop does; a stalled plan must
        // not outwait the requests it plans for.
        var timeout = settings.LlmRequestTimeoutSeconds is > 0 ? TimeSpan.FromSeconds(settings.LlmRequestTimeoutSeconds.Value) : (TimeSpan?)null;
        var client = _factory.Create(resolved.Endpoint, timeout);
        if (tryBeginRequest != null && !await tryBeginRequest(cancellationToken)) return (false, null);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = await client.CompleteAsync(request, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return (false, completion.Usage);
        var strategy = completion.Content == null ? null : PlayStrategy.TryParse(ExtractJson(completion.Content));
        if (strategy == null)
        {
            return (false, completion.Usage);
        }

        var adopted = _store.TryUpdate(strategy with
        {
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Source = "llm"
        }, revision);
        return (adopted, completion.Usage);
    }

    /// <summary>
    /// The strategy and recent accepted actions are historical context only; filtering on the
    /// caller's current run prevents an old game's index from masquerading as a live option.
    /// </summary>
    private static string BuildContext(string stateSummary, PlayStrategy current,
        IReadOnlyList<DecisionLogEntry>? entries, string? runId)
    {
        var lines = new List<string>
        {
            "Current game state summary:",
            stateSummary.Length > 6000 ? stateSummary[..6000] : stateSummary,
            "Current standing strategy (historical guidance, not a fresh action):",
            current.ToJson(),
            "Recent accepted decisions from this run (do not reuse indices):"
        };
        if (!string.IsNullOrWhiteSpace(runId) && runId != "run_unknown" && entries != null)
        {
            foreach (var entry in entries.Where(e => string.Equals(e.run_id, runId, StringComparison.Ordinal)).TakeLast(6))
            {
                var reason = entry.reason?.Replace('\r', ' ').Replace('\n', ' ');
                lines.Add("- " + entry.action + (reason == null ? "" : ": " + reason[..Math.Min(reason.Length, 160)]));
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Pulls the first JSON object out of a reply that may wrap it in prose or a code fence. The model
    /// is asked for bare JSON but is not guaranteed to comply, so the planner finds the object rather
    /// than failing the whole plan on a leading sentence.
    /// </summary>
    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
