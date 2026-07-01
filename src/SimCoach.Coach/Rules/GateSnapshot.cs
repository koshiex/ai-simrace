namespace SimCoach.Coach.Rules;

/// <summary>
/// The lock-free latest-frame snapshot the gate logic reads — a handful of scalars (workload, position,
/// corner phase, session flags). It is never assembled into a Gold artifact and never serialized to the LLM,
/// so "only Gold-tier JSON leaves the machine" holds. The real source (a gate-only <c>TelemetryFanOut</c>
/// subscription + sim session flags, M7) is wired in a later PR; until then <see cref="Unknown"/> is supplied
/// so frame-dependent gates fail OPEN — a <c>default</c> struct would read as position 0 / speed 0 and misfire.
/// </summary>
public readonly record struct GateSnapshot(
    double Brake,
    double Steer,
    double SteerRate,
    double SpeedKmh,
    bool OffTrack,
    bool Contact,
    double NormalizedCarPosition,
    GateCornerPhase CornerPhase,
    SessionFlag SessionState,
    bool HasFrame)
{
    /// <summary>The "no live frame yet" sentinel: <see cref="HasFrame"/> is false, so frame-dependent gates fail open.</summary>
    public static GateSnapshot Unknown { get; } =
        new(0, 0, 0, 0, false, false, 0, GateCornerPhase.None, SessionFlag.Green, HasFrame: false);
}
