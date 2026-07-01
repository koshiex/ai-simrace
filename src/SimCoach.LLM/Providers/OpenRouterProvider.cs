using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Ring-2 adapter for the OpenRouter chat-completions API over a typed <see cref="HttpClient"/>. One adapter
/// fronts several upstream families; the output-schema dialect is chosen per resolved model id via
/// <see cref="ISchemaTranslatorSelector"/>, not by being "OpenRouter". The structured JSON is returned verbatim
/// in <see cref="LlmResult.Success.Json"/> — content validity (schema/word-count) is the Coach layer's
/// post-parse job, so this adapter never emits <see cref="LlmFailure.SchemaViolation"/>. Buffered only;
/// streaming is declared for P6.
/// </summary>
internal sealed class OpenRouterProvider : ILlmProvider
{
    private static readonly Uri _completionsPath = new("chat/completions", UriKind.Relative);

    private readonly HttpClient _http;
    private readonly ISchemaTranslatorSelector _selector;
    private readonly TimeProvider _timeProvider;

    public OpenRouterProvider(HttpClient http, ISchemaTranslatorSelector selector, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _http = http;
        _selector = selector;
        _timeProvider = timeProvider;
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonObject body = BuildBody(request, route);
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeoutCts = new CancellationTokenSource(route.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        long startedAt = _timeProvider.GetTimestamp();

        HttpResponseMessage response;
        string payload;
        try
        {
            response = await _http.PostAsync(_completionsPath, content, linked.Token);
            payload = await response.Content.ReadAsStringAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new LlmResult.Failure(new LlmFailure.Timeout(
                $"OpenRouter call exceeded the {(int)route.Timeout.TotalMilliseconds} ms route timeout."));
        }
        catch (HttpRequestException ex)
        {
            return new LlmResult.Failure(new LlmFailure.Transport(ex.Message));
        }

        using (response)
        {
            TimeSpan latency = _timeProvider.GetElapsedTime(startedAt);
            if (!response.IsSuccessStatusCode)
            {
                return new LlmResult.Failure(Classify(response, payload));
            }

            return Parse(payload, route, latency);
        }
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        => throw new NotSupportedException("OpenRouter streaming is declared for P6, not wired in Phase 3.");

    private JsonObject BuildBody(LlmRequest request, ResolvedRoute route)
    {
        ISchemaTranslator translator = _selector.For(route.ModelId);
        SchemaDirective directive = translator.Translate(request.JsonSchema, request.SchemaName);

        string system = directive.SystemInstruction is null
            ? request.SystemPrompt
            : $"{request.SystemPrompt}\n\n{directive.SystemInstruction}";

        var body = new JsonObject
        {
            ["model"] = route.ModelId,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }),
            ["max_tokens"] = route.MaxOutputTokens,
            ["stream"] = false,
            ["reasoning"] = BuildReasoning(route.Reasoning),
        };

        if (directive.ResponseFormat is not null)
        {
            body["response_format"] = directive.ResponseFormat;
        }

        if (directive.Tools is not null)
        {
            body["tools"] = directive.Tools;
            body["tool_choice"] = directive.ToolChoice;
        }

        return body;
    }

    private static JsonObject BuildReasoning(ReasoningEffort reasoning)
        => reasoning switch
        {
            ReasoningEffort.Low => new JsonObject { ["effort"] = "low" },
            _ => new JsonObject { ["enabled"] = false },
        };

    private static LlmFailure Classify(HttpResponseMessage response, string payload)
    {
        int status = (int)response.StatusCode;
        string message = string.IsNullOrWhiteSpace(payload)
            ? $"OpenRouter returned HTTP {status}."
            : $"OpenRouter returned HTTP {status}: {Truncate(payload)}";

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new LlmFailure.Auth(message),
            HttpStatusCode.TooManyRequests => new LlmFailure.RateLimited(message, ReadRetryAfter(response)),
            >= HttpStatusCode.InternalServerError => new LlmFailure.ServerError(message, status),
            _ => new LlmFailure.ServerError(message, status),
        };
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is TimeSpan delta)
        {
            return delta;
        }

        return retryAfter.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null;
    }

    private static LlmResult Parse(string payload, ResolvedRoute route, TimeSpan latency)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return new LlmResult.Failure(new LlmFailure.Transport($"Unparseable OpenRouter response: {ex.Message}"));
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return new LlmResult.Failure(new LlmFailure.Transport("OpenRouter response had no choices."));
            }

            JsonElement choice = choices[0];
            string? json = ExtractContent(choice);
            if (string.IsNullOrEmpty(json))
            {
                return new LlmResult.Failure(new LlmFailure.Transport("OpenRouter choice carried no content."));
            }

            string? finishReason = choice.TryGetProperty("finish_reason", out JsonElement fr)
                && fr.ValueKind == JsonValueKind.String
                    ? fr.GetString()
                    : null;

            LlmUsage usage = ReadUsage(root);
            var info = new LlmCallInfo(route.ProviderId, route.ModelId, latency, finishReason);
            return new LlmResult.Success(json, usage, info);
        }
    }

    private static string? ExtractContent(JsonElement choice)
    {
        if (!choice.TryGetProperty("message", out JsonElement message))
        {
            return null;
        }

        if (message.TryGetProperty("content", out JsonElement content)
            && content.ValueKind == JsonValueKind.String)
        {
            string? text = content.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        // Forced-tool (Anthropic family) returns the structured JSON as the tool call's arguments string.
        if (message.TryGetProperty("tool_calls", out JsonElement toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0
            && toolCalls[0].TryGetProperty("function", out JsonElement fn)
            && fn.TryGetProperty("arguments", out JsonElement args)
            && args.ValueKind == JsonValueKind.String)
        {
            return args.GetString();
        }

        return null;
    }

    private static LlmUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new LlmUsage(0, 0);
        }

        int input = ReadInt(usage, "prompt_tokens");
        int output = ReadInt(usage, "completion_tokens");
        int cached = usage.TryGetProperty("prompt_tokens_details", out JsonElement promptDetails)
            ? ReadInt(promptDetails, "cached_tokens")
            : 0;
        int reasoning = usage.TryGetProperty("completion_tokens_details", out JsonElement completionDetails)
            ? ReadInt(completionDetails, "reasoning_tokens")
            : 0;

        return new LlmUsage(input, output, cached, reasoning);
    }

    private static int ReadInt(JsonElement obj, string property)
        => obj.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static string Truncate(string text)
        => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
