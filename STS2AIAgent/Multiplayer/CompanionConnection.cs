using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Multiplayer;

internal sealed class CompanionConnection
{
    public const string TokenEnvironment = "STS2_COMPANION_SESSION_TOKEN";
    public const string TokenHeader = "X-STS2-Companion-Session";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _token;
    private readonly int _port;
    private readonly int _pid;
    private readonly HttpClient _http;

    public CompanionConnection(int port, int pid, string token, HttpClient? http = null)
    {
        _port = port;
        _pid = pid;
        _token = token;
        _http = http ?? Http;
    }

    public static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static bool IsAuthorized(string? expected, string? supplied)
    {
        return !string.IsNullOrEmpty(expected) && expected.Length == 64 && supplied?.Length == 64 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    }

    public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > TeamConversation.MaxMessageLength)
            throw new ArgumentException("请输入 1–2000 个字符的队伍消息。");
        var data = await SendAsync("message", new { message }, cancellationToken);
        return data.GetProperty("reply").GetString() ?? "队友没有返回文本。";
    }

    public async Task<string> ControlAsync(bool running, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var data = await SendAsync("control", new { running }, deadline.Token);
        return data.GetProperty("phase").GetString() ?? "unknown";
    }

    private async Task<JsonElement> SendAsync(string operation, object payload, CancellationToken cancellationToken)
    {
        // A stale session must never send messages to a replacement process that
        // happens to reuse the same port. The private session token is checked too.
        using var healthDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        healthDeadline.CancelAfter(TimeSpan.FromSeconds(3));
        var health = await _http.GetStringAsync($"http://127.0.0.1:{_port}/health", healthDeadline.Token);
        if (!CompanionHealth.IsExpectedProcess(health, _port, _pid))
            throw new InvalidOperationException("AI 队友连接已改变，请重新组队。");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/companion/{operation}");
        request.Headers.Add(TokenHeader, _token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!response.IsSuccessStatusCode || !body.RootElement.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException("队友未能确认请求，请查看队友窗口中的状态。请求不会自动重发。");
        return body.RootElement.GetProperty("data").Clone();
    }
}
