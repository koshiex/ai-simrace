using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class OpenRouterProviderTests
{
    private const string RealTimeSchema =
        """
        { "type": "object", "additionalProperties": false,
          "required": ["action_id", "phrase_ru"],
          "properties": {
            "action_id": { "type": "string", "enum": ["wider_entry", "brake_later_by_meters"] },
            "phrase_ru": { "type": "string", "maxLength": 80 } } }
        """;

    private static readonly HashSet<string> _allowedBodyKeys = new(StringComparer.Ordinal)
    {
        "model", "messages", "max_tokens", "stream", "reasoning", "response_format", "tools", "tool_choice",
    };

    [Fact]
    public async Task Success_maps_content_usage_and_call_info()
    {
        var handler = MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            OpenAiSuccess("{\"action_id\":\"wider_entry\",\"phrase_ru\":\"Шире вход.\"}"));
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema),
            Route("openrouter-google", "google/gemini-2.5-flash-lite"),
            CancellationToken.None);

        LlmResult.Success success = result.Should().BeOfType<LlmResult.Success>().Subject;
        success.Json.Should().Contain("wider_entry");
        success.Usage.InputTokens.Should().Be(120);
        success.Usage.OutputTokens.Should().Be(18);
        success.Usage.CachedInputTokens.Should().Be(40);
        success.Usage.ReasoningTokens.Should().Be(5);
        success.Info.ProviderId.Should().Be("openrouter-google");
        success.Info.ProviderModelId.Should().Be("google/gemini-2.5-flash-lite");
        success.Info.FinishReason.Should().Be("stop");
        success.Info.Latency.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task Request_body_carries_model_messages_knobs_and_response_format()
    {
        var handler = MockHttpMessageHandler.Json(HttpStatusCode.OK, OpenAiSuccess("{}"));
        OpenRouterProvider provider = Provider(handler);

        await provider.CompleteAsync(
            Request(RealTimeSchema),
            Route("openrouter-google", "google/gemini-2.5-flash-lite", maxTokens: 96, reasoning: ReasoningEffort.Off),
            CancellationToken.None);

        JsonObject body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        body.Select(kvp => kvp.Key).Should().OnlyContain(k => _allowedBodyKeys.Contains(k));
        body["model"]!.GetValue<string>().Should().Be("google/gemini-2.5-flash-lite");
        body["max_tokens"]!.GetValue<int>().Should().Be(96);
        body["stream"]!.GetValue<bool>().Should().BeFalse();
        body["reasoning"]!["enabled"]!.GetValue<bool>().Should().BeFalse();
        JsonArray messages = body["messages"]!.AsArray();
        messages[0]!["role"]!.GetValue<string>().Should().Be("system");
        messages[1]!["role"]!.GetValue<string>().Should().Be("user");
        // Gemini family strips the maxLength constraint from the carried schema.
        JsonObject schema = body["response_format"]!["json_schema"]!["schema"]!.AsObject();
        schema["properties"]!["phrase_ru"]!.AsObject().ContainsKey("maxLength").Should().BeFalse();
    }

    [Fact]
    public async Task Low_reasoning_route_emits_effort_low()
    {
        var handler = MockHttpMessageHandler.Json(HttpStatusCode.OK, OpenAiSuccess("{}"));
        OpenRouterProvider provider = Provider(handler);

        await provider.CompleteAsync(
            Request(RealTimeSchema),
            Route("openrouter-anthropic", "anthropic/claude-sonnet-4.6", reasoning: ReasoningEffort.Low),
            CancellationToken.None);

        JsonObject body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        body["reasoning"]!["effort"]!.GetValue<string>().Should().Be("low");
    }

    [Fact]
    public async Task Anthropic_route_uses_forced_tool_and_reads_tool_arguments()
    {
        var handler = MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            AnthropicToolSuccess("{\"top_priority\":\"Тормози раньше\"}"));
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema, "coach_debrief"),
            Route("openrouter-anthropic", "anthropic/claude-sonnet-4.6", maxTokens: 2000),
            CancellationToken.None);

        JsonObject body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        body.Should().NotContainKey("response_format");
        body["tool_choice"]!["name"]!.GetValue<string>().Should().Be("coach_debrief");
        LlmResult.Success success = result.Should().BeOfType<LlmResult.Success>().Subject;
        success.Json.Should().Contain("top_priority");
    }

    [Fact]
    public async Task Debrief_route_does_not_truncate_at_2000_tokens()
    {
        // A representative Low-effort debrief response returns a non-truncation finish reason under
        // MaxOutputTokens=2000 (the headroom the plan sizes for adaptive thinking + 200-word output).
        var handler = MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            AnthropicToolSuccess("{\"top_priority\":\"x\"}", finish: "tool_calls"));
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema, "coach_debrief"),
            Route("openrouter-anthropic", "anthropic/claude-sonnet-4.6", maxTokens: 2000, reasoning: ReasoningEffort.Low),
            CancellationToken.None);

        LlmResult.Success success = result.Should().BeOfType<LlmResult.Success>().Subject;
        success.Info.FinishReason.Should().NotBe("max_tokens");
    }

    [Fact]
    public async Task RateLimited_maps_429_with_retry_after()
    {
        var handler = MockHttpMessageHandler.Json(
            HttpStatusCode.TooManyRequests, "{\"error\":\"slow down\"}", retryAfterSeconds: "30");
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema), Route("openrouter-google", "google/gemini-2.5-flash-lite"), CancellationToken.None);

        LlmResult.Failure failure = result.Should().BeOfType<LlmResult.Failure>().Subject;
        LlmFailure.RateLimited rateLimited = failure.Error.Should().BeOfType<LlmFailure.RateLimited>().Subject;
        rateLimited.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ServerError_maps_503_with_status_code()
    {
        var handler = MockHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "down");
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema), Route("openrouter-google", "google/gemini-2.5-flash-lite"), CancellationToken.None);

        LlmResult.Failure failure = result.Should().BeOfType<LlmResult.Failure>().Subject;
        failure.Error.Should().BeOfType<LlmFailure.ServerError>().Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Auth_maps_401()
    {
        var handler = MockHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "no key");
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema), Route("openrouter-google", "google/gemini-2.5-flash-lite"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.Auth>();
    }

    [Fact]
    public async Task Transport_maps_network_exception()
    {
        var handler = MockHttpMessageHandler.Throws(new HttpRequestException("connection reset"));
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema), Route("openrouter-google", "google/gemini-2.5-flash-lite"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.Transport>();
    }

    [Fact]
    public async Task Timeout_maps_when_route_budget_exceeded()
    {
        OpenRouterProvider provider = Provider(new HangingHandler());

        LlmResult result = await provider.CompleteAsync(
            Request(RealTimeSchema),
            Route("openrouter-google", "google/gemini-2.5-flash-lite", timeoutMs: 50),
            CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.Timeout>();
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_a_timeout()
    {
        OpenRouterProvider provider = Provider(new HangingHandler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.CompleteAsync(
            Request(RealTimeSchema),
            Route("openrouter-google", "google/gemini-2.5-flash-lite"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static OpenRouterProvider Provider(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.test/api/v1/") },
            new SchemaTranslatorSelector(),
            TimeProvider.System);

    private static LlmRequest Request(string schema, string schemaName = "coach_tip")
        => new("corner", "Ты гоночный инженер.", "Gold JSON здесь", schema, schemaName);

    private static ResolvedRoute Route(
        string providerId,
        string modelId,
        int maxTokens = 96,
        double timeoutMs = 2000,
        ReasoningEffort reasoning = ReasoningEffort.Off)
        => new(providerId, modelId, maxTokens, TimeSpan.FromMilliseconds(timeoutMs), reasoning, false);

    private static string OpenAiSuccess(string contentJson, string finish = "stop")
        => new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["content"] = contentJson },
                ["finish_reason"] = finish,
            }),
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = 120,
                ["completion_tokens"] = 18,
                ["prompt_tokens_details"] = new JsonObject { ["cached_tokens"] = 40 },
                ["completion_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 5 },
            },
        }.ToJsonString();

    private static string AnthropicToolSuccess(string argsJson, string finish = "tool_calls")
        => new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["tool_calls"] = new JsonArray(new JsonObject
                    {
                        ["function"] = new JsonObject
                        {
                            ["name"] = "coach_debrief",
                            ["arguments"] = argsJson,
                        },
                    }),
                },
                ["finish_reason"] = finish,
            }),
        }.ToJsonString();

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
