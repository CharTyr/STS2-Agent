using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

/// <summary>
/// Deterministic coverage for the Jev transport: the request it puts on the wire, the answers it takes
/// back, the one status it retries, and the two failures it refuses to misreport.
/// </summary>
/// <remarks>
/// Every test injects its own <see cref="HttpClient"/> over a fake <see cref="HttpMessageHandler"/>, so
/// none of them touches the TypeSafe service and none of them carries an API key. The retry delay is
/// collapsed to a millisecond through the client's own hook, because a test that sleeps a real second
/// per retry is a test people stop running.
/// </remarks>
internal static class JevClientTests
{
    // Matches the shape of AgentSettings.JevBaseUrl: the service root, with no /v1 of its own,
    // because the client appends /v1/systemone and /v1/models.
    private const string BaseUrl = "https://example.test";

    private const string SystemOneUrl = BaseUrl + "/v1/systemone";

    private const string ModelsUrl = BaseUrl + "/v1/models";

    private const string ApiKey = "test-key";

    /// <summary>A choice answer in the exact shape the live service returns.</summary>
    private const string ChoiceResponseBody = """
    {
      "model": "jev-1.13.0",
      "answers": {
        "risk": {
          "type": "choice",
          "choice": "play:0",
          "confidence": 0.24,
          "probabilities": { "end": 0.02, "play:0": 0.49, "defend": 0.49 }
        }
      },
      "usage": { "input_tokens": 840, "output_tokens": 12 }
    }
    """;

    /// <summary>
    /// The answer shape the decider's own question ids use: <c>action</c> for the choice and
    /// <c>danger</c> for the score.
    /// </summary>
    private const string DeciderResponseBody = """
    {
      "model": "jev-1.13.0",
      "answers": {
        "action": { "type": "choice", "choice": "end_turn", "confidence": 0.9 },
        "danger": { "type": "score", "score": 1.5, "confidence": 0.5 }
      },
      "usage": { "input_tokens": 900, "output_tokens": 4 }
    }
    """;

    /// <summary>
    /// A score answer, which carries a fractional score plus a legend and its own confidence.
    /// </summary>
    private const string ScoreResponseBody = """
    {
      "model": "jev-1.13.0",
      "answers": {
        "risk": {
          "type": "score",
          "score": 1.82,
          "confidence": 0.27,
          "legend": { "0": "safe", "1": "low", "2": "medium" },
          "probabilities": { "0": 0.13, "1": 0.26, "2": 0.61 }
        }
      },
      "usage": { "input_tokens": 31, "output_tokens": 4 }
    }
    """;

    private const string ModelsBody = """
    {
      "models": [
        { "name": "jev-latest", "description": "Alias for the current release", "release_date": "2026-01-01" },
        { "name": "jev-1.13.0", "description": "Pinned release", "release_date": "2026-03-01" }
      ]
    }
    """;

    /// <summary>
    /// The wire shape the decider actually puts on the endpoint, captured through the real client over a
    /// fake transport: the aligned <c>option_hints</c> must be closed over the same request's
    /// <c>criteria</c>, the macro goal must lead the question, the frame's state must still travel
    /// verbatim, and the danger score must still be asked.
    /// </summary>
    /// <remarks>
    /// The decider's own tests assert the request object with a fake <see cref="IJevClient"/>; this one
    /// exists because "the object was right" and "the JSON body was right" are different claims --
    /// <see cref="JevClient"/> serializes with a null naming policy and
    /// <c>JsonIgnoreCondition.WhenWritingNull</c>, so this is where a facet that is merely null in C#
    /// is shown to leave the wire, and where a key that is merely present in C# is shown to be spelled
    /// the way the service reads it.
    /// </remarks>
    public static async Task SystemOneAsync_PostsTheAlignedChoiceQuestionTheDeciderBuilds()
    {
        var handler = new RecordingHandler(DeciderResponseBody);
        using var http = new HttpClient(handler);
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", http);
        var strategy = new PlayStrategy
        {
            Goal = "kill the weakest enemy first",
            Instructions = "focus fire",
            PlanScreen = "MAP",
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["play_card"] = "prefer damage",
                ["play_card:7->3"] = "an index from an earlier fight"
            }
        };

        var decision = await new JevExecutionDecider(client, "jev-latest")
            .DecideAsync(JevStrategyAlignmentTests.CombatFrame, strategy, CancellationToken.None);

        Assert.Equal("end_turn", decision.Action);
        Assert.Equal(SystemOneUrl, handler.LastUrl);
        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        var question = root.GetProperty("questions").GetProperty("action");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.Equal("score", root.GetProperty("questions").GetProperty("danger").GetProperty("type").GetString());
        Assert.Equal("COMBAT", root.GetProperty("state").GetProperty("screen").GetString());

        var criteria = question.GetProperty("criteria");
        var ids = criteria.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(ids.Contains("end_turn"));
        Assert.True(ids.Contains("play_card:0->0"));

        var instructions = question.GetProperty("instructions");
        var hints = instructions.GetProperty("strategy").GetProperty("option_hints");
        var forwarded = hints.EnumerateObject().Select(property => property.Name).ToList();
        Assert.NotEmpty(forwarded);
        foreach (var key in forwarded)
        {
            Assert.True(ids.Contains(key), $"wire hint '{key}' must be a criteria id of the same request");
        }

        Assert.False(hints.TryGetProperty("play_card:7->3", out _), "the stale id must not reach the wire");
        Assert.False(hints.TryGetProperty("play_card", out _), "the kind key must not reach the wire as an id");
        Assert.Equal("prefer damage", hints.GetProperty("play_card:0->0").GetString());
        Assert.Contains("kill the weakest enemy first", instructions.GetProperty("goal").GetString());
        Assert.Equal("focus fire", instructions.GetProperty("strategy").GetProperty("instructions").GetString());
        // A recorded scope reaches the wire; a plan with none leaves the key out entirely.
        Assert.Equal("MAP", instructions.GetProperty("strategy").GetProperty("plan_scope").GetProperty("written_for_screen").GetString());
    }

    public static async Task SystemOneAsync_PostsStateModelAndQuestionsToSystemOne()
    {
        var handler = new RecordingHandler(ChoiceResponseBody);
        var client = new JevClient(BaseUrl + "/", ApiKey, "jev-latest", new HttpClient(handler));

        await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(SystemOneUrl, handler.LastUrl);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(ApiKey, handler.AuthorizationParameter);
        Assert.NotNull(handler.LastBody);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        Assert.Equal("jev-latest", root.GetProperty("model").GetString());
        Assert.Equal(10, root.GetProperty("state").GetProperty("hp").GetInt32());
        Assert.Equal("choice", root.GetProperty("questions").GetProperty("risk").GetProperty("type").GetString());
        Assert.Equal("Pick the safer attack", root.GetProperty("questions").GetProperty("risk").GetProperty("instructions").GetString());
        Assert.Equal("Attack now", root.GetProperty("questions").GetProperty("risk").GetProperty("criteria").GetProperty("strike").GetString());

        // A noul question carries no criteria at all: the key must be absent, not null.
        Assert.False(
            root.GetProperty("questions").GetProperty("lethal").TryGetProperty("criteria", out _),
            "a noul question must not serialize a criteria member");
    }

    public static async Task SystemOneAsync_ParsesChoiceAnswerWithProbabilitiesAndConfidence()
    {
        var handler = new RecordingHandler(ChoiceResponseBody);
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var response = await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(840, response.Usage!.InputTokens);
        Assert.Equal(12, response.Usage!.OutputTokens);
        Assert.Single(response.Answers!);

        var answer = response.Answers!["risk"];
        Assert.Equal("choice", answer.Type);
        Assert.Equal("play:0", answer.Choice);
        Assert.NotNull(answer.Confidence);
        Assert.Equal(0.24, answer.Confidence!.Value);
        Assert.NotNull(answer.Probabilities);
        Assert.Equal(0.49, answer.Probabilities!["play:0"]);
        Assert.Equal(3, answer.Probabilities!.Count);
    }

    public static async Task SystemOneAsync_ParsesFractionalScoreAnswerWithLegend()
    {
        var handler = new RecordingHandler(ScoreResponseBody);
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var response = await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        var answer = response.Answers!["risk"];
        Assert.Equal("score", answer.Type);
        // 1.82 is a position between two levels, not an index into one: rounding it here would throw
        // away the part that distinguishes them.
        Assert.NotNull(answer.Score);
        Assert.Equal(1.82, answer.Score!.Value);
        Assert.Equal(0.27, answer.Confidence!.Value);
        Assert.Equal("low", answer.Legend!["1"]);
        Assert.Equal(0.61, answer.Probabilities!["2"]);
    }

    public static async Task SystemOneAsync_RetriesAfter429ThenSucceeds()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "1"),
            (HttpStatusCode.OK, ChoiceResponseBody, null));
        var client = new JevClient(
            BaseUrl,
            ApiKey,
            "jev-latest",
            new HttpClient(handler),
            retryDelay: _ => TimeSpan.FromMilliseconds(1));

        var response = await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("play:0", response.Answers!["risk"].Choice);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(SystemOneUrl, handler.Urls[1]);
    }

    public static async Task SystemOneAsync_RateLimitAfterRetriesSurfacesAsRateLimited()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "0"),
            (HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "0"),
            (HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "0"),
            (HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "0"));
        var client = new JevClient(
            BaseUrl,
            ApiKey,
            "jev-latest",
            new HttpClient(handler),
            retryDelay: _ => TimeSpan.FromMilliseconds(1));

        JevException? caught = null;
        try
        {
            await client.SystemOneAsync(SampleRequest(), CancellationToken.None);
        }
        catch (JevException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Equal(JevExceptionKind.RateLimited, caught!.Kind);
        Assert.Equal<int?>(429, caught.StatusCode);
        // The first attempt plus three retries; a fourth would be a request the caller never approved.
        Assert.Equal(4, handler.RequestCount);
    }

    public static async Task SystemOneAsync_CallerCancellationSurfacesAsOperationCanceled()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var handler = new CancelingHandler();
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var canceled = false;
        var jevFailure = false;
        try
        {
            await client.SystemOneAsync(SampleRequest(), cancel.Token);
        }
        catch (JevException)
        {
            jevFailure = true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            canceled = true;
        }

        Assert.True(canceled, "caller cancellation must propagate as OperationCanceledException");
        Assert.False(jevFailure, "caller cancellation must not be filed as a provider failure");
        Assert.Equal(1, handler.RequestCount);
    }

    public static async Task SystemOneAsync_UnauthorizedMapsToConfigError()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Unauthorized, string.Empty, null));
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        JevException? caught = null;
        try
        {
            await client.SystemOneAsync(SampleRequest(), CancellationToken.None);
        }
        catch (JevException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Equal(JevExceptionKind.Config, caught!.Kind);
        Assert.Equal<int?>(401, caught.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    public static async Task SystemOneAsync_ServerFailureMapsToServerError()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.InternalServerError, """{"error":"boom"}""", null));
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        JevException? caught = null;
        try
        {
            await client.SystemOneAsync(SampleRequest(), CancellationToken.None);
        }
        catch (JevException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Equal(JevExceptionKind.Network, caught!.Kind);
        Assert.Equal<int?>(500, caught.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    public static async Task SystemOneAsync_AnswerlessBodyIsAServerError()
    {
        var handler = new RecordingHandler("""{"model":"jev-1.13.0"}""");
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        JevException? caught = null;
        try
        {
            await client.SystemOneAsync(SampleRequest(), CancellationToken.None);
        }
        catch (JevException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Equal(JevExceptionKind.Server, caught!.Kind);
        Assert.Null(caught!.StatusCode);
    }

    public static async Task PingAsync_AsksOneRealQuestion()
    {
        var handler = new RecordingHandler("""{"model":"jev-1.13.0","answers":{"probe":{"type":"choice","choice":"a","confidence":0.9}}}""");
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var reply = await client.PingAsync(CancellationToken.None);

        Assert.True(reply.StartsWith("OK · jev-1.13.0 · ", StringComparison.Ordinal), reply);
        Assert.Equal(SystemOneUrl, handler.LastUrl);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("choice", body.RootElement.GetProperty("questions").GetProperty("probe").GetProperty("type").GetString());
    }

    public static async Task PingAsync_ReportsTheFailureInsteadOfThrowing()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Unauthorized, string.Empty, null));
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var reply = await client.PingAsync(CancellationToken.None);

        // The status line the settings screen shows. It reports the failure rather than throwing,
        // because its caller is a probe, not a decision path.
        Assert.Contains("Jev HTTP 401", reply);
        Assert.Equal(1, handler.RequestCount);
    }

    public static void Endpoint_RootGetsTheDefaultPath()
    {
        Assert.Equal(SystemOneUrl, JevEndpoint.RequestUrl(BaseUrl));
        Assert.Equal(SystemOneUrl, JevEndpoint.RequestUrl(BaseUrl + "/"));
    }

    public static void Endpoint_FullRequestUrlIsUsedAsWritten()
    {
        Assert.Equal("https://openrouter.ai/api/v1/systemone", JevEndpoint.RequestUrl("https://openrouter.ai/api/v1/systemone"));
        Assert.Equal("https://openrouter.ai/api/v1/systemone", JevEndpoint.RequestUrl(" https://openrouter.ai/api/v1/systemone/ "));
        Assert.Equal("https://api.inceptionlabs.ai/v1/decisions", JevEndpoint.RequestUrl("https://api.inceptionlabs.ai/v1/decisions"));
        const string cloudflare = "https://api.cloudflare.com/client/v4/accounts/abc123/ai/run/@cf/cloudflare/clef";
        Assert.Equal(cloudflare, JevEndpoint.RequestUrl(cloudflare));
        Assert.Equal("cloudflare", JevEndpoint.FindPreset(cloudflare)!.Id);
        Assert.True(JevEndpoint.HasPlaceholder(JevEndpoint.Presets.Single(p => p.Id == "cloudflare").Url));
        Assert.False(JevEndpoint.HasPlaceholder(cloudflare));
    }

    public static void Endpoint_EveryPresetResolvesToItself()
    {
        foreach (var preset in JevEndpoint.Presets)
        {
            Assert.Equal(preset.Url, JevEndpoint.RequestUrl(preset.Url));
            Assert.True(JevProtocol.All.Contains(preset.Protocol), $"preset {preset.Id} has an unknown format");
            Assert.True(preset.DocsUrl.StartsWith("https://", StringComparison.Ordinal), $"preset {preset.Id} needs a docs link");
            Assert.True(preset.KeyUrl.Length == 0 || preset.KeyUrl.StartsWith("https://", StringComparison.Ordinal), $"preset {preset.Id} key link");
            Assert.Equal(preset.Id, JevEndpoint.FindPreset(preset.Url)!.Id);
            Assert.True(Uri.TryCreate(preset.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https",
                $"preset {preset.Id} must be an https URL");
            Assert.True(preset.Models.Length > 0 && preset.Models.All(m => m.Length > 0), $"preset {preset.Id} needs models");
        }

        Assert.Null(JevEndpoint.FindPreset(BaseUrl));
    }

    public static void Endpoint_EveryPresetHasAnEmbeddedLogo()
    {
        var csproj = AgentSourceFixture.Read("STS2AIAgent/STS2AIAgent.csproj");
        Assert.Contains("Assets/jev/*.png", csproj);
        Assert.Contains("STS2AIAgent.JevLogo.", csproj);
        foreach (var preset in JevEndpoint.Presets)
        {
            var path = Path.Combine(AgentSourceFixture.Root, "STS2AIAgent", "Assets", "jev", preset.Id + ".png");
            Assert.True(File.Exists(path), $"preset {preset.Id} needs a logo at {path}");
            var header = File.ReadAllBytes(path).Take(8).ToArray();
            Assert.True(header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                $"{preset.Id}.png must be a PNG");
        }
    }

    public static async Task SystemOneAsync_PostsToAFullProviderUrl()
    {
        var handler = new RecordingHandler(ChoiceResponseBody);
        var client = new JevClient("https://openrouter.ai/api/v1/systemone", ApiKey, "typesafe/jev-1.13", new HttpClient(handler));

        await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("https://openrouter.ai/api/v1/systemone", handler.LastUrl);
    }

    public static async Task Decisions_SendsNamedQuestionsAndReadsTheAnswerList()
    {
        var handler = new RecordingHandler("""
        {
          "model": "gpt-6-luna",
          "answers": [
            { "name": "risk", "type": "choice", "choice": "strike", "confidence": 0.8,
              "probabilities": [ { "value": "strike", "probability": 0.8 }, { "value": "defend", "probability": 0.2 } ] },
            { "name": "lethal", "type": "predicate", "probability": 0.1 }
          ],
          "usage": { "input_tokens": 50, "output_tokens": 3 }
        }
        """);
        var client = new JevClient("https://api.openai.com/v1/decisions", ApiKey, "gpt-6-luna", new HttpClient(handler),
            protocol: JevProtocol.OpenAiDecisions);

        var response = await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/decisions", handler.LastUrl);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("jev-latest", root.GetProperty("model").GetString());
        Assert.Contains("\"hp\":10", root.GetProperty("input").GetString()!);
        var questions = root.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal("choice", questions[0].GetProperty("type").GetString());
        Assert.Equal("risk", questions[0].GetProperty("name").GetString());
        Assert.Equal("strike", questions[0].GetProperty("choices")[0].GetProperty("value").GetString());
        Assert.Equal("predicate", questions[1].GetProperty("type").GetString());

        Assert.Equal("strike", response.Answers!["risk"].Choice);
        Assert.Equal(0.8, response.Answers["risk"].Confidence!.Value);
        Assert.Equal(0.2, response.Answers["risk"].Probabilities!["defend"]);
        Assert.Equal("noul", response.Answers["lethal"].Type);
        Assert.Equal(0.1, response.Answers["lethal"].Noul!.Value);
        Assert.Equal(50, response.Usage!.InputTokens);
    }

    public static async Task Decisions_SendsTheDeciderScoreQuestionAsLevels()
    {
        var handler = new RecordingHandler("""{"answers":[{"name":"danger","type":"score","score":1.5,"confidence":0.4}]}""");
        var client = new JevClient("https://api.openai.com/v1/decisions", ApiKey, "gpt-6-luna", new HttpClient(handler),
            protocol: JevProtocol.OpenAiDecisions);
        var request = new JevRequest
        {
            Model = "",
            State = JsonNode.Parse("""{"hp":3}"""),
            Questions = new Dictionary<string, JevQuestion>
            {
                ["danger"] = new JevQuestion { Type = "score", Instructions = new { goal = "rate" }, Criteria = new[] { "safe", "lethal" } }
            }
        };

        var response = await client.SystemOneAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var question = body.RootElement.GetProperty("questions")[0];
        Assert.Equal("lethal", question.GetProperty("levels")[1].GetProperty("label").GetString());
        Assert.Contains("\"goal\"", question.GetProperty("instructions").GetString()!);
        Assert.Equal(1.5, response.Answers!["danger"].Score!.Value);
    }

    public static async Task Decisions_RefusalIsAServerError()
    {
        var handler = new RecordingHandler("""{"answers":[{"name":"risk","type":"refusal"}]}""");
        var client = new JevClient("https://api.openai.com/v1/decisions", ApiKey, "gpt-6-luna", new HttpClient(handler),
            protocol: JevProtocol.OpenAiDecisions);
        JevException? caught = null;
        try { await client.SystemOneAsync(SampleRequest(), CancellationToken.None); }
        catch (JevException ex) { caught = ex; }
        Assert.Equal(JevExceptionKind.Server, caught!.Kind);
    }

    public static async Task SystemOne_UnwrapsCloudflareResult()
    {
        var handler = new RecordingHandler("""{"result":{"answers":{"risk":{"type":"choice","choice":"defend","confidence":0.7}}},"success":true,"errors":[]}""");
        var client = new JevClient("https://api.cloudflare.com/client/v4/accounts/abc/ai/run/@cf/cloudflare/clef-flash", ApiKey, "clef-flash", new HttpClient(handler));

        var response = await client.SystemOneAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("defend", response.Answers!["risk"].Choice);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/abc/ai/run/@cf/cloudflare/clef-flash", handler.LastUrl);
    }

    public static async Task SystemOne_CloudflareFailureCarriesItsMessage()
    {
        var handler = new RecordingHandler("""{"result":null,"success":false,"errors":[{"message":"bad model"}]}""");
        var client = new JevClient("https://api.cloudflare.com/client/v4/accounts/abc/ai/run/@cf/cloudflare/clef-flash", ApiKey, "clef-flash", new HttpClient(handler));
        JevException? caught = null;
        try { await client.SystemOneAsync(SampleRequest(), CancellationToken.None); }
        catch (JevException ex) { caught = ex; }
        Assert.Equal(JevExceptionKind.Server, caught!.Kind);
        Assert.Contains("bad model", caught.Message);
    }

    public static void UnfilledPlaceholderIsRejectedAtConstruction()
    {
        JevException? caught = null;
        try { _ = new JevClient(JevEndpoint.Presets.Single(p => p.Id == "cloudflare").Url, ApiKey, "clef-flash"); }
        catch (JevException ex) { caught = ex; }
        Assert.Equal(JevExceptionKind.Config, caught!.Kind);
        Assert.Contains("placeholder", caught.Message);
    }

    public static void BlankConfigurationIsRejectedAtConstruction()
    {
        JevException? caught = null;
        try
        {
            _ = new JevClient(BaseUrl, "  ", "jev-latest", new HttpClient(new RecordingHandler(ModelsBody)));
        }
        catch (JevException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Equal(JevExceptionKind.Config, caught!.Kind);
        Assert.False(
            caught.Message.Contains(ApiKey, StringComparison.Ordinal),
            "the API key must never appear in an error message");
    }

    private static JevRequest SampleRequest()
    {
        return new JevRequest
        {
            State = JsonNode.Parse("""{"hp":10,"hand":["strike","defend"]}"""),
            Model = "jev-latest",
            Questions = new Dictionary<string, JevQuestion>
            {
                ["risk"] = new JevQuestion
                {
                    Type = "choice",
                    Instructions = "Pick the safer attack",
                    Criteria = new Dictionary<string, string>
                    {
                        ["strike"] = "Attack now",
                        ["defend"] = "Block this turn"
                    }
                },
                ["lethal"] = new JevQuestion
                {
                    Type = "noul",
                    Instructions = "Is any enemy about to die?"
                }
            }
        };
    }

    /// <summary>Answers every request with one fixed response, and records what it was asked.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public RecordingHandler(string response)
        {
            _response = response;
        }

        public string LastUrl { get; private set; } = string.Empty;

        public HttpMethod LastMethod { get; private set; } = HttpMethod.Get;

        public string? LastBody { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString() ?? string.Empty;
            LastMethod = request.Method;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
        }
    }

    /// <summary>
    /// Returns a scripted sequence of responses, so a retry can be told apart from the request it
    /// replaced. Each scripted response may carry a <c>retry-after</c> value.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, string? RetryAfter)> _script;

        public ScriptedHandler(params (HttpStatusCode Status, string Body, string? RetryAfter)[] script)
        {
            _script = new Queue<(HttpStatusCode, string, string?)>(script);
        }

        public List<string> Bodies { get; } = new();

        public List<string> Urls { get; } = new();

        public int RequestCount => Bodies.Count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri?.ToString() ?? string.Empty);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_script.Count == 0)
            {
                throw new InvalidOperationException("The client sent more requests than the test scripted.");
            }

            var (status, body, retryAfter) = _script.Dequeue();
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (retryAfter != null)
            {
                response.Headers.TryAddWithoutValidation("retry-after", retryAfter);
            }

            return response;
        }
    }

    /// <summary>
    /// Holds a request open on the caller's token, the way a real stalled provider does, so the test
    /// can prove cancellation is not being swallowed by the client.
    /// </summary>
    private sealed class CancelingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request was never expected to complete.");
        }
    }
}
