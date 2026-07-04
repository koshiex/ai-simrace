using System.Text.Json.Nodes;

namespace SimCoach.RuEval;

/// <summary>
/// The judge's tiny strict verdict schema (mirrors <c>OutputSchema</c>'s canonical shape: an object with
/// <c>additionalProperties:false</c> and <c>required == keys(properties)</c> for OpenAI-style strict mode). The
/// five score dimensions are integers; range is enforced post-parse in <see cref="VerdictParser"/> (numeric
/// constraints are unreliable across the provider schema translators, exactly as the real-time schema avoids
/// <c>maxLength</c>).
/// </summary>
public static class RuJudgeSchema
{
    /// <summary>OpenRouter <c>json_schema.name</c> / Anthropic tool name for the verdict.</summary>
    public const string SchemaName = "ru_eval_verdict";

    public static string Verdict()
    {
        static JsonObject Integer() => new() { ["type"] = "integer" };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "groundedness", "brevity", "natural_russian", "actionability", "tone", "justification_ru"),
            ["properties"] = new JsonObject
            {
                ["groundedness"] = Integer(),
                ["brevity"] = Integer(),
                ["natural_russian"] = Integer(),
                ["actionability"] = Integer(),
                ["tone"] = Integer(),
                ["justification_ru"] = new JsonObject { ["type"] = "string" },
            },
        };

        return schema.ToJsonString();
    }
}
