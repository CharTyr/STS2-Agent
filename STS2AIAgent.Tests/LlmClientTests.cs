using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class ThinkingRequestBuilderTests
{
    public static void Infer(string model, string mode, string expected)
    {
        var fields = ThinkingRequestBuilder.Build(model, mode, ThinkingIntensity.Medium);
        if (expected == "reasoning_effort")
        {
            Assert.Equal("medium", fields.ReasoningEffort);
            Assert.Null(fields.DeepSeekThinking);
        }
        else if (expected == "deepseek")
        {
            Assert.Null(fields.ReasoningEffort);
            Assert.Equal("enabled", fields.DeepSeekThinking?["type"]);
        }
        else
        {
            Assert.Null(fields.ReasoningEffort);
            Assert.Null(fields.DeepSeekThinking);
        }

        Assert.False(string.IsNullOrWhiteSpace(fields.PromptSuffix));
    }

    public static void Off_DisablesDeepSeekThinking()
    {
        var fields = ThinkingRequestBuilder.Build("deepseek-r1", "auto", ThinkingIntensity.Off);
        Assert.Null(fields.ReasoningEffort);
        Assert.Equal("disabled", fields.DeepSeekThinking?["type"]);
    }
}

internal static class OpenAiCompatibleClientTests
{
    public static void ResolveCompletionsUrl_NormalizesBase()
    {
        Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiCompatibleClient.ResolveCompletionsUrl("https://api.openai.com/v1"));
        Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiCompatibleClient.ResolveCompletionsUrl("https://api.openai.com/v1/"));
        Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiCompatibleClient.ResolveCompletionsUrl("https://api.openai.com/v1/chat/completions"));
    }

    public static void ParseCompletion_ReadsToolCallsAndReasoning()
    {
        const string payload = """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "playing",
                "reasoning_content": "need to strike",
                "tool_calls": [
                  {
                    "id": "call_1",
                    "type": "function",
                    "function": {
                      "name": "act",
                      "arguments": "{\"action\":\"play_card\",\"card_index\":0}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

        var completion = OpenAiCompatibleClient.ParseCompletion(payload);
        Assert.Equal("playing", completion.Content);
        Assert.Equal("need to strike", completion.Reasoning);
        Assert.Single(completion.ToolCalls);
        Assert.Equal("act", completion.ToolCalls[0].Name);
        Assert.Contains("play_card", completion.ToolCalls[0].ArgumentsJson);
    }

    public static async Task CompleteAsync_PostsOpenAiCompatibleBody()
    {
        var handler = new RecordingHandler("""
        {"choices":[{"message":{"role":"assistant","content":"pong"}}]}
        """);
        var client = new OpenAiCompatibleClient(
            new LlmEndpoint { BaseUrl = "https://example.test/v1", ApiKey = "sk-test" },
            handler);

        var completion = await client.CompleteAsync(new LlmRequest
        {
            Model = "gpt-5",
            Messages = new[] { LlmMessage.System("sys"), LlmMessage.User("hi") },
            Tools = Agent.AgentTools.ReadOnly,
            Thinking = ThinkingIntensity.High,
            ThinkingMode = "auto"
        }, CancellationToken.None);

        Assert.Equal("pong", completion.Content);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"reasoning_effort\":\"high\"", handler.LastBody);
        Assert.Contains("\"stream\":true", handler.LastBody);
        Assert.True(handler.LastHeaders.Contains("Authorization"), "missing Authorization header");
        Assert.EndsWith("/chat/completions", handler.LastUrl);
    }

    public static async Task CompleteAsync_PostsDeepSeekThinkingInExtraBody()
    {
        var handler = new RecordingHandler("""
        {"choices":[{"message":{"role":"assistant","content":"ok"}}]}
        """);
        var client = new OpenAiCompatibleClient(
            new LlmEndpoint { BaseUrl = "https://example.test/v1", ApiKey = "sk-test" },
            handler);

        await client.CompleteAsync(new LlmRequest
        {
            Model = "deepseek-chat",
            Messages = new[] { LlmMessage.User("hi") },
            Thinking = ThinkingIntensity.Medium,
            ThinkingMode = "deepseek",
            Stream = false
        }, CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        using var document = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.True(document.RootElement.TryGetProperty("extra_body", out var extraBody));
        Assert.Equal("enabled", extraBody.GetProperty("thinking").GetProperty("type").GetString());
    }

    public static void ParseSse_AccumulatesContentAndToolCalls()
    {
        const string payload = """
        data: {"choices":[{"delta":{"content":"play "}}]}

        data: {"choices":[{"delta":{"content":"strike","tool_calls":[{"index":0,"id":"call_1","function":{"name":"act","arguments":"{\"action\""}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":":\"play_card\"}"}}]}}]}

        data: [DONE]
        """;

        var completion = OpenAiCompatibleClient.ParseSsePayload(payload);
        Assert.Equal("play strike", completion.Content);
        Assert.Single(completion.ToolCalls);
        Assert.Equal("act", completion.ToolCalls[0].Name);
        Assert.Contains("play_card", completion.ToolCalls[0].ArgumentsJson);
    }

    public static void ParseCompletion_ReadsUsage()
    {
        const string payload = """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "ok"
              }
            }
          ],
          "usage": {
            "prompt_tokens": 120,
            "completion_tokens": 30,
            "total_tokens": 150
          }
        }
        """;

        var completion = OpenAiCompatibleClient.ParseCompletion(payload);
        Assert.NotNull(completion.Usage);
        Assert.Equal(120, completion.Usage!.PromptTokens);
        Assert.Equal(30, completion.Usage.CompletionTokens);
        Assert.Equal(150, completion.Usage.TotalTokens);
    }

    public static void ParseSse_ReadsUsageFromEndChunk()
    {
        const string payload = """
        data: {"choices":[{"delta":{"content":"hello"}}]}

        data: {"choices":[],"usage":{"prompt_tokens":50,"completion_tokens":10,"total_tokens":60}}

        data: [DONE]
        """;

        var completion = OpenAiCompatibleClient.ParseSsePayload(payload);
        Assert.Equal("hello", completion.Content);
        Assert.NotNull(completion.Usage);
        Assert.Equal(50, completion.Usage!.PromptTokens);
        Assert.Equal(10, completion.Usage.CompletionTokens);
        Assert.Equal(60, completion.Usage.TotalTokens);
    }

    public static void LlmUsage_CombineAndAdd()
    {
        var u1 = new LlmUsage { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 };
        var u2 = new LlmUsage { PromptTokens = 20, CompletionTokens = 8, TotalTokens = 28 };

        var sum = u1 + u2;
        Assert.Equal(30, sum.PromptTokens);
        Assert.Equal(13, sum.CompletionTokens);
        Assert.Equal(43, sum.TotalTokens);

        var combinedWithNull = LlmUsage.Combine(u1, null);
        Assert.Equal(15, combinedWithNull!.TotalTokens);

        var combinedBoth = LlmUsage.Combine(u1, u2);
        Assert.Equal(43, combinedBoth!.TotalTokens);
    }

    public static async Task CompleteAsync_HeadersThenStalledBodyTimesOut()
    {
        using var stall = new StallingHeaderServer();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.ExpectContinue = false;
        var client = new OpenAiCompatibleClient(
            new LlmEndpoint { BaseUrl = stall.BaseUrl },
            httpClient: http,
            requestTimeout: TimeSpan.FromMilliseconds(400));

        var elapsed = Stopwatch.StartNew();
        try
        {
            await client.CompleteAsync(new LlmRequest
            {
                Model = "test",
                Messages = new[] { LlmMessage.User("hi") },
                Stream = true
            }, CancellationToken.None);
            throw new Exception("Expected a timeout after ResponseHeadersRead stall.");
        }
        catch (LlmException ex)
        {
            Assert.Equal<int?>(408, ex.StatusCode);
            Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        elapsed.Stop();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8), $"stall timeout took {elapsed.Elapsed}, expected a short injected timeout");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(200), $"stall timeout returned too quickly: {elapsed.Elapsed}");
    }

    public static async Task CompleteAsync_HeadersThenStalledBodyUserCancel()
    {
        using var stall = new StallingHeaderServer();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.ExpectContinue = false;
        var client = new OpenAiCompatibleClient(
            new LlmEndpoint { BaseUrl = stall.BaseUrl },
            httpClient: http,
            requestTimeout: TimeSpan.FromSeconds(5));

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var elapsed = Stopwatch.StartNew();
        var canceled = false;
        try
        {
            await client.CompleteAsync(new LlmRequest
            {
                Model = "test",
                Messages = new[] { LlmMessage.User("hi") },
                Stream = true
            }, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            canceled = true;
        }

        elapsed.Stop();
        Assert.True(canceled, "user cancellation must surface as OperationCanceledException, not a timeout LlmException");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3), $"user cancel took {elapsed.Elapsed}");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public RecordingHandler(string response)
        {
            _response = response;
        }

        public string? LastBody { get; private set; }

        public string LastUrl { get; private set; } = string.Empty;

        public List<string> LastHeaders { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString() ?? string.Empty;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            LastHeaders.AddRange(request.Headers.Select(header => header.Key));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
        }
    }

    private sealed class StallingHeaderServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _loop;

        public string BaseUrl { get; }

        public StallingHeaderServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}/v1";
            _loop = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                    _ = ServeAsync(client);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                using (client)
                await using (var stream = client.GetStream())
                {
                    var buffer = new byte[8192];
                    var total = 0;
                    while (total < buffer.Length)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(total), _lifetime.Token);
                        if (read == 0)
                        {
                            return;
                        }

                        total += read;
                        if (HasHeaderDelimiter(buffer, total))
                        {
                            break;
                        }
                    }

                    var headers = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1048576\r\nConnection: keep-alive\r\n\r\n");
                    await stream.WriteAsync(headers, _lifetime.Token);
                    await stream.FlushAsync(_lifetime.Token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
            {
            }
        }

        private static bool HasHeaderDelimiter(byte[] buffer, int length)
        {
            for (var i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' &&
                    buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            _lifetime.Dispose();
        }
    }
}
