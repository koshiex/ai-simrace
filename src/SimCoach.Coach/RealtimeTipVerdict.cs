namespace SimCoach.Coach;

/// <summary>
/// The three-way outcome of validating a real-time LLM tip (M7). <see cref="Accept"/> yields an emittable
/// action + phrase; <see cref="Abstain"/> is a sanctioned <c>"none"</c> on a weak catch-all — silence, neither
/// retried nor templated; <see cref="Reject"/> is a quality/infra miss that falls back to the deterministic
/// template (and, off the corner cadence, may be retried once).
/// </summary>
public enum RealtimeTipVerdict
{
    Accept,
    Abstain,
    Reject,
}
