namespace SimCoach.Coach;

/// <summary>
/// The single RU gloss for a closed-set <c>reason</c> code (from <c>CornerEvent.reason</c> /
/// <c>AggregatedLoss.dominant_reason</c>) — the one taxonomy and one fallback shared by the deterministic
/// session debrief (<see cref="DebriefTemplate"/>) and the realtime <c>corner_catch_all</c> phrasing
/// (via <c>ParamTransform.ReasonRu</c>), so both resolve identical RU for the same key. Reuses the existing
/// <c>Reason_*</c> resx keys; an empty reason falls back to the neutral "lost time" gloss.
/// </summary>
internal static class ReasonGloss
{
    public static string ToRu(string reason) =>
        string.IsNullOrEmpty(reason) ? CoachStrings.Get("Reason_slower") : CoachStrings.Get("Reason_" + reason);
}
