using SimCoach.Pipeline.Kernels;
using SimCoach.Reference;

namespace SimCoach.Coach.Rules;

/// <summary>
/// Maps a normalized lap position (0..1) onto the live <see cref="GateCornerPhase"/> using the track's baked
/// corner windows (<see cref="Corner"/>: Start → Apex → End). Pure and wrap-around safe. The apex band width
/// is config (<see cref="RuleEngineOptions.ApexWindowFraction"/>) so the apex quiet-zone has a tunable,
/// magic-number-free source. A position outside every corner window is <see cref="GateCornerPhase.None"/>
/// (a straight); a track with no baked geometry has no corners, so everything resolves to None.
/// </summary>
public sealed class CornerPhaseResolver
{
    private readonly double _apexBandFraction;

    public CornerPhaseResolver(RuleEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _apexBandFraction = options.ApexWindowFraction;
    }

    public GateCornerPhase Resolve(double position, IReadOnlyList<Corner> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);

        foreach (Corner corner in corners)
        {
            // Shared apex-band math (SimCoach.Pipeline) — the SINGLE definition of "apex" the metric also
            // uses, so the live gate and the brake-overlap window can never disagree in code.
            CornerPhaseOffsets offsets = CornerPhaseBands.Offsets(
                corner.StartPosition, corner.ApexPosition, corner.EndPosition, _apexBandFraction);
            if (offsets.Length <= 0)
            {
                continue; // degenerate window
            }

            double posOffset = CornerPhaseBands.Mod1(position - corner.StartPosition);
            if (posOffset > offsets.Length)
            {
                continue; // not inside this corner's window
            }

            if (posOffset >= offsets.ApexStart && posOffset <= offsets.ApexEnd)
            {
                return GateCornerPhase.Apex;
            }

            if (posOffset < offsets.ApexStart)
            {
                // Entry zone: the first half is the braking phase, the rest is turn-in.
                return posOffset < offsets.TurnInStart ? GateCornerPhase.Braking : GateCornerPhase.Entry;
            }

            return GateCornerPhase.Exit;
        }

        return GateCornerPhase.None;
    }
}
