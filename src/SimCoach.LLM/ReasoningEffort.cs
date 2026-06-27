namespace SimCoach.LLM;

/// <summary>Provider-neutral reasoning knob (route config). Real-time routes use <see cref="Off"/>; the
/// debrief route uses <see cref="Low"/>.</summary>
public enum ReasoningEffort
{
    Off,
    Low,
}
