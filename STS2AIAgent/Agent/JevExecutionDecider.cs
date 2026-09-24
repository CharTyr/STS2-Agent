using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The default <see cref="IActionDecider"/>: asks Jev which of the concrete options on this frame to
/// take, and maps its answer back to the action+indices the bridge executes.
/// </summary>
/// <remarks>
/// One Jev call mixes a <c>choice</c> question (which option) with a <c>score</c> question (how
/// dangerous the current frame is), the same speculative fan-out the reference implementation uses: the two are
/// evaluated in parallel against the same state, so the danger read costs no extra round trip. The
/// choice question's <c>criteria</c> is the option id → description map <see cref="JevOptionEnumerator"/>
/// produced, and its <c>instructions</c> is a faceted object -- the plan's macro goal, the screen's
/// playbook slice, the current <see cref="PlayStrategy"/>, and a legality reminder -- because Jev reads
/// structured instructions as readily as a sentence and the facets keep the concerns separable.
///
/// The strategy facet is normalized, never forwarded raw: <c>option_hints</c> is re-keyed onto this
/// frame's concrete option ids by <see cref="JevOptionEnumerator.AlignHints"/>, so a hint keyed by
/// option kind (the planner's documented shape) still lands on the options that kind offers, and a hint
/// naming an index that no longer exists is dropped rather than offered as a choice Jev cannot take.
/// The chosen id is still required to be one the enumerator produced, so alignment can only remove
/// guidance -- it can never invent an action.
///
/// The decider never throws for an empty or malformed frame: it returns an <see cref="ExecutionDecision"/>
/// with <see cref="ExecutionDecision.Error"/> set, and the orchestrator falls back to the LLM path. Only
/// the caller's own cancellation propagates.
/// </remarks>
internal sealed class JevExecutionDecider : IActionDecider
{
    /// <summary>The question id the chosen option is read back from.</summary>
    private const string ChoiceId = "action";

    /// <summary>The question id the danger read is read back from.</summary>
    private const string DangerId = "danger";

    private readonly IJevClient _client;
    private readonly string _model;
    private readonly TimeSpan _turnTimeout;
    private readonly Func<int, bool>? _allowAttempt;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="turnTimeout">Total deadline covering both HTTP attempts and the intervening backoff.</param>
    /// <param name="allowAttempt">Called before each send with attempts already made (0 or 1).
    /// Check the budget against those pending attempts; a denied call sends nothing.</param>
    public JevExecutionDecider(IJevClient client, string model,
        TimeSpan? turnTimeout = null, Func<int, bool>? allowAttempt = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        _model = string.IsNullOrWhiteSpace(model) ? "jev-latest" : model;
        _turnTimeout = turnTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout : TimeSpan.FromSeconds(90);
        _allowAttempt = allowAttempt;
        _delay = delay ?? Task.Delay;
    }

    public async Task<ExecutionDecision> DecideAsync(
        string snapshotJson,
        PlayStrategy strategy,
        CancellationToken cancellationToken)
    {
        var options = JevOptionEnumerator.Enumerate(snapshotJson);
        if (options.Count == 0)
        {
            return new ExecutionDecision { Error = "no legal options on this frame" };
        }

        var request = BuildRequest(snapshotJson, options, strategy);

        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_turnTimeout);
        var started = Stopwatch.StartNew();
        var attempts = 0;
        JevResponse response;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                // The caller owns a serialized turn. Check the pending count before dispatch, not
                // after receiving 429: no budget means no second network request.
                if (_allowAttempt != null && !_allowAttempt(attempts))
                {
                    return new ExecutionDecision
                    {
                        Error = "jev request budget exhausted",
                        RequestsSpent = attempts,
                        JevElapsedMilliseconds = started.ElapsedMilliseconds
                    };
                }

                deadline.Token.ThrowIfCancellationRequested();
                attempts++;
                try
                {
                    response = await _client.SystemOneAsync(request, deadline.Token);
                    break;
                }
                catch (JevException ex) when (attempts == 1 && ex.StatusCode == 429)
                {
                    var seconds = ex.RetryAfterSeconds ?? JevClient.DefaultRetryAfterSeconds;
                    var backoff = JevClient.DefaultRetryDelay(seconds);
                    if (backoff >= _turnTimeout - started.Elapsed)
                    {
                        return new ExecutionDecision
                        {
                            Error = "jev rate limit exceeded the turn deadline",
                            RequestsSpent = attempts,
                            JevElapsedMilliseconds = started.ElapsedMilliseconds
                        };
                    }

                    await _delay(backoff, deadline.Token);
                }
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(new AgentTurnResult
            {
                RequestsSpent = attempts,
                JevElapsedMilliseconds = started.ElapsedMilliseconds
            }, ex, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionDecision
            {
                Error = "jev provider request timed out",
                RequestsSpent = attempts,
                JevElapsedMilliseconds = started.ElapsedMilliseconds
            };
        }
        catch (JevException ex)
        {
            return new ExecutionDecision
            {
                Error = $"jev {ex.Kind}: {DiagnosticExport.Redact(ex.Message)}",
                RequestsSpent = attempts,
                JevElapsedMilliseconds = started.ElapsedMilliseconds
            };
        }

        var elapsed = started.ElapsedMilliseconds;
        var usage = ToUsage(response.Usage);
        if (response.Answers == null
            || !response.Answers.TryGetValue(ChoiceId, out var answer)
            || answer == null
            || string.IsNullOrWhiteSpace(answer.Choice))
        {
            return new ExecutionDecision { Error = "jev returned no choice", RequestsSpent = attempts,
                Usage = usage, JevElapsedMilliseconds = elapsed };
        }

        var chosen = options.FirstOrDefault(
            option => string.Equals(option.Id, answer.Choice, StringComparison.Ordinal));
        if (chosen == null)
        {
            // Jev named an id the enumerator did not produce; acting on it would be an illegal index.
            return new ExecutionDecision { Error = "jev chose an unknown option", RequestsSpent = attempts,
                Usage = usage, JevElapsedMilliseconds = elapsed };
        }

        var danger = response.Answers.TryGetValue(DangerId, out var risk)
            && risk?.Type == "score" && risk.Score is { } score && double.IsFinite(score)
            && score is >= 0 and <= 4 ? risk.Score : null;
        return new ExecutionDecision
        {
            Action = chosen.Action,
            CardIndex = chosen.CardIndex,
            TargetIndex = chosen.TargetIndex,
            OptionIndex = chosen.OptionIndex,
            X = chosen.X,
            Y = chosen.Y,
            Tool = chosen.Tool,
            Reason = chosen.Description,
            Confidence = answer.Confidence,
            Probabilities = answer.Probabilities,
            DangerScore = danger,
            JevElapsedMilliseconds = elapsed,
            Usage = usage,
            RequestsSpent = attempts,
            OfferedOptionIds = options.Select(option => option.Id).ToArray()
        };
    }

    /// <summary>
    /// Builds the one-call request: the frame's state, a choice over the concrete options, and an optional
    /// danger score. The state is forwarded as a JSON node so the compact <c>agent_view</c> reaches Jev verbatim.
    /// </summary>
    private JevRequest BuildRequest(
        string snapshotJson,
        List<JevOption> options,
        PlayStrategy strategy)
    {
        JsonNode? state = null;
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            var stateElement = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("state", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                    ? nested
                    : root;
            state = JsonNode.Parse(stateElement.GetRawText());
        }
        catch (JsonException)
        {
            // A frame that does not parse still goes to Jev as a raw string rather than not going at all.
            state = JsonValue.Create(snapshotJson);
        }

        var criteria = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            criteria[option.Id] = option.Description;
        }

        // The frame the decision is about, read once and used for both the screen facet and the
        // comparison that tells a live plan from guidance carried over from another screen or encounter.
        var scope = JevOptionEnumerator.ReadScope(snapshotJson);
        var screen = scope.Screen ?? "UNKNOWN";

        // A hint Jev cannot match to a criterion is not guidance. Re-keying here is what enforces the
        // invariant that every option_hints key is a criteria key of this same request.
        var aligned = JevOptionEnumerator.AlignHints(strategy.OptionHints, options);
        var goal = PlayStrategy.ClampGoal(strategy.Goal);
        var instructions = new
        {
            goal = ChoiceGoal(goal),
            screen,
            playbook = PlayPrompt.PlaybookGuidance(screen),
            strategy = new
            {
                posture = strategy.Posture,
                goal,
                instructions = strategy.Instructions,
                option_hints = aligned.OptionHints,
                // How much of the planner's per-option guidance actually named an option this frame
                // offers. A non-zero drop count is the visible trace of a plan written for another
                // frame, and it tells the executor to weigh `instructions` over the removed nudges.
                option_hints_used = aligned.OptionHints.Count,
                option_hints_dropped = aligned.DroppedKeys.Count,
                plan_scope = PlanScope(strategy, scope, screen)
            },
            legality = "Every option id is a currently legal action. Choose one of them exactly as given."
        };

        return new JevRequest
        {
            State = state,
            Model = _model,
            Questions = new Dictionary<string, JevQuestion>
            {
                [ChoiceId] = new JevQuestion
                {
                    Type = "choice",
                    Instructions = instructions,
                    Criteria = criteria
                },
                [DangerId] = new JevQuestion
                {
                    Type = "score",
                    Instructions = "Rate the player's immediate danger this frame; do not replace the separate action choice.",
                    Criteria = new[] { "safe", "low", "moderate", "high", "lethal" }
                }
            }
        };
    }

    /// <summary>
    /// The macro objective the execution model works toward, placed in front of the per-frame question.
    /// </summary>
    /// <remarks>
    /// Without a stated goal the executor re-derives an objective from whatever hand it is holding, which
    /// is where the live 2026-09-23 run's long, unsure reasoning before a low-confidence <c>end_turn</c>
    /// comes from. With no goal the previous static sentence is kept byte for byte, so a strategy that
    /// states nothing behaves exactly as before.
    /// </remarks>
    private static string ChoiceGoal(string goal)
    {
        const string frame = "Pick the single best action to take right now in this Slay the Spire 2 frame.";
        return goal.Length == 0 ? frame : "Work toward this macro goal: " + goal + " " + frame;
    }

    /// <summary>
    /// Where the standing plan was written, against the frame being decided, or null when the plan
    /// recorded no scope (a default or MCP-written strategy).
    /// </summary>
    /// <remarks>
    /// The planner's refresh key is run+screen+act, so one combat plan stands for every combat in the
    /// act. This makes the reuse visible to the executor instead of silently presenting carried-over
    /// guidance as current. The encounter claim is deliberately narrow: a combat round counter restarts
    /// at 1 per encounter, so a frame whose round is BELOW the round the plan recorded proves a newer
    /// encounter, and nothing is asserted when the plan recorded no round or the frame has none.
    /// </remarks>
    private static object? PlanScope(PlayStrategy strategy, FrameScope frame, string screen)
    {
        var planScreen = (strategy.PlanScreen ?? string.Empty).Trim();
        if (planScreen.Length == 0 && strategy.PlanRound == null)
        {
            return null;
        }

        var sameScreen = planScreen.Length > 0
            && string.Equals(planScreen, screen, StringComparison.OrdinalIgnoreCase);
        var newerEncounter = strategy.PlanRound is { } planRound
            && frame.Round is { } round
            && round < planRound;
        return new
        {
            written_for_screen = planScreen,
            written_at_round = strategy.PlanRound,
            current_screen = screen,
            current_round = frame.Round,
            same_screen = sameScreen,
            newer_encounter_than_plan = newerEncounter,
            note = newerEncounter
                ? "This standing guidance was written for an earlier encounter; use it as general posture, not as current-fight tactics."
                : "Standing guidance for the scope named here."
        };
    }

    private static LlmUsage? ToUsage(JevUsage? usage)
    {
        return usage == null
            ? null
            : new LlmUsage
            {
                PromptTokens = Math.Max(0, usage.InputTokens),
                CompletionTokens = Math.Max(0, usage.OutputTokens),
                TotalTokens = (int)Math.Min(int.MaxValue, (long)Math.Max(0, usage.InputTokens) + Math.Max(0, usage.OutputTokens))
            };
    }
}
