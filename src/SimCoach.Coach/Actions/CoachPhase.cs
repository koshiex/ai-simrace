namespace SimCoach.Coach.Actions;

/// <summary>
/// The causal phase of a corner an action addresses. The declaration order is the urgency order
/// (<see cref="Brake"/> most urgent), so a root cause in braking outranks an exit-phase symptom when
/// <see cref="CoachPriority"/> compares lexicographically. Used only at registry-load time to build the
/// total order.
/// </summary>
public enum CoachPhase
{
    Brake,
    Entry,
    Apex,
    Exit,
}
