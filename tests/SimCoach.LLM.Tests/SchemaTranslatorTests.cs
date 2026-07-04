using System.Text.Json.Nodes;
using FluentAssertions;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class SchemaTranslatorTests
{
    private const string RealTimeSchema =
        """
        { "type": "object", "additionalProperties": false,
          "required": ["action_id", "phrase_ru"],
          "properties": {
            "action_id": { "type": "string", "enum": ["wider_entry", "brake_later_by_meters"] },
            "phrase_ru": { "type": "string", "minLength": 1, "maxLength": 80 } } }
        """;

    private const string DebriefSchema =
        """
        { "type": "object", "additionalProperties": false,
          "required": ["top_losses", "top_priority", "setup_hint"],
          "properties": {
            "top_losses": { "type": "array", "maxItems": 5, "items": {
              "type": "object", "additionalProperties": false, "required": ["corner", "ms", "why"],
              "properties": { "corner": {"type":"string"}, "ms": {"type":"integer","minimum":0},
                              "why": {"type":"string"} } } },
            "top_priority": { "type": "string" },
            "setup_hint": { "type": ["string", "null"] } } }
        """;

    [Fact]
    public void Strict_wraps_schema_verbatim_in_json_schema_envelope()
    {
        SchemaDirective directive = new StrictJsonSchemaTranslator().Translate(RealTimeSchema, "coach_tip");

        directive.Tools.Should().BeNull();
        directive.SystemInstruction.Should().BeNull();
        JsonObject format = directive.ResponseFormat!;
        format["type"]!.GetValue<string>().Should().Be("json_schema");
        JsonObject jsonSchema = format["json_schema"]!.AsObject();
        jsonSchema["name"]!.GetValue<string>().Should().Be("coach_tip");
        jsonSchema["strict"]!.GetValue<bool>().Should().BeTrue();
        // The inner schema is preserved verbatim (constraints intact for the strict family).
        jsonSchema["schema"]!["properties"]!["phrase_ru"]!["maxLength"]!.GetValue<int>().Should().Be(80);
    }

    [Fact]
    public void Gemini_strips_banned_constraint_keywords_recursively()
    {
        SchemaDirective directive = new GeminiSchemaTranslator().Translate(DebriefSchema, "coach_debrief");

        JsonObject schema = directive.ResponseFormat!["json_schema"]!["schema"]!.AsObject();
        JsonObject props = schema["properties"]!.AsObject();
        // Top-level array constraint stripped.
        props["top_losses"]!.AsObject().ContainsKey("maxItems").Should().BeFalse();
        // Nested item constraint stripped too.
        JsonObject itemProps = props["top_losses"]!["items"]!["properties"]!.AsObject();
        itemProps["ms"]!.AsObject().ContainsKey("minimum").Should().BeFalse();
        // Untouched keys survive.
        itemProps["ms"]!["type"]!.GetValue<string>().Should().Be("integer");
    }

    [Fact]
    public void Gemini_strips_maxItems_which_leaves_the_debrief_bound_to_the_post_parse_cap()
    {
        // M28 rationale pin: maxItems is intentionally in _bannedKeywords, so the debrief's top_losses array
        // bound cannot ride on the wire — it survives only as the TipValidator cap. CoachStartupValidator
        // hard-fails a Gemini debrief route for exactly this reason.
        const string arraySchema =
            """{ "type": "object", "properties": { "xs": { "type": "array", "maxItems": 3, "minItems": 1 } } }""";

        SchemaDirective directive = new GeminiSchemaTranslator().Translate(arraySchema, "x");

        JsonObject xs = directive.ResponseFormat!["json_schema"]!["schema"]!["properties"]!["xs"]!.AsObject();
        xs.ContainsKey("maxItems").Should().BeFalse();
        xs.ContainsKey("minItems").Should().BeFalse();
        xs["type"]!.GetValue<string>().Should().Be("array");
    }

    [Fact]
    public void Gemini_rewrites_string_null_union_to_nullable()
    {
        SchemaDirective directive = new GeminiSchemaTranslator().Translate(DebriefSchema, "coach_debrief");

        JsonObject hint = directive.ResponseFormat!["json_schema"]!["schema"]!["properties"]!["setup_hint"]!
            .AsObject();
        hint["type"]!.GetValue<string>().Should().Be("string");
        hint["nullable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Gemini_preserves_required_and_property_keys()
    {
        SchemaDirective directive = new GeminiSchemaTranslator().Translate(RealTimeSchema, "coach_tip");

        JsonObject schema = directive.ResponseFormat!["json_schema"]!["schema"]!.AsObject();
        schema["required"]!.AsArray().Should().HaveCount(2);
        schema["properties"]!.AsObject().Should().ContainKeys("action_id", "phrase_ru");
        // Only the banned keyword went, not the property.
        schema["properties"]!["phrase_ru"]!.AsObject().ContainsKey("maxLength").Should().BeFalse();
        schema["properties"]!["phrase_ru"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void Gemini_preserves_a_two_member_confidence_enum()
    {
        // M31: the confidence field is a 2-member string enum (like action_id) — verify it survives Gemini's
        // constraint strip intact so the wire schema can carry it across families.
        const string confidenceSchema =
            """
            { "type": "object", "additionalProperties": false,
              "required": ["action_id", "phrase_ru", "confidence"],
              "properties": {
                "action_id": { "type": "string", "enum": ["wider_entry"] },
                "phrase_ru": { "type": "string" },
                "confidence": { "type": "string", "enum": ["high", "low"] } } }
            """;

        SchemaDirective directive = new GeminiSchemaTranslator().Translate(confidenceSchema, "coach_tip");

        JsonObject schema = directive.ResponseFormat!["json_schema"]!["schema"]!.AsObject();
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().BeEquivalentTo("action_id", "phrase_ru", "confidence");
        JsonObject confidence = schema["properties"]!["confidence"]!.AsObject();
        confidence["type"]!.GetValue<string>().Should().Be("string");
        confidence["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("high", "low");
    }

    [Fact]
    public void Gemini_keeps_a_property_named_like_a_banned_keyword()
    {
        const string schema =
            """
            { "type": "object", "properties": {
                "minimum": { "type": "number", "maximum": 5 },
                "phrase": { "type": "string" } } }
            """;

        SchemaDirective directive = new GeminiSchemaTranslator().Translate(schema, "x");

        JsonObject props = directive.ResponseFormat!["json_schema"]!["schema"]!["properties"]!.AsObject();
        props.Should().ContainKey("minimum");                                     // property NAME survives
        props["minimum"]!["type"]!.GetValue<string>().Should().Be("number");
        props["minimum"]!.AsObject().ContainsKey("maximum").Should().BeFalse();    // keyword INSIDE it stripped
    }

    [Fact]
    public void Anthropic_emits_forced_tool_shape()
    {
        SchemaDirective directive = new AnthropicToolSchemaTranslator().Translate(DebriefSchema, "coach_debrief");

        directive.ResponseFormat.Should().BeNull();
        // OpenAI-style function tool — the shape OpenRouter's compat endpoint accepts for anthropic/* (a native
        // {name, input_schema} / tool_choice{type:tool} body is rejected with HTTP 400).
        JsonObject tool = directive.Tools!.Single()!.AsObject();
        tool["type"]!.GetValue<string>().Should().Be("function");
        JsonObject fn = tool["function"]!.AsObject();
        fn["name"]!.GetValue<string>().Should().Be("coach_debrief");
        fn["parameters"]!["properties"]!.AsObject().Should().ContainKey("top_losses");
        directive.ToolChoice!["type"]!.GetValue<string>().Should().Be("function");
        directive.ToolChoice!["function"]!["name"]!.GetValue<string>().Should().Be("coach_debrief");
    }

    [Fact]
    public void JsonObject_injects_schema_into_system_instruction()
    {
        SchemaDirective directive = new JsonObjectSchemaTranslator().Translate(RealTimeSchema, "coach_tip");

        directive.ResponseFormat!["type"]!.GetValue<string>().Should().Be("json_object");
        directive.Tools.Should().BeNull();
        directive.SystemInstruction.Should().Contain("coach_tip").And.Contain("action_id");
    }

    // 'expected' is the SchemaFamily name as a string: the enum is internal, so a public [Theory] signature
    // cannot take it directly (CS0051). The assertion compares enum names.
    [Theory]
    [InlineData("anthropic/claude-sonnet-4.6", "AnthropicTool")]
    [InlineData("google/gemini-2.5-flash-lite", "Gemini")]
    [InlineData("openai/gpt-4o-mini", "OpenAiStrict")]
    [InlineData("deepseek/deepseek-chat", "OpenAiStrict")]
    public void Selector_infers_family_from_model_slug(string modelId, string expected)
    {
        SchemaTranslatorSelector.FamilyOf(modelId).ToString().Should().Be(expected);
        new SchemaTranslatorSelector().For(modelId).Family.ToString().Should().Be(expected);
    }
}
