using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The default <see cref="IActionDecider"/>: asks Jev which of the concrete options on this frame to
/// take, and maps its answer back to the action+indices the bridge executes.
/// </summary>
/// <remarks>
/// One Jev call mixes a <c>choice</c> question (which option) with a <c>noul</c> question (is this a
/// lethal-danger moment), the same speculative fan-out the reference implementation uses: the two are
/// evaluated in parallel against the same state, so the danger read costs no extra round trip. The
/// choice question's <c>criteria</c> is the option id → description map <see cref="JevOptionEnumerator"/>
/// produced, and its <c>instructions</c> is a faceted object -- goal, the screen's playbook slice, the
/// current <see cref="PlayStrategy"/>, and a legality reminder -- because Jev reads structured
/// instructions as readily as a sentence and the facets keep the concerns separable.
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

    public JevExecutionDecider(IJevClient client, string model)
    {
        _client = client;
        _model = string.IsNullOrWhiteSpace(model) ? "jev-latest" : model;
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

        var screen = PlaybookSections.ScreenOfCompactState(snapshotJson) ?? "UNKNOWN";
        var request = BuildRequest(snapshotJson, screen, options, strategy);

        JevResponse response;
        try
        {
            response = await _client.SystemOneAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JevException ex)
        {
            return new ExecutionDecision { Error = $"jev {ex.Kind}: {ex.Message}" };
        }

        if (response.Answers == null
            || !response.Answers.TryGetValue(ChoiceId, out var answer)
            || string.IsNullOrWhiteSpace(answer.Choice))
        {
            return new ExecutionDecision { Error = "jev returned no choice" };
        }

        var chosen = options.FirstOrDefault(
            option => string.Equals(option.Id, answer.Choice, StringComparison.Ordinal));
        if (chosen == null)
        {
            // Jev named an id the enumerator did not produce; acting on it would be an illegal index.
            return new ExecutionDecision { Error = $"jev chose unknown option '{answer.Choice}'" };
        }

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
            Usage = ToUsage(response.Usage),
            // A Jev call is one provider request even though it is not an LLM completion; the session
            // budget counts it the same way.
            RequestsSpent = 1
        };
    }

    /// <summary>
    /// Builds the one-call request: the frame's state, a choice over the concrete options, and a danger
    /// noul. The state is forwarded as a JSON node so the compact <c>agent_view</c> reaches Jev verbatim.
    /// </summary>
    private JevRequest BuildRequest(
        string snapshotJson,
        string screen,
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

        var instructions = new
        {
            goal = "Pick the single best action to take right now in this Slay the Spire 2 frame.",
            screen,
            playbook = PlayPrompt.PlaybookGuidance(screen),
            strategy = new
            {
                posture = strategy.Posture,
                instructions = strategy.Instructions,
                option_hints = strategy.OptionHints
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
                    Type = "noul",
                    Instructions = "Is the player in immediate lethal danger this frame (about to take fatal damage)?"
                }
            }
        };
    }

    private static LlmUsage? ToUsage(JevUsage? usage)
    {
        return usage == null
            ? null
            : new LlmUsage
            {
                PromptTokens = usage.InputTokens,
                CompletionTokens = usage.OutputTokens,
                TotalTokens = usage.InputTokens + usage.OutputTokens
            };
    }
}
