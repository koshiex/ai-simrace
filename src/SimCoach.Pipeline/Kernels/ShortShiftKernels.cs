using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Short-shifting over a corner window — upshifting below the engine's power band. For each upshift (gear
/// increases between two consecutive frames, out of a driving gear) the rpm on the frame just BEFORE the
/// change is the rpm the driver chose to shift at; a short-shift is one taken well below the top of the
/// usable rev range. No telemetry channel carries the car's redline, so the reference is the PEAK
/// <c>rpm</c> observed anywhere in the window — the highest the engine is revved is the best self-derived
/// proxy for the power-band ceiling. The score is the LARGEST shift-rpm deficit below that peak,
/// normalized to 0..1 between an onset and a saturation fraction. Only genuine forward upshifts count:
/// downshifts (braking into the corner) and pull-aways out of neutral/reverse are ignored, since neither
/// is a short-shift. A window with no upshift, or with no meaningful rpm data, returns 0.
/// </summary>
public static class ShortShiftKernels
{
    /// <summary>Lowest driving gear an upshift can originate from — excludes neutral (0) and reverse (−1).</summary>
    private const int LowestDrivingGear = 1;

    /// <summary>Below this peak rpm the window carries no meaningful rev data (idle / missing channel) → 0.</summary>
    private const float MinPeakRpm = 1000f;

    /// <summary>Shifting within this fraction of peak rpm is normal, not short-shifting (maps to 0).</summary>
    private const float OnsetDeficitFraction = 0.15f;

    /// <summary>At or beyond this fraction below peak rpm the score saturates at 1.</summary>
    private const float SaturationDeficitFraction = 0.40f;

    public static float ShortShiftScore(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        float peakRpm = 0f;
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.Rpm > peakRpm)
            {
                peakRpm = frame.Rpm;
            }
        }

        if (peakRpm < MinPeakRpm)
        {
            return 0f;
        }

        float worstDeficit = 0f;
        for (int i = 1; i < frames.Count; i++)
        {
            TelemetryFrame previous = frames[i - 1];
            TelemetryFrame current = frames[i];

            // Only forward upshifts from a driving gear — a downshift or a pull-away out of N/R is not a short-shift.
            if (previous.Gear < LowestDrivingGear || current.Gear <= previous.Gear)
            {
                continue;
            }

            float deficit = (peakRpm - previous.Rpm) / peakRpm;
            if (deficit > worstDeficit)
            {
                worstDeficit = deficit;
            }
        }

        return Math.Clamp(
            (worstDeficit - OnsetDeficitFraction) / (SaturationDeficitFraction - OnsetDeficitFraction),
            0f,
            1f);
    }
}
