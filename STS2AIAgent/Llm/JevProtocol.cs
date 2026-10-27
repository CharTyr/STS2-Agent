using System.Text.Json;
using System.Text.Json.Nodes;

namespace STS2AIAgent.Llm;

/// <summary>
/// The two request shapes a fast-judgment service can speak, and the adapters between them. The rest of
/// the mod only ever builds a System One <see cref="JevRequest"/> and reads a System One
/// <see cref="JevResponse"/>; this is the one place that knows another shape exists.
/// </summary>
/// <remarks>
/// <para><c>systemone</c>: TypeSafe's native shape, <c>{model, state, questions: {id: question}}</c> in and
/// <c>{answers: {id: answer}}</c> out. Most providers use it as is; Cloudflare Workers AI wraps the answer
/// in <c>{result: …, success, errors}</c>, which <see cref="ParseResponse"/> unwraps.</para>
/// <para><c>openai_decisions</c>: OpenAI's Decisions API, <c>{model, input, questions: [named questions]}</c>
/// in and <c>{answers: [named answers]}</c> out, with <c>predicate</c> standing for <c>noul</c>. Mapped
/// from the shape MaiWork uses against the provider's published documentation.</para>
/// </remarks>
internal static class JevProtocol
{
    public const string SystemOne = "systemone";

    public const string OpenAiDecisions = "openai_decisions";

    public static readonly IReadOnlyList<string> All = new[] { SystemOne, OpenAiDecisions };

    /// <summary>A known protocol id, or <see cref="SystemOne"/> for anything blank or unknown.</summary>
    public static string Normalize(string? protocol) =>
        string.Equals(protocol?.Trim(), OpenAiDecisions, StringComparison.OrdinalIgnoreCase) ? OpenAiDecisions : SystemOne;

    public static string BuildBody(string protocol, JevRequest request, JsonSerializerOptions options)
    {
        if (Normalize(protocol) == SystemOne) return JsonSerializer.Serialize(request, options);

        var questions = new JsonArray();
        foreach (var (name, question) in request.Questions)
        {
            var instructions = AsText(question.Instructions, options);
            var criteria = question.Criteria == null ? null : JsonSerializer.SerializeToNode(question.Criteria, options);
            var item = new JsonObject { ["type"] = null, ["name"] = name, ["instructions"] = instructions };
            switch (question.Type)
            {
                case "choice" when criteria is JsonObject choices:
                    item["type"] = "choice";
                    var list = new JsonArray();
                    foreach (var (id, description) in choices)
                        list.Add(new JsonObject { ["value"] = id, ["description"] = NodeText(description) });
                    item["choices"] = list;
                    break;
                case "score" when criteria is JsonArray levels:
                    item["type"] = "score";
                    var labels = new JsonArray();
                    foreach (var level in levels) labels.Add(new JsonObject { ["label"] = NodeText(level) });
                    item["levels"] = labels;
                    break;
                case "noul":
                    item["type"] = "predicate";
                    break;
                default:
                    throw new JevException($"Question \"{name}\" cannot be sent in the OpenAI Decisions format.", JevExceptionKind.Config);
            }

            questions.Add(item);
        }

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["input"] = request.State == null ? "" : request.State is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : request.State.ToJsonString(options),
            ["questions"] = questions
        };
        return body.ToJsonString(options);
    }

    /// <summary>Reads a provider's answer back into the System One shape, or throws a <see cref="JevExceptionKind.Server"/> error.</summary>
    public static JevResponse ParseResponse(string protocol, string payload, JsonSerializerOptions options)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new JevException($"Jev response was not valid JSON: {ex.Message}", JevExceptionKind.Server);
        }

        if (root is not JsonObject body)
            throw new JevException("Jev response was not a JSON object.", JevExceptionKind.Server);

        return Normalize(protocol) == SystemOne ? ParseSystemOne(body, options) : ParseDecisions(body, options);
    }

    private static JevResponse ParseSystemOne(JsonObject body, JsonSerializerOptions options)
    {
        if (body["success"] is JsonValue success && success.TryGetValue<bool>(out var ok) && !ok)
        {
            var message = body["errors"] is JsonArray { Count: > 0 } errors ? NodeText(errors[0]?["message"]) : "";
            throw new JevException("Jev request failed: " + (message.Length == 0 ? "success=false" : message), JevExceptionKind.Server);
        }

        // Cloudflare Workers AI returns {result: {answers…}, success, errors}.
        var payload = body["answers"] == null && body["result"] is JsonObject result && result["answers"] != null ? result : body;
        JevResponse? response;
        try
        {
            response = payload.Deserialize<JevResponse>(options);
        }
        catch (JsonException ex)
        {
            throw new JevException($"Jev response was not valid JSON: {ex.Message}", JevExceptionKind.Server);
        }

        if (response?.Answers is null)
            throw new JevException("Jev response did not contain an \"answers\" object.", JevExceptionKind.Server);
        return response;
    }

    private static JevResponse ParseDecisions(JsonObject body, JsonSerializerOptions options)
    {
        if (body["answers"] is not JsonArray raw)
            throw new JevException("Decisions response did not contain an \"answers\" array.", JevExceptionKind.Server);

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (var entry in raw.OfType<JsonObject>())
        {
            var name = NodeText(entry["name"]);
            if (name.Length == 0) continue;
            var type = NodeText(entry["type"]);
            answers[name] = type switch
            {
                "choice" => new JevAnswer
                {
                    Type = "choice", Choice = NodeText(entry["choice"]),
                    Probabilities = Probabilities(entry["probabilities"]), Confidence = Number(entry["confidence"])
                },
                "score" => new JevAnswer
                {
                    Type = "score", Score = Number(entry["score"]),
                    Probabilities = Probabilities(entry["probabilities"]), Confidence = Number(entry["confidence"])
                },
                "predicate" => new JevAnswer { Type = "noul", Noul = Number(entry["probability"]) },
                "refusal" => throw new JevException($"Decisions refused question \"{name}\".", JevExceptionKind.Server),
                _ => throw new JevException($"Decisions returned an unknown answer type \"{type}\".", JevExceptionKind.Server)
            };
        }

        JevUsage? usage = null;
        if (body["usage"] is JsonObject usageNode)
        {
            try { usage = usageNode.Deserialize<JevUsage>(options); }
            catch (JsonException) { usage = null; }
        }

        return new JevResponse { Model = NodeText(body["model"]), Answers = answers, Usage = usage };
    }

    /// <summary>OpenAI's probability list, <c>[{value|label, probability}]</c>, as System One's map.</summary>
    private static Dictionary<string, double>? Probabilities(JsonNode? node)
    {
        if (node is not JsonArray items) return null;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not JsonObject item || Number(item["probability"]) is not { } probability) continue;
            var key = NodeText(item["value"]);
            if (key.Length == 0) key = NodeText(item["label"]);
            if (key.Length == 0) key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            map[key] = probability;
        }

        return map;
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private static string NodeText(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };

    private static string AsText(object instructions, JsonSerializerOptions options) =>
        instructions as string ?? JsonSerializer.Serialize(instructions, options);
}
