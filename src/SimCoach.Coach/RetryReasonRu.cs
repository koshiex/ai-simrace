namespace SimCoach.Coach;

/// <summary>
/// Terse EN→RU rendering of the closed <see cref="TipValidator"/> refusal-reason set, appended to the retry
/// system prompt as a single <c>"Причина отказа: &lt;reason&gt;"</c> line (M28) so the model sees *why* its prior
/// answer was rejected. The validator emits English identifiers (some carry a dynamic numeric suffix); this type
/// only maps each closed reason's fixed prefix to a stable key. The RU text for that key lives in the versioned
/// embedded <c>coach.retry-reason.*.ru.txt</c> resource (loaded via <see cref="PromptResources.ReadRetryReasons"/>),
/// mirroring the sibling prompt fragments, and is passed in by the caller. An unrecognised reason (or a key absent
/// from the resource) echoes verbatim — safe, since the retry still demands strict schema compliance regardless.
/// </summary>
internal static class RetryReasonRu
{
    private const string Prefix = "Причина отказа: ";

    public static string Line(IReadOnlyDictionary<string, string> reasons, string reason)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(reason);

        string? key = KeyFor(reason);
        return Prefix + (key is not null && reasons.TryGetValue(key, out string? ru) ? ru : reason);
    }

    private static string? KeyFor(string reason)
    {
        if (reason.StartsWith("missing action_id/phrase_ru", StringComparison.Ordinal))
        {
            return "missing_action_fields";
        }

        if (reason.StartsWith("action_id '", StringComparison.Ordinal))
        {
            return "action_not_allowed";
        }

        if (reason.StartsWith("empty phrase_ru", StringComparison.Ordinal))
        {
            return "empty_phrase_ru";
        }

        if (reason.StartsWith("phrase_ru exceeds", StringComparison.Ordinal))
        {
            return "phrase_ru_too_long";
        }

        if (reason.StartsWith("missing top_losses/top_priority", StringComparison.Ordinal))
        {
            return "missing_debrief_fields";
        }

        if (reason.StartsWith("top_losses exceeds", StringComparison.Ordinal))
        {
            return "top_losses_too_many";
        }

        if (reason.StartsWith("empty top_priority", StringComparison.Ordinal))
        {
            return "empty_top_priority";
        }

        if (reason.StartsWith("debrief exceeds", StringComparison.Ordinal))
        {
            return "debrief_too_long";
        }

        if (reason.StartsWith("malformed json", StringComparison.Ordinal))
        {
            return "malformed_json";
        }

        if (reason.StartsWith("SchemaViolation", StringComparison.Ordinal))
        {
            return "schema_violation";
        }

        return null;
    }
}
