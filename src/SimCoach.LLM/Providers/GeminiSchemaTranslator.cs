using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Gemini-via-OpenRouter strict <c>json_schema</c>. Gemini's responseSchema rejects the JSON-Schema numeric/
/// length/array constraint keywords and the OpenAI <c>["string","null"]</c> nullability union, so the schema is
/// recursively constraint-stripped and the union is rewritten to <c>type:X + nullable:true</c> before it rides.
/// </summary>
internal sealed class GeminiSchemaTranslator : ISchemaTranslator
{
    private static readonly HashSet<string> _bannedKeywords = new(StringComparer.Ordinal)
    {
        "minimum",
        "maximum",
        "exclusiveMinimum",
        "exclusiveMaximum",
        "minLength",
        "maxLength",
        "minItems",
        "maxItems",
    };

    public SchemaFamily Family => SchemaFamily.Gemini;

    public SchemaDirective Translate(string jsonSchema, string schemaName)
    {
        JsonObject schema = StrictJsonSchemaTranslator.ParseObject(jsonSchema);
        Sanitize(schema);
        return new SchemaDirective(
            ResponseFormat: StrictJsonSchemaTranslator.BuildStrictResponseFormat(schema, schemaName));
    }

    private static void Sanitize(JsonObject obj)
    {
        var bannedHere = new List<string>();
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            if (_bannedKeywords.Contains(pair.Key))
            {
                bannedHere.Add(pair.Key);
            }
        }

        foreach (string key in bannedHere)
        {
            obj.Remove(key);
        }

        if (obj["type"] is JsonArray typeUnion)
        {
            RewriteNullableUnion(obj, typeUnion);
        }

        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            Recurse(pair.Value);
        }
    }

    private static void RewriteNullableUnion(JsonObject obj, JsonArray typeUnion)
    {
        string? primary = null;
        bool hasNull = false;
        foreach (JsonNode? item in typeUnion)
        {
            string? type = item?.GetValue<string>();
            if (type == "null")
            {
                hasNull = true;
            }
            else if (type is not null)
            {
                primary = type;
            }
        }

        if (hasNull && primary is not null)
        {
            obj["type"] = primary;
            obj["nullable"] = true;
        }
    }

    private static void Recurse(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject child:
                Sanitize(child);
                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Recurse(item);
                }

                break;
        }
    }
}
