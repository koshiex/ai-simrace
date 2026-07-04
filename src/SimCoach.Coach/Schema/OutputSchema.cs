using System.Text.Json.Nodes;

namespace SimCoach.Coach.Schema;

/// <summary>
/// Compiles the per-request output JSON schema. The real-time schema's <c>action_id.enum</c> IS the valid
/// subset — the single biggest reliability lever, and the only shape <c>FakeProvider</c> reads. PR-E emits a
/// CANONICAL strict schema; the D5 <c>ISchemaTranslator</c> (a later PR) owns any provider-specific rewrite
/// (e.g. Gemini's <c>["string","null"]</c>→<c>nullable:true</c> and constraint strip). Both schemas keep
/// <c>required == keys(properties)</c> for OpenAI-style strict mode.
/// </summary>
public static class OutputSchema
{
    /// <summary>OpenRouter <c>json_schema.name</c> / Anthropic tool name for the real-time tip schema.</summary>
    public const string RealTimeSchemaName = "coach_tip";

    /// <summary>Schema name for the session-debrief schema.</summary>
    public const string DebriefSchemaName = "coach_debrief";

    /// <summary>
    /// The abstain sentinel <c>action_id</c> (M7). It is a member of the real-time <c>action_id</c> enum only
    /// when abstain is offered; strict mode then makes the wire schema itself the primary guard — the model
    /// cannot emit it otherwise. Never an action-registry id, so a leaked value never satisfies the subset.
    /// </summary>
    public const string AbstainActionId = "none";

    /// <summary>
    /// Real-time schema (corner/sector/lap). <paramref name="subsetIds"/> become the <c>action_id</c> enum.
    /// When <paramref name="allowAbstain"/> is set the <see cref="AbstainActionId"/> sentinel is appended once,
    /// giving the model a first-class right to stay silent on a weak catch-all (M7). No <c>maxLength</c>/
    /// <c>minLength</c>: those are unenforced value-constraints (the word limit is enforced post-parse) and
    /// Gemini's <c>responseSchema</c> rejects them outright.
    /// </summary>
    public static string RealTime(IReadOnlyList<string> subsetIds, bool allowAbstain)
    {
        var enumArray = new JsonArray();
        foreach (string id in subsetIds)
        {
            enumArray.Add(id);
        }

        if (allowAbstain)
        {
            enumArray.Add(AbstainActionId);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("action_id", "phrase_ru"),
            ["properties"] = new JsonObject
            {
                ["action_id"] = new JsonObject { ["type"] = "string", ["enum"] = enumArray },
                ["phrase_ru"] = new JsonObject { ["type"] = "string" },
            },
        };

        return schema.ToJsonString();
    }

    /// <summary>
    /// Session-debrief schema. <c>top_losses</c> is bounded by <paramref name="maxDebriefLosses"/> via
    /// <c>maxItems</c>; <c>setup_hint</c> is a <c>["string","null"]</c> union — load-bearing structure that
    /// keeps the field optional while satisfying strict <c>required == keys(properties)</c>.
    /// </summary>
    public static string Debrief(int maxDebriefLosses)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("top_losses", "top_priority", "setup_hint"),
            ["properties"] = new JsonObject
            {
                ["top_losses"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = maxDebriefLosses,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("corner", "ms", "why"),
                        ["properties"] = new JsonObject
                        {
                            ["corner"] = new JsonObject { ["type"] = "string" },
                            ["ms"] = new JsonObject { ["type"] = "integer" },
                            ["why"] = new JsonObject { ["type"] = "string" },
                        },
                    },
                },
                ["top_priority"] = new JsonObject { ["type"] = "string" },
                ["setup_hint"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            },
        };

        return schema.ToJsonString();
    }
}
