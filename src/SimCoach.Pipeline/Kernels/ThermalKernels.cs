using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Tyre/brake-temp abuse over a lap: the peak tyre and brake temperatures (max across the
/// [FL, FR, RL, RR] arrays and over every frame) and whether each was <b>sustained</b> above its abuse
/// band. Returns a <see cref="ThermalResult"/> the Reference layer maps onto the proto; the kernel stays
/// decoupled from the event shape. Frames with empty temp arrays (ACC often reports none live) contribute
/// nothing — neither to a peak nor to an exposure ratio — so a lap with no temperature data yields
/// all-zero / false rather than throwing.
/// <para>
/// The overheat flags are deliberately NOT "the peak crossed the band once". A single hard stop legitimately
/// spikes a GT3 brake disc for a few tens of milliseconds: a real lap peaked 701 °C for 67 ms (0.015% of the
/// lap, median 414 °C) and the old peak-crossed-once rule announced "brakes overheated" while the in-game HUD
/// still read cold. Overheating is a SUSTAINED condition, so a flag requires the channel to sit above its band
/// for at least <paramref name="minOverheatFraction"/> of the frames that actually carried temperatures. The
/// peaks are still reported verbatim as metrics.
/// </para>
/// </summary>
public static class ThermalKernels
{
    /// <summary>
    /// Analyzes one lap's frames. Thresholds are supplied by the caller (config-driven, see
    /// <c>ComputeOptions</c>) so the kernel carries no tuning constants of its own.
    /// </summary>
    /// <param name="frames">The lap's frames.</param>
    /// <param name="tyreOverheatC">Tyre core temperature above which a frame counts as abusive.</param>
    /// <param name="brakeOverheatC">Brake temperature above which a frame counts as abusive.</param>
    /// <param name="minOverheatFraction">
    /// Fraction of the temperature-carrying frames that must be above the band before the flag is raised.
    /// </param>
    public static ThermalResult Analyze(
        IReadOnlyList<TelemetryFrame> frames,
        float tyreOverheatC,
        float brakeOverheatC,
        float minOverheatFraction)
    {
        ArgumentNullException.ThrowIfNull(frames);

        float maxTyre = 0f;
        float maxBrake = 0f;
        int tyreSampled = 0;
        int brakeSampled = 0;
        int tyreOver = 0;
        int brakeOver = 0;
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.TyreTempC.Count > 0)
            {
                float tyre = Peak(frame.TyreTempC);
                maxTyre = MathF.Max(maxTyre, tyre);
                tyreSampled++;
                if (tyre > tyreOverheatC)
                {
                    tyreOver++;
                }
            }

            if (frame.BrakeTempC.Count > 0)
            {
                float brake = Peak(frame.BrakeTempC);
                maxBrake = MathF.Max(maxBrake, brake);
                brakeSampled++;
                if (brake > brakeOverheatC)
                {
                    brakeOver++;
                }
            }
        }

        return new ThermalResult
        {
            MaxTyreTempC = maxTyre,
            MaxBrakeTempC = maxBrake,
            TyreOverheat = IsSustained(tyreOver, tyreSampled, minOverheatFraction),
            BrakeOverheat = IsSustained(brakeOver, brakeSampled, minOverheatFraction),
        };
    }

    private static bool IsSustained(int overFrames, int sampledFrames, float minOverheatFraction) =>
        sampledFrames > 0 && (float)overFrames / sampledFrames >= minOverheatFraction;

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
