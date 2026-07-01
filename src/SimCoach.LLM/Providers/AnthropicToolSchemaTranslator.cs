using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Anthropic forced-tool emulation: Anthropic does not honour OpenAI-style strict <c>json_schema</c>, so the
/// schema is presented as a single tool and the model is forced to call it. The request rides OpenRouter's
/// OpenAI-compatible <c>/chat/completions</c> endpoint, so the tool MUST use the OpenAI wire shape
/// (<c>{type:"function", function:{name, parameters}}</c> + <c>tool_choice:{type:"function", function:{name}}</c>),
/// NOT Anthropic's native <c>{name, input_schema}</c> / <c>tool_choice:{type:"tool"}</c> — the latter is rejected
/// with HTTP 400 by the compat endpoint. OpenRouter maps the function tool to Anthropic tool-use upstream and
/// returns OpenAI-style <c>tool_calls</c>, which <c>OpenRouterProvider.ExtractContent</c> reads back.
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
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = schemaName,
                    ["parameters"] = schema,
                },
            },
        };
        var toolChoice = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = schemaName },
        };
        return new SchemaDirective(Tools: tools, ToolChoice: toolChoice);
    }
}
