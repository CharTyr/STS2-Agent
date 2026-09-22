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

    private readonly ILlmClientFactory _factory;
    private readonly Func<AgentSettings> _settings;
    private readonly StrategyStore _store;
    private string? _lastContextKey;
    private int _lowConfidenceStreak;

    public StrategyPlanner(ILlmClientFactory factory, Func<AgentSettings> settings, StrategyStore store)
    {
        _factory = factory;
        _settings = settings;
        _store = store;
    }

    /// <summary>
    /// Notes the outcome of a Jev decision and reports whether the planner should run: on a context
    /// change (a new screen/act/boss, keyed by the caller-supplied string) or after a streak of
    /// low-confidence decisions. A confident decision resets the streak.
    /// </summary>
    public bool ShouldRefresh(string contextKey, double? confidence, double threshold)
    {
        var contextChanged = !string.Equals(contextKey, _lastContextKey, StringComparison.Ordinal);
        _lastContextKey = contextKey;

        _lowConfidenceStreak = confidence.HasValue && confidence.Value < threshold
            ? _lowConfidenceStreak + 1
            : 0;

        return contextChanged || _lowConfidenceStreak >= LowConfidenceStreakThreshold;
    }

    /// <summary>
    /// Asks the play model for a fresh strategy and stores it. Returns true when a new strategy was
    /// adopted; a model failure or an unparseable reply returns false and leaves the store as it was,
    /// so the decider keeps a working strategy rather than losing it to a bad plan.
    /// </summary>
    public async Task<bool> RefreshAsync(string stateSummary, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = settings.TryResolvePlayModel() ?? settings.TryResolveConversationModel();
        if (resolved == null)
        {
            return false;
        }

        var request = new LlmRequest
        {
            Model = resolved.Model.Model,
            Stream = false,
            Messages = new[]
            {
                LlmMessage.System(
                    "You are the strategy planner for a Slay the Spire 2 agent. A fast execution model "
                    + "picks each concrete action; you only set the standing strategy it follows. Reply with a "
                    + "single JSON object and nothing else, with these keys: \"posture\" (\"aggressive\", "
                    + "\"defensive\", or \"balanced\"), \"instructions\" (one or two sentences of standing "
                    + "guidance), and \"option_hints\" (an object mapping option kinds to short nudges, may be "
                    + "empty). Do not name specific card indices or targets; they change every frame."),
                LlmMessage.User("Current game state summary:\n" + stateSummary)
            }
        };

        var client = _factory.Create(resolved.Endpoint);
        var completion = await client.CompleteAsync(request, cancellationToken);
        var strategy = completion.Content == null ? null : PlayStrategy.TryParse(ExtractJson(completion.Content));
        if (strategy == null)
        {
            return false;
        }

        _store.Update(strategy with
        {
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Source = "llm"
        });
        return true;
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
