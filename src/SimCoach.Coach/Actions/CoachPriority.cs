namespace SimCoach.Coach.Actions;

/// <summary>
/// The total-order priority of a coaching action: a <see cref="CoachPhase"/> (the dominant key, so a
/// braking root cause outranks an exit symptom) then an integer <see cref="Rank"/> within the phase.
/// Lower compares as more urgent. The registry asserts every authored <c>(Phase, Rank)</c> is unique, so
/// the order is tie-free and <c>Take(N)</c> over the valid subset is deterministic — without any
/// flattened-int encoding or phase-weight multiplier.
/// </summary>
public readonly record struct CoachPriority(CoachPhase Phase, int Rank) : IComparable<CoachPriority>
{
    public int CompareTo(CoachPriority other)
    {
        int phase = Phase.CompareTo(other.Phase);
        return phase != 0 ? phase : Rank.CompareTo(other.Rank);
    }

    public static bool operator <(CoachPriority left, CoachPriority right) => left.CompareTo(right) < 0;

    public static bool operator >(CoachPriority left, CoachPriority right) => left.CompareTo(right) > 0;

    public static bool operator <=(CoachPriority left, CoachPriority right) => left.CompareTo(right) <= 0;

    public static bool operator >=(CoachPriority left, CoachPriority right) => left.CompareTo(right) >= 0;
}
