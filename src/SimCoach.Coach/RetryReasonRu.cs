namespace SimCoach.Coach;

/// <summary>
/// Terse EN→RU rendering of the closed <see cref="TipValidator"/> refusal-reason set, appended to the retry
/// system prompt as a single <c>"Причина отказа: &lt;reason&gt;"</c> line (M28) so the model sees *why* its prior
/// answer was rejected. The validator emits English identifiers (some carry a dynamic numeric suffix), so the
/// map keys on the fixed prefix of each closed reason; an unrecognised reason echoes verbatim (safe — the retry
/// still demands strict schema compliance regardless).
/// </summary>
internal static class RetryReasonRu
{
    private const string Prefix = "Причина отказа: ";

    public static string Line(string reason) => Prefix + Translate(reason);

    private static string Translate(string reason)
    {
        if (reason.StartsWith("missing action_id/phrase_ru", StringComparison.Ordinal))
        {
            return "нет обязательных полей action_id или phrase_ru";
        }

        if (reason.StartsWith("action_id '", StringComparison.Ordinal))
        {
            return "action_id не из разрешённого списка";
        }

        if (reason.StartsWith("empty phrase_ru", StringComparison.Ordinal))
        {
            return "пустое поле phrase_ru";
        }

        if (reason.StartsWith("phrase_ru exceeds", StringComparison.Ordinal))
        {
            return "фраза длиннее лимита слов";
        }

        if (reason.StartsWith("missing top_losses/top_priority", StringComparison.Ordinal))
        {
            return "нет обязательных полей top_losses или top_priority";
        }

        if (reason.StartsWith("top_losses exceeds", StringComparison.Ordinal))
        {
            return "слишком много элементов в top_losses";
        }

        if (reason.StartsWith("empty top_priority", StringComparison.Ordinal))
        {
            return "пустое поле top_priority";
        }

        if (reason.StartsWith("debrief exceeds", StringComparison.Ordinal))
        {
            return "разбор длиннее лимита слов";
        }

        if (reason.StartsWith("malformed json", StringComparison.Ordinal))
        {
            return "ответ не является валидным JSON";
        }

        if (reason.StartsWith("SchemaViolation", StringComparison.Ordinal))
        {
            return "ответ не соответствует схеме";
        }

        return reason;
    }
}
