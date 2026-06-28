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
            double length = Mod1(corner.EndPosition - corner.StartPosition);
            if (length <= 0)
            {
                continue; // degenerate window
            }

            double posOffset = Mod1(position - corner.StartPosition);
            if (posOffset > length)
            {
                continue; // not inside this corner's window
            }

            double apexOffset = Mod1(corner.ApexPosition - corner.StartPosition);
            double apexStart = apexOffset * (1.0 - _apexBandFraction);
            double apexEnd = apexOffset + ((length - apexOffset) * _apexBandFraction);

            if (posOffset >= apexStart && posOffset <= apexEnd)
            {
                return GateCornerPhase.Apex;
            }

            if (posOffset < apexStart)
            {
                // Entry zone: the first half is the braking phase, the rest is turn-in.
                return posOffset < apexStart / 2.0 ? GateCornerPhase.Braking : GateCornerPhase.Entry;
            }

            return GateCornerPhase.Exit;
        }

        return GateCornerPhase.None;
    }

    /// <summary>Forward distance in [0, 1) — folds a raw position delta into the lap's wrap-around.</summary>
    private static double Mod1(double value) => ((value % 1.0) + 1.0) % 1.0;
}
