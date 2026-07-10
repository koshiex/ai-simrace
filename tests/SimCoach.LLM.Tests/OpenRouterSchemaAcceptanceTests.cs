using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

/// <summary>
/// Per-family structured-output acceptance matrix over the real <see cref="OpenRouterProvider"/> wire (no
/// network — <see cref="MockHttpMessageHandler"/>). One OpenRouter adapter fronts three upstream families that
/// disagree on structured-output shape, and the family is inferred from the resolved model slug — so a model
/// swap that changes the slug prefix silently changes the wire contract. This fixture pins each shipped slug to
/// its family AND to the request body it must produce: a pre-pin guard for the M42 debrief pin
/// (<c>anthropic/claude-sonnet-4.6</c> → Anthropic forced-tool) and its <c>debrief_fallback</c>, plus the
/// Gemini corner/sector routes.
/// </summary>
public sealed class OpenRouterSchemaAcceptanceTests
{
    private const string SchemaWithConstraint =
        """
        { "type": "object", "additionalProperties": false,
          "required": ["action_id", "phrase_ru"],
          "properties": {
            "action_id": { "type": "string", "enum": ["wider_entry", "brake_later_by_meters"] },
            "phrase_ru": { "type": "string", "maxLength": 80 } } }
        """;

    // Expected family passed as a string (the SchemaFamily enum is internal — a public [Theory] method may use
    // an internal type in its body but not its signature).
    [Theory]
    [InlineData("anthropic/claude-sonnet-4.6", "AnthropicTool")]   // M42 debrief pin
    [InlineData("anthropic/claude-haiku-4.5", "AnthropicTool")]    // debrief_fallback route
    [InlineData("google/gemini-3.1-flash-lite", "Gemini")]         // corner route
    [InlineData("google/gemini-2.5-flash-lite", "Gemini")]         // sector/lap/strategy routes
    [InlineData("openai/gpt-4o-mini", "OpenAiStrict")]             // default family (no slug prefix match)
    public void Shipped_slug_maps_to_expected_family(string modelId, string expectedFamily)
        => SchemaTranslatorSelector.FamilyOf(modelId).ToString().Should().Be(expectedFamily);

    [Theory]
    [InlineData("openai/gpt-4o-mini")]
    public async Task OpenAiStrict_family_carries_verbatim_strict_json_schema(string modelId)
    {
        JsonObject body = await CapturedBody(
            modelId, OpenAiSuccess("{\"action_id\":\"wider_entry\",\"phrase_ru\":\"Шире.\"}"));

        body.Should().NotContainKey("tools");
        body["response_format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>().Should().BeTrue();
        // OpenAI-strict rides the schema verbatim — the maxLength constraint survives (unlike Gemini, which strips it).
        PhraseSchema(body).ContainsKey("maxLength").Should().BeTrue();
    }

    [Theory]
    [InlineData("google/gemini-3.1-flash-lite")]
    [InlineData("google/gemini-2.5-flash-lite")]
    public async Task Gemini_family_strips_unsupported_constraints(string modelId)
    {
        JsonObject body = await CapturedBody(
            modelId, OpenAiSuccess("{\"action_id\":\"wider_entry\",\"phrase_ru\":\"Шире.\"}"));

        body.Should().NotContainKey("tools");
        body["response_format"]!["json_schema"]!["schema"].Should().NotBeNull();
        PhraseSchema(body).ContainsKey("maxLength").Should().BeFalse();
    }

    [Theory]
    [InlineData("anthropic/claude-sonnet-4.6")]
    [InlineData("anthropic/claude-haiku-4.5")]
    public async Task Anthropic_family_uses_forced_tool_not_response_format(string modelId)
    {
        var handler = MockHttpMessageHandler.Json(
            HttpStatusCode.OK, AnthropicToolSuccess("{\"action_id\":\"wider_entry\",\"phrase_ru\":\"Шире.\"}"));
        OpenRouterProvider provider = Provider(handler);

        LlmResult result = await provider.CompleteAsync(Request(), Route(modelId), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>();
        JsonObject body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        body.Should().NotContainKey("response_format");
        // OpenRouter's compat endpoint takes an OpenAI-style forced function tool, named after the schema.
        body["tools"]![0]!["function"]!["name"]!.GetValue<string>().Should().Be("coach_tip");
        body["tool_choice"]!["function"]!["name"]!.GetValue<string>().Should().Be("coach_tip");
    }

    private static async Task<JsonObject> CapturedBody(string modelId, string successPayload)
    {
        var handler = MockHttpMessageHandler.Json(HttpStatusCode.OK, successPayload);
        OpenRouterProvider provider = Provider(handler);
        LlmResult result = await provider.CompleteAsync(Request(), Route(modelId), CancellationToken.None);
        result.Should().BeOfType<LlmResult.Success>();
        return JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
    }

    private static JsonObject PhraseSchema(JsonObject body)
        => body["response_format"]!["json_schema"]!["schema"]!["properties"]!["phrase_ru"]!.AsObject();

    private static OpenRouterProvider Provider(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.test/api/v1/") },
            new SchemaTranslatorSelector(),
            TimeProvider.System);

    private static LlmRequest Request()
        => new("corner", "Ты гоночный инженер.", "Gold JSON здесь", SchemaWithConstraint, "coach_tip");

    private static ResolvedRoute Route(string modelId)
        => new("openrouter", modelId, 128, TimeSpan.FromSeconds(2), ReasoningEffort.Off, false);

    private static string OpenAiSuccess(string contentJson)
        => new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["content"] = contentJson },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 100, ["completion_tokens"] = 12 },
        }.ToJsonString();

    private static string AnthropicToolSuccess(string argsJson)
        => new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["tool_calls"] = new JsonArray(new JsonObject
                    {
                        ["function"] = new JsonObject { ["name"] = "coach_tip", ["arguments"] = argsJson },
                    }),
                },
                ["finish_reason"] = "tool_calls",
            }),
        }.ToJsonString();
}
