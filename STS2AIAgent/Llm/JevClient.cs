using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using STS2AIAgent.Agent;

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
    /// Timeout applied to each request using the dedicated shared connection pool. Ten minutes would suit a chat
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

    private const int RateLimitedStatus = (int)HttpStatusCode.TooManyRequests;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _http;
    private readonly TimeSpan? _requestTimeout;

    private readonly string _baseUrl;

    private readonly string _apiKey;

    private readonly string _model;

    private readonly string _protocol;

    private readonly int _maxRetries;

    private readonly Func<int, TimeSpan> _retryDelay;

    /// <param name="baseUrl">Service root, or the provider's full request URL (see <see cref="JevEndpoint"/>). A trailing <c>/</c> is trimmed.</param>
    /// <param name="protocol">Wire format, one of <see cref="JevProtocol.All"/>; blank means System One.</param>
    /// <param name="apiKey">Bearer token. Secret: never logged, never rendered in an exception.</param>
    /// <param name="model">Default Jev model id, used when a request leaves its own blank.</param>
    /// <param name="httpClient">
    /// Optional transport. Its own timeout is left intact; an explicit requestTimeout adds a per-call
    /// deadline. Without an injected transport, the shared pool uses DefaultRequestTimeout per attempt.
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
        Func<int, TimeSpan>? retryDelay = null,
        TimeSpan? requestTimeout = null,
        string? protocol = null)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = (apiKey ?? string.Empty).Trim();
        _model = model ?? string.Empty;
        _protocol = JevProtocol.Normalize(protocol);
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _http = httpClient ?? SharedHttp;
        _requestTimeout = requestTimeout is { } t && t > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Min(t.TotalMilliseconds, uint.MaxValue - 1d))
            : httpClient == null ? DefaultRequestTimeout : null;

        // Reported here rather than on the first request, because an unconfigured client is a
        // configuration mistake and every call it makes would 401 anyway.
        if (_baseUrl.Length == 0)
        {
            throw new JevException("Fast judgment base URL is empty.", JevExceptionKind.Config);
        }

        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new JevException("Fast judgment base URL must be an HTTP(S) service URL without credentials, query, or fragment.", JevExceptionKind.Config);

        if (JevEndpoint.HasPlaceholder(_baseUrl))
            throw new JevException("Fast judgment base URL still contains a {placeholder}; replace it with your own value (for Cloudflare, the account id).", JevExceptionKind.Config);

        if (_apiKey.Length == 0)
        {
            throw new JevException("Fast judgment API key is empty.", JevExceptionKind.Config);
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
        var body = JevProtocol.BuildBody(_protocol, ResolveRequest(request), JsonOptions);
        return SendAsync(
            () => CreateRequest(HttpMethod.Post, JevEndpoint.RequestUrl(_baseUrl), body),
            ParseResponse,
            cancellationToken);
    }

    /// <summary>
    /// The connection test: one real, minimal question in the configured format, so the address, key,
    /// model and format are all proven by the same call play will make. It costs one small request.
    /// </summary>
    public async Task<string> PingAsync(CancellationToken cancellationToken)
    {
        var probe = new JevRequest
        {
            Model = _model,
            State = JsonNode.Parse("""{"probe":"connection test"}"""),
            Questions = new Dictionary<string, JevQuestion>
            {
                [ProbeId] = new JevQuestion
                {
                    Type = "choice",
                    Instructions = "Connection test. Choose option a.",
                    Criteria = new Dictionary<string, object> { ["a"] = "Option a", ["b"] = "Option b" }
                }
            }
        };
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await SystemOneAsync(probe, cancellationToken);
            var model = string.IsNullOrWhiteSpace(response.Model) ? _model : response.Model;
            return response.Answers!.TryGetValue(ProbeId, out var answer) && answer?.Choice is "a" or "b"
                ? $"OK · {model} · {started.ElapsedMilliseconds} ms"
                : $"Connected ({model}), but the answer did not match the question.";
        }
        catch (JevException ex)
        {
            // A settings screen shows this string; it is a status line, not a stack trace. Only the
            // caller's own cancellation escapes, because that is not the probe failing.
            return ex.Message;
        }
    }

    private const string ProbeId = "probe";

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
                            $"Fast judgment rate limit persisted through {attempt - 1} retries.",
                            JevExceptionKind.RateLimited,
                            RateLimitedStatus,
                            retryAfterSeconds: Math.Clamp(seconds, 0, MaxRetryAfterSeconds));
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
        using var timeout = _requestTimeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        if (timeout != null) timeout.CancelAfter(_requestTimeout!.Value);
        HttpRequestMessage? request = null;
        try
        {
            request = requestFactory();
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout?.Token ?? cancellationToken);
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
            return new AttemptOutcome(null, new JevException("Fast judgment request timed out.", timeout?.IsCancellationRequested == true ? JevExceptionKind.Network : ClassifyCancellation(ex)));
        }
        catch (HttpRequestException ex)
        {
            return new AttemptOutcome(null, new JevException($"Fast judgment request failed: {SafeText(ex.Message)}", JevExceptionKind.Network));
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

    private async Task<JevException> ToFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var body = Snapshot(await ReadBodyAsync(response, cancellationToken));
        var kind = status >= 400 && status < 500 && status is not (408 or 429)
            ? JevExceptionKind.Config
            : JevExceptionKind.Network;
        return new JevException($"Fast judgment HTTP {status}: {body}", kind, status);
    }

    private JevResponse ParseResponse(string payload)
    {
        try
        {
            return JevProtocol.ParseResponse(_protocol, payload, JsonOptions);
        }
        catch (JevException ex)
        {
            throw new JevException(SafeText(ex.Message), ex.Kind, ex.StatusCode);
        }
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

    private string SafeText(string text) => DiagnosticExport.Redact(text.Replace(_apiKey, "[redacted]", StringComparison.Ordinal));

    private string Snapshot(string body)
    {
        var trimmed = SafeText(body).Trim();
        return trimmed.Length > 400 ? trimmed[..400] + "..." : trimmed;
    }

    /// <summary>What one send produced: a response to classify, or the failure to surface.</summary>
    private readonly record struct AttemptOutcome(HttpResponseMessage? Response, JevException? Failure);
}
