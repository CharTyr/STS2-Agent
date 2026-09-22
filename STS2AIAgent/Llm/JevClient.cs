using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2AIAgent.Llm;

/// <summary>
/// Transport for the TypeSafe Jev <c>/v1/systemone</c> endpoint, used by the dual-layer decision mode
/// where Jev executes an action and the LLM only plans strategy.
/// </summary>
/// <remarks>
/// <para>
/// This client deliberately does <em>not</em> reuse <c>DefaultLlmClientFactory.SharedHttp</c>. That
/// singleton is tuned for long OpenAI-compatible conversations, and a Jev question is asked once per
/// action, so the two layers want different timeouts and different retry budgets. Sharing one instance
/// would also mean a stalled Jev call showing up as the LLM layer's problem in the diagnostics export.
/// </para>
/// <para>
/// The API key is never logged: no exception message, no diagnostic line and no body snapshot carries
/// it. Only the status code and a trimmed response body do.
/// </para>
/// </remarks>
internal sealed class JevClient : IJevClient
{
    /// <summary>
    /// Timeout applied to a <see cref="HttpClient"/> this class creates. Ten minutes would suit a chat
    /// completion; Jev answers a fixed set of questions, so five is generous and still bounded.
    /// </summary>
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ceiling the default retry policy places on a server-supplied <c>retry-after</c>. The service
    /// documents 1200 requests/min and 250k tokens/s; a banner asking for an hour would park the
    /// decision loop, so the default honours the request only up to this bound and then gives up.
    /// </summary>
    internal const int MaxRetryAfterSeconds = 30;

    /// <summary>Retries attempted after the first one, before the failure is surfaced.</summary>
    internal const int DefaultMaxRetries = 3;

    /// <summary>Assumed backoff when a 429 carries no readable <c>retry-after</c>.</summary>
    internal const int DefaultRetryAfterSeconds = 2;

    private const string SystemOnePath = "/v1/systemone";

    private const string ModelsPath = "/v1/models";

    private const int RateLimitedStatus = (int)HttpStatusCode.TooManyRequests;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    private readonly string _baseUrl;

    private readonly string _apiKey;

    private readonly string _model;

    private readonly int _maxRetries;

    private readonly Func<int, TimeSpan> _retryDelay;

    /// <param name="baseUrl">Base URL of the TypeSafe service. A trailing <c>/</c> is trimmed.</param>
    /// <param name="apiKey">Bearer token. Secret: never logged, never rendered in an exception.</param>
    /// <param name="model">Default Jev model id, used when a request leaves its own blank.</param>
    /// <param name="httpClient">
    /// Client to send through. An injected one is used as-is -- its timeout is the caller's business;
    /// without one this class creates its own with <see cref="DefaultRequestTimeout"/>.
    /// </param>
    /// <param name="maxRetries">Retries after the first attempt. Zero disables retrying.</param>
    /// <param name="retryDelay">
    /// Maps the number of seconds the server asked for (or a small default when it asked for nothing)
    /// to the delay actually waited. Injectable so a test can collapse a one-second backoff; the
    /// default is <see cref="DefaultRetryDelay"/>, which also applies the <see cref="MaxRetryAfterSeconds"/> cap.
    /// </param>
    /// <exception cref="JevException"><see cref="JevExceptionKind.Config"/> for an empty base URL or key.</exception>
    public JevClient(
        string baseUrl,
        string apiKey,
        string model,
        HttpClient? httpClient = null,
        int maxRetries = DefaultMaxRetries,
        Func<int, TimeSpan>? retryDelay = null)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
        _model = model ?? string.Empty;
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _http = httpClient ?? new HttpClient { Timeout = DefaultRequestTimeout };

        // Reported here rather than on the first request, because an unconfigured client is a
        // configuration mistake and every call it makes would 401 anyway.
        if (_baseUrl.Length == 0)
        {
            throw new JevException("Jev base URL is empty.", JevExceptionKind.Config);
        }

        if (_apiKey.Length == 0)
        {
            throw new JevException("Jev API key is empty.", JevExceptionKind.Config);
        }
    }

    /// <summary>
    /// Honour the server's <c>retry-after</c>, capped at <see cref="MaxRetryAfterSeconds"/>. Exposed so
    /// a caller can see the policy without reading the constructor's default argument.
    /// </summary>
    internal static TimeSpan DefaultRetryDelay(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaxRetryAfterSeconds));

    public Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.Serialize(ResolveRequest(request), JsonOptions);
        return SendAsync(
            () => CreateRequest(HttpMethod.Post, _baseUrl + SystemOnePath, body),
            ParseResponse,
            cancellationToken);
    }

    public async Task<string> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(
                () => CreateRequest(HttpMethod.Get, _baseUrl + ModelsPath, body: null),
                DescribeModels,
                cancellationToken);
        }
        catch (JevException ex)
        {
            // A settings screen shows this string; it is a status line, not a stack trace. Only the
            // caller's own cancellation escapes, because that is not the probe failing.
            return ex.Message;
        }
    }

    /// <summary>
    /// Fills a blank request model from the configured default. A request that names its own model
    /// wins, so a later caller can pin a version without a second client instance.
    /// </summary>
    private JevRequest ResolveRequest(JevRequest request) =>
        string.IsNullOrWhiteSpace(request.Model)
            ? new JevRequest { State = request.State, Model = _model, Questions = request.Questions }
            : request;

    /// <summary>
    /// One call: send, then read. Retries the whole attempt, since an <see cref="HttpRequestMessage"/>
    /// cannot be reused once it has been sent.
    /// </summary>
    private async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<string, T> parse,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            var outcome = await SendOnceAsync(requestFactory, cancellationToken);

            if (outcome.Response is { } response)
            {
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var seconds = ReadRetryAfterSeconds(response) ?? DefaultRetryAfterSeconds;
                        if (attempt <= _maxRetries)
                        {
                            await Task.Delay(_retryDelay(seconds), cancellationToken);
                            continue;
                        }

                        throw new JevException(
                            $"Jev rate limit persisted through {attempt - 1} retries.",
                            JevExceptionKind.RateLimited,
                            RateLimitedStatus);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        return parse(await ReadBodyAsync(response, cancellationToken));
                    }

                    throw await ToFailureAsync(response, cancellationToken);
                }
            }

            // A transport failure with no response to read: the only remotely likely cause is a
            // dropped connection, so it is worth one bounded retry before the caller sees it.
            if (attempt <= _maxRetries)
            {
                await Task.Delay(_retryDelay(Math.Min(attempt, 8)), cancellationToken);
                continue;
            }

            throw outcome.Failure!;
        }
    }

    private async Task<AttemptOutcome> SendOnceAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage? request = null;
        try
        {
            request = requestFactory();
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            return new AttemptOutcome(response, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller owns this token, so the cancellation is theirs and must reach the ordinary
            // cancellation plumbing rather than be filed as a provider failure.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return new AttemptOutcome(null, new JevException("Jev request timed out.", ClassifyCancellation(ex), ex));
        }
        catch (HttpRequestException ex)
        {
            return new AttemptOutcome(null, new JevException($"Jev request failed: {ex.Message}", JevExceptionKind.Network, ex));
        }
        finally
        {
            request?.Dispose();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    /// <summary>
    /// Reads <c>retry-after</c> as whole seconds. Returns null when the header is missing or not a
    /// plain number, which is the documented case for an HTTP-date form this service does not use.
    /// </summary>
    private static int? ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("retry-after", out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        return int.TryParse((raw ?? string.Empty).Trim(), out var seconds) && seconds >= 0
            ? seconds
            : null;
    }

    private static async Task<JevException> ToFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var body = Snapshot(await ReadBodyAsync(response, cancellationToken));
        var kind = status is 401 or 403 ? JevExceptionKind.Config : JevExceptionKind.Server;
        return new JevException($"Jev HTTP {status}: {body}", kind, status);
    }

    private JevResponse ParseResponse(string payload)
    {
        JevResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<JevResponse>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new JevException($"Jev response was not valid JSON: {ex.Message}", JevExceptionKind.Server);
        }

        if (response?.Answers is null)
        {
            throw new JevException("Jev response did not contain an \"answers\" object.", JevExceptionKind.Server);
        }

        return response;
    }

    /// <summary>
    /// Turns the body of <c>GET /v1/models</c> into a one-line status. <c>models</c> is the array the
    /// service returns; a body that parses but holds no array still means the key was accepted, so it
    /// reads "OK" rather than an error.
    /// </summary>
    private static string DescribeModels(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Array)
            {
                return $"OK, {models.GetArrayLength()} models";
            }
        }
        catch (JsonException)
        {
        }

        return "OK";
    }

    /// <summary>
    /// Splits a cancellation into the two reasons it is not the caller's. <see cref="HttpClient"/>
    /// reports its own timeout as a cancellation, so the trigger has to be read off the exception
    /// itself: since .NET 5 it carries a <see cref="TimeoutException"/> inner exception, and the outer
    /// message names the timeout either way. Anything left over is surfaced as
    /// <see cref="JevExceptionKind.Canceled"/> so it is never mistaken for a provider refusal.
    /// </summary>
    private static JevExceptionKind ClassifyCancellation(OperationCanceledException ex) =>
        ex.InnerException is TimeoutException ||
        ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            ? JevExceptionKind.Network
            : JevExceptionKind.Canceled;

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken) ?? string.Empty;
    }

    private static string Snapshot(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length > 400 ? trimmed[..400] + "..." : trimmed;
    }

    /// <summary>What one send produced: a response to classify, or the failure to surface.</summary>
    private readonly record struct AttemptOutcome(HttpResponseMessage? Response, JevException? Failure);
}
