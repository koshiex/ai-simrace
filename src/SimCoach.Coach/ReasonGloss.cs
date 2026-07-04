namespace SimCoach.Coach;

/// <summary>
/// The single RU gloss for a closed-set <c>reason</c> code (from <c>CornerEvent.reason</c> /
/// <c>AggregatedLoss.dominant_reason</c>) — the one taxonomy and one fallback shared by the deterministic
/// session debrief (<see cref="DebriefTemplate"/>) and the realtime <c>corner_catch_all</c> phrasing
/// (via <c>ParamTransform.ReasonRu</c>), so both resolve identical RU for the same key. Reuses the existing
/// <c>Reason_*</c> resx keys; an empty <em>or unmapped</em> reason falls back to the neutral "lost time" gloss.
/// </summary>
internal static class ReasonGloss
{
    private const string NeutralReasonKey = "Reason_slower";

    public static string ToRu(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return CoachStrings.Get(NeutralReasonKey);
        }

        string key = "Reason_" + reason;
        string gloss = CoachStrings.Get(key);

        // CoachStrings.Get echoes the key back when the resource is missing; a raw reason identifier
        // must never reach voice/overlay, so an unresolved lookup degrades to the neutral gloss.
        return string.Equals(gloss, key, StringComparison.Ordinal) ? CoachStrings.Get(NeutralReasonKey) : gloss;
    }
}
