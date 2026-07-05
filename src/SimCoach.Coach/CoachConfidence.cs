namespace SimCoach.Coach;

/// <summary>
/// The real-time tip's bounded self-report of how confident the model is in the <c>action_id</c> it chose (M31).
/// <see cref="High"/> = the Gold numbers clearly support the pick; <see cref="Low"/> = the evidence is ambiguous
/// or weak. Observe-only: it never changes emit/silence/severity/cost — it is parsed tolerantly and logged for
/// calibration. A missing or unrecognised wire value defaults to <see cref="High"/>, so template/FakeProvider
/// tips (which never emit it) stay out of the low bucket.
/// </summary>
public enum CoachConfidence
{
    High,
    Low,
}
