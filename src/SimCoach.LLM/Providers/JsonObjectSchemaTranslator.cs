using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Fallback for providers that only honour <c>response_format:{json_object}</c> and cannot carry a schema on
/// the wire. The schema is injected into the system prompt as a directive; the real guard remains Coach's
/// post-parse validation. Never selected by slug inference — reserved for an explicit config override.
/// </summary>
internal sealed class JsonObjectSchemaTranslator : ISchemaTranslator
{
    public SchemaFamily Family => SchemaFamily.JsonObject;

    public SchemaDirective Translate(string jsonSchema, string schemaName)
    {
        JsonObject schema = StrictJsonSchemaTranslator.ParseObject(jsonSchema);
        var responseFormat = new JsonObject { ["type"] = "json_object" };
        string instruction =
            $"Respond ONLY with JSON conforming to the schema named \"{schemaName}\": {schema.ToJsonString()}";
        return new SchemaDirective(ResponseFormat: responseFormat, SystemInstruction: instruction);
    }
}
