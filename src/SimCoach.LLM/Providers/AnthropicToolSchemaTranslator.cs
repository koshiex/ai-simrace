using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Anthropic forced-tool emulation: Anthropic does not honour OpenAI-style strict <c>json_schema</c>, so the
/// schema is presented as a single tool's <c>input_schema</c> and the model is forced to call it. The exact
/// wire shape OpenRouter accepts for <c>anthropic/*</c> is confirmed by the per-model-family schema-acceptance
/// release gate (see the plan's R4) before the debrief default is pinned live.
/// </summary>
internal sealed class AnthropicToolSchemaTranslator : ISchemaTranslator
{
    public SchemaFamily Family => SchemaFamily.AnthropicTool;

    public SchemaDirective Translate(string jsonSchema, string schemaName)
    {
        JsonObject schema = StrictJsonSchemaTranslator.ParseObject(jsonSchema);
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = schemaName,
                ["input_schema"] = schema,
            },
        };
        var toolChoice = new JsonObject
        {
            ["type"] = "tool",
            ["name"] = schemaName,
        };
        return new SchemaDirective(Tools: tools, ToolChoice: toolChoice);
    }
}
