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
}
