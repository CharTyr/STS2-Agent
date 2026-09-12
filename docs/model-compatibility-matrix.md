# OpenAI-Compatible Model Compatibility Matrix

> Last updated: 2026-09-08. Grounded in the current source (STS2AIAgent/Llm/OpenAiCompatibleClient.cs) and offline tests; live-model sampling remains a per-provider follow-up. This document complements the status page; it is not a substitute for a live-model test against a real endpoint.

Scope: behavior expected from any OpenAI-compatible /v1/chat/completions endpoint (official OpenAI, DeepSeek, SiliconFlow, OpenRouter, Ollama, LM Studio, vLLM, and clones). A row is `Verified` only when there is an executable test or direct source contract; `Partial` means the code path exists but only samples were validated; `Gap` means not covered yet.

## Request Surface

| Capability | Behavior in code | Evidence | Status |
| --- | --- | --- | --- |
| Tools / function calling | tools array serialised via ToToolDto (type=function, name, description, parameters); assistant tool_calls parsed with per-index accumulation for SSE deltas | TestRunner AgentTools.*; SSE accumulation in ParseSsePayload | Verified |
| JSON fallback when tools unavailable | ShouldRetryWithoutStream demotes only HTTP 400/415/422 mentioning stream; agent fallback JsonActFallback asks for a bare JSON action object | OpenAI.ParseCompletion; AgentLoop.JsonActNoTools, JsonIgnoredWithTools, CrystalJsonNoTools | Verified |
| Thinking / reasoning effort | reasoning_effort sent when configured; gpt-4o / gpt-5 / o3-mini / deepseek / explicit / off mapping in ThinkingRequestBuilder | Thinking.* tests | Verified |
| DeepSeek-style thinking wrapper | thinking body + extra_body.thinking sent only for deepseek profile | OpenAI.DeepSeekExtraBody | Verified |
| Vision (image input) | image_url data URL content parts when ImageJpeg present | ToMessageDto path; no dedicated offline test | Partial |
| Cancellation | play_card / request cancellation propagates through linked CTS | AgentLoop.CancelPropagates | Verified |
| Connection reuse / streaming body | HttpCompletionOption.ResponseHeadersRead when streaming; SSE parsing with [DONE] handling | OpenAI.ParseSse, ParseSsePayload | Verified |

## Usage Accounting

| Capability | Behavior | Evidence | Status |
| --- | --- | --- | --- |
| Non-stream usage | usage.prompt_tokens/completion_tokens/total_tokens read; total<=0 falls back to prompt+completion | OpenAI.ParseCompletionUsage, LlmUsageMath | Verified |
| Streamed usage | stream request sets stream_options.include_usage=true; last SSE chunk usage wins | OpenAI.ParseSseUsage; Usage.MissingNotZero | Verified |
| Missing usage | ReadUsage returns null when absent; budget treats missing usage as nonzero (usage unknown) | Usage.MissingNotZero; Budget.* | Verified |

## Error and Timeout Handling

| Capability | Behavior | Evidence | Status |
| --- | --- | --- | --- |
| 401 / 429 / 5xx | Non-success status raises LlmException(message, statusCode) with message parsed from error.message; no silent retry | FormatError in OpenAiCompatibleClient; failure-kind tests | Verified |
| Stream-unsupported demotion | Only 400/415/422 with stream wording is retried without stream; other status codes surface directly | ShouldRetryWithoutStream | Verified |
| Request timeout | Client timeout = configured request timeout + 1 min; per-call CancellationTokenSource(_requestTimeout); timeout raises 408-style LlmException | OpenAI.StalledBodyTimeout, StalledBodyUserCancel | Verified |
| Stalled body | Loopback tests return headers first, then stall; bounded timeout cancels body read | OpenAI.StalledBodyTimeout, StalledBodyUserCancel | Verified |
| Retry / backoff (transport) | MaxRetries on the Python sidecar and action replay safety; LLM client itself does not auto-retry non-specified statuses | test_action_replay_safety.py; Recovery.* | Verified |

## Provider Notes

- Known-good in live play per history: DeepSeek (chat/play), MiniMax (budget-proxy acceptance), official OpenAI-compatible loopback fixtures.
- Ollama / LM Studio: key may be empty; usage may be absent on some versions -- covered by missing-usage path.
- Not yet sampled live: SiliconFlow, OpenRouter, vLLM native extra_body variants beyond DeepSeek, and providers that require max_tokens vs max_completion_tokens differences.

## Matrix Read Date

Run dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release to re-verify the offline columns after LLM client changes.

