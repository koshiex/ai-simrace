namespace SimCoach.LLM.Providers;

/// <summary>
/// The output-schema dialect a model family accepts. Selected from the resolved model id (NOT the gateway
/// provider), because one <see cref="OpenRouterProvider"/> fronts several upstream families that disagree on
/// structured-output shape.
/// </summary>
internal enum SchemaFamily
{
    /// <summary>OpenAI-style <c>response_format:{json_schema,strict:true}</c>.</summary>
    OpenAiStrict,

    /// <summary>Same envelope as <see cref="OpenAiStrict"/> but the schema is constraint-stripped and the
    /// <c>["string","null"]</c> union is rewritten to <c>nullable:true</c> (Gemini's responseSchema).</summary>
    Gemini,

    /// <summary>Anthropic forced-tool emulation (no native OpenAI-strict <c>json_schema</c>).</summary>
    AnthropicTool,

    /// <summary>Providers that only honour <c>response_format:{json_object}</c>; the schema is injected into
    /// the system prompt instead.</summary>
    JsonObject,
}
