using System.Text.Json;
using SimCoach.Coach.Schema;

namespace SimCoach.Coach;

/// <summary>
/// Cadence-aware post-parse validation of an LLM response — the non-empty / subset-membership / word-limit
/// checks the wire schema cannot carry (Gemini's <c>responseSchema</c> rejects length and enum-beyond-subset
/// constraints, so they are enforced here in C#). Pure; <c>CoachService</c> consumes the verdict to decide
/// emit vs retry vs template. Limits are passed in from <see cref="CoachOptions"/> — no magic numbers.
/// </summary>
public static class TipValidator
{
    /// <summary>
    /// Validates a real-time tip (corner/sector/lap): <c>action_id ∈ subset</c>, non-empty phrase ≤ words.
    /// Three-way (M7): a sanctioned <see cref="OutputSchema.AbstainActionId"/> — <c>action_id="none"</c> when
    /// <paramref name="allowAbstain"/> is set — reports <see cref="RealtimeTipVerdict.Abstain"/> (silence) and
    /// the phrase is ignored even when non-empty. When abstain was not offered, <c>"none"</c> is not in the
    /// subset and falls through to an ordinary <see cref="RealtimeTipVerdict.Reject"/> (→ template).
    /// </summary>
    public static RealtimeTipVerdict TryValidateRealtime(
        string json,
        IReadOnlyCollection<string> subsetIds,
        int maxWords,
        bool allowAbstain,
        out string actionId,
        out string phraseRu,
        out string failure,
        out CoachConfidence confidence)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(subsetIds);
        actionId = string.Empty;
        phraseRu = string.Empty;
        failure = string.Empty;
        // Observe-only (M31): confidence never affects the verdict. Default High so a missing/unrecognised
        // value (and every non-Success path) keeps template/FakeProvider tips out of the low bucket.
        confidence = CoachConfidence.High;

        if (!TryParse(json, out JsonDocument? doc, out failure))
        {
            return RealtimeTipVerdict.Reject;
        }

        using (doc)
        {
            JsonElement root = doc!.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "action_id", out string id)
                || !TryGetString(root, "phrase_ru", out string phrase))
            {
                failure = "missing action_id/phrase_ru";
                return RealtimeTipVerdict.Reject;
            }

            confidence = ParseConfidence(root);

            if (allowAbstain && string.Equals(id, OutputSchema.AbstainActionId, StringComparison.Ordinal))
            {
                return RealtimeTipVerdict.Abstain;
            }

            if (!subsetIds.Contains(id))
            {
                failure = $"action_id '{id}' not in subset";
                return RealtimeTipVerdict.Reject;
            }

            if (string.IsNullOrWhiteSpace(phrase))
            {
                failure = "empty phrase_ru";
                return RealtimeTipVerdict.Reject;
            }

            if (PhraseWordCount.Count(phrase) > maxWords)
            {
                failure = $"phrase_ru exceeds {maxWords} words";
                return RealtimeTipVerdict.Reject;
            }

            actionId = id;
            phraseRu = phrase;
            return RealtimeTipVerdict.Accept;
        }
    }

    /// <summary>Validates a debrief (session cadence): bounded <c>top_losses</c>, non-empty priority, ≤ aggregate words.</summary>
    public static bool TryValidateDebrief(
        string json,
        int maxLosses,
        int maxWords,
        out string topPriority,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(json);
        topPriority = string.Empty;
        failure = string.Empty;

        if (!TryParse(json, out JsonDocument? doc, out failure))
        {
            return false;
        }

        using (doc)
        {
            JsonElement root = doc!.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("top_losses", out JsonElement lossesEl)
                || lossesEl.ValueKind != JsonValueKind.Array
                || !TryGetString(root, "top_priority", out string priority))
            {
                failure = "missing top_losses/top_priority";
                return false;
            }

            if (lossesEl.GetArrayLength() > maxLosses)
            {
                failure = $"top_losses exceeds {maxLosses} items";
                return false;
            }

            if (string.IsNullOrWhiteSpace(priority))
            {
                failure = "empty top_priority";
                return false;
            }

            int words = PhraseWordCount.Count(priority);
            foreach (JsonElement loss in lossesEl.EnumerateArray())
            {
                if (loss.ValueKind == JsonValueKind.Object && TryGetString(loss, "why", out string why))
                {
                    words += PhraseWordCount.Count(why);
                }
            }

            if (TryGetString(root, "setup_hint", out string hint))
            {
                words += PhraseWordCount.Count(hint);
            }

            if (words > maxWords)
            {
                failure = $"debrief exceeds {maxWords} words";
                return false;
            }

            topPriority = priority;
            return true;
        }
    }

    // Tolerant M31 parse: the closed {high, low} band, case-insensitive. Missing or anything unrecognised →
    // High (the default band). Never throws, never influences Accept/Reject/Abstain.
    private static CoachConfidence ParseConfidence(JsonElement root) =>
        TryGetString(root, "confidence", out string raw)
        && string.Equals(raw, OutputSchema.ConfidenceLow, StringComparison.OrdinalIgnoreCase)
            ? CoachConfidence.Low
            : CoachConfidence.High;

    private static bool TryParse(string json, out JsonDocument? doc, out string failure)
    {
        failure = string.Empty;
        try
        {
            doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException ex)
        {
            doc = null;
            failure = $"malformed json: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
