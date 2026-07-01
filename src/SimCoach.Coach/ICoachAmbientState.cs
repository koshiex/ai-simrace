using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;

namespace SimCoach.Coach;

/// <summary>
/// The ambient per-session state <c>CoachService</c> needs but the corner/sector/lap domain events do not
/// carry: the session metadata for <see cref="GoldSessionContext"/> (track / car-class / weather /
/// has-reference) and the latest gate snapshot. PR-G ships <see cref="DefaultCoachAmbientState"/>; a later PR
/// backs it with the sim-adapter car class + the reference lookup + a gate-only <c>TelemetryFanOut</c>
/// latest-frame subscription. (Lap number is tracked by <c>CoachService</c> off the event stream, not here.)
/// </summary>
public interface ICoachAmbientState
{
    GoldSessionContext SessionMetadata();

    GateSnapshot LatestGate();
}
