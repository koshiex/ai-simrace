using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Tyre/brake-temp abuse over a lap: the peak tyre and brake temperatures (max across the
/// [FL, FR, RL, RR] arrays and over every frame) and whether each crosses its abuse band. Returns a
/// <see cref="ThermalResult"/> the Reference layer maps onto the proto; the kernel stays decoupled from
/// the event shape. Frames with empty temp arrays (ACC often reports none live) contribute nothing, so a
/// lap with no temperature data yields all-zero / false rather than throwing.
/// </summary>
public static class ThermalKernels
{
    /// <summary>Tyre core temperature above this (deg C) counts as overheating.</summary>
    private const float TyreOverheatC = 110f;

    /// <summary>Brake temperature above this (deg C) counts as overheating.</summary>
    private const float BrakeOverheatC = 700f;

    public static ThermalResult Analyze(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        float maxTyre = 0f;
        float maxBrake = 0f;
        foreach (TelemetryFrame frame in frames)
        {
            maxTyre = MathF.Max(maxTyre, Peak(frame.TyreTempC));
            maxBrake = MathF.Max(maxBrake, Peak(frame.BrakeTempC));
        }

        return new ThermalResult
        {
            MaxTyreTempC = maxTyre,
            MaxBrakeTempC = maxBrake,
            TyreOverheat = maxTyre > TyreOverheatC,
            BrakeOverheat = maxBrake > BrakeOverheatC,
        };
    }

    private static float Peak(IReadOnlyList<float> values)
    {
        float peak = 0f;
        foreach (float value in values)
        {
            if (value > peak)
            {
                peak = value;
            }
        }

        return peak;
    }
}
