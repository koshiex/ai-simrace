using System.Text.Json;

namespace SimCoach.RuEval;

/// <summary>
/// Parses the judge's strict-schema verdict JSON (System.Text.Json). Structural: all five score fields must be
/// present integers within <c>[0, maxScore]</c> and a non-empty justification string. A malformed or
/// out-of-range payload is rejected (returns false) rather than silently coerced — a broken verdict must not
/// masquerade as a passing score.
/// </summary>
public static class VerdictParser
{
    private static readonly string[] _scoreFields =
        ["groundedness", "brevity", "natural_russian", "actionability", "tone"];

    public static bool TryParse(string json, int maxScore, out JudgeVerdict? verdict, out string failure)
    {
        ArgumentNullException.ThrowIfNull(json);
        verdict = null;
        failure = string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            failure = $"malformed json: {ex.Message}";
            return false;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = "verdict root is not an object";
                return false;
            }

            int[] scores = new int[_scoreFields.Length];
            for (int i = 0; i < _scoreFields.Length; i++)
            {
                if (!TryGetScore(root, _scoreFields[i], maxScore, out scores[i], out failure))
                {
                    return false;
                }
            }

            if (!root.TryGetProperty("justification_ru", out JsonElement justEl)
                || justEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(justEl.GetString()))
            {
                failure = "missing justification_ru";
                return false;
            }

            verdict = new JudgeVerdict(scores[0], scores[1], scores[2], scores[3], scores[4], justEl.GetString()!);
            return true;
        }
    }

    private static bool TryGetScore(JsonElement root, string name, int maxScore, out int value, out string failure)
    {
        value = 0;
        failure = string.Empty;
        if (!root.TryGetProperty(name, out JsonElement el)
            || el.ValueKind != JsonValueKind.Number
            || !el.TryGetInt32(out value))
        {
            failure = $"missing or non-integer '{name}'";
            return false;
        }

        if (value < 0 || value > maxScore)
        {
            failure = $"'{name}' = {value} out of range [0, {maxScore}]";
            return false;
        }

        return true;
    }
}
