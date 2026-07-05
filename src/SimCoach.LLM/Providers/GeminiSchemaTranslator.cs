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
        // maxItems is intentionally stripped: Gemini's responseSchema rejects it, so the debrief's array bound
        // survives only as the post-parse TipValidator cap (CoachStartupValidator hard-fails a Gemini debrief
        // route for exactly this reason — see M28). Keep it here; the SchemaTranslatorTests pin the strip.
        "maxItems",
    };

    // Maps whose keys are NAMES (property/definition names), not schema keywords — never strip their keys; only
    // recurse into their value schemas. Otherwise a property literally named "minimum" would be deleted.
    private static readonly HashSet<string> _schemaNameMaps = new(StringComparer.Ordinal)
    {
        "properties",
        "patternProperties",
        "$defs",
        "definitions",
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
            if (_schemaNameMaps.Contains(pair.Key) && pair.Value is JsonObject schemaMap)
            {
                foreach (KeyValuePair<string, JsonNode?> entry in schemaMap)
                {
                    Recurse(entry.Value);
                }
            }
            else
            {
                Recurse(pair.Value);
            }
        }
    }

    // Handles only the OpenAI [X,"null"] nullability union the Coach output schemas emit. A multi-type union is
    // not produced by those schemas; this keeps the last non-null type and is not a general union collapser.
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
