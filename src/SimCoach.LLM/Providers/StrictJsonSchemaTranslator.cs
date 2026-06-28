using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>OpenAI-family strict <c>json_schema</c> response format. The schema rides verbatim.</summary>
internal sealed class StrictJsonSchemaTranslator : ISchemaTranslator
{
    public SchemaFamily Family => SchemaFamily.OpenAiStrict;

    public SchemaDirective Translate(string jsonSchema, string schemaName)
        => new(ResponseFormat: BuildStrictResponseFormat(ParseObject(jsonSchema), schemaName));

    internal static JsonObject BuildStrictResponseFormat(JsonObject schema, string schemaName)
        => new()
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = schemaName,
                ["strict"] = true,
                ["schema"] = schema,
            },
        };

    internal static JsonObject ParseObject(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonNode node = JsonNode.Parse(json)
            ?? throw new ArgumentException("Schema JSON parsed to null.", nameof(json));
        return node.AsObject();
    }
}
