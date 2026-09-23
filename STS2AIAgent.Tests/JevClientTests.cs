using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>A score answer, which carries a fractional score plus a legend and its own confidence.</summary>
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
        Assert.Equal(JevExceptionKind.Server, caught!.Kind);
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

    public static async Task PingAsync_ReportsTheModelCount()
    {
        var handler = new RecordingHandler(ModelsBody);
        var client = new JevClient(BaseUrl, ApiKey, "jev-latest", new HttpClient(handler));

        var reply = await client.PingAsync(CancellationToken.None);

        Assert.Equal("OK, 2 models", reply);
        Assert.Equal(ModelsUrl, handler.LastUrl);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
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
