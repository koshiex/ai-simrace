using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;

namespace SimCoach.Coach;

/// <summary>
/// The PR-G placeholder ambient state, used until the host wires the real source (sim-adapter car class +
/// reference lookup + gate-only <c>TelemetryFanOut</c> latest frame). It reports no live frame (so
/// frame-dependent gates fail open) and no reference (so tips are flagged no-PB-yet). Replaced wholesale in a
/// later PR — never registered in a shipping host as-is.
/// </summary>
public sealed class DefaultCoachAmbientState : ICoachAmbientState
{
    // A non-empty, never-baked TrackId so corner-name resolution falls back to positional phrasing instead of
    // tripping CornerNameMap's track-id guard; CarClass/WeatherBucket are verbatim passthrough (no lookup).
    private static readonly GoldSessionContext _unknownMetadata = new(
        TrackId: "unknown",
        CarClass: "unknown",
        WeatherBucket: "unknown",
        LapNumber: 0,
        HasReference: false);

    public GoldSessionContext SessionMetadata() => _unknownMetadata;

    public GateSnapshot LatestGate() => GateSnapshot.Unknown;
}
