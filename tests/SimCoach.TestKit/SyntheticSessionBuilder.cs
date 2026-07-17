using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;

namespace SimCoach.TestKit;

/// <summary>
/// Builds a deterministic multi-lap <see cref="TelemetryFrame"/> stream from a
/// <see cref="SyntheticTrack"/>, so compute code (lap/sector segmentation, kernels, corner models)
/// has lap/sector/corner structure to test against without a real ACC capture.
/// Pure and deterministic — every value derives from the frame index, never from wall-clock or RNG.
/// </summary>
public static class SyntheticSessionBuilder
{
    private const float StraightSpeedMps = 70f;
    private const float TwoPi = 2f * MathF.PI;
    private static readonly TimeSpan _frameInterval = TimeSpan.FromMilliseconds(10); // 100 Hz
    private static readonly DateTimeOffset _defaultStart = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Synthesizes <paramref name="lapCount"/> laps. Frames are evenly spaced in position
    /// (<paramref name="samplesPerLap"/> per lap). Laps in <paramref name="dirtyLaps"/> (1-based)
    /// carry <c>is_valid_lap = false</c>.
    /// </summary>
    public static IReadOnlyList<TelemetryFrame> Build(
        SyntheticTrack track,
        int lapCount,
        IReadOnlySet<int>? dirtyLaps = null,
        int samplesPerLap = 200,
        DateTimeOffset? startUtc = null,
        IReadOnlySet<int>? pitLaps = null,
        string weatherBucket = "dry-warm")
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrEmpty(weatherBucket);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerLap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(track.SectorCount);

        DateTimeOffset start = startUtc ?? _defaultStart;
        float radiusM = track.LapLengthM / TwoPi;
        int totalFrames = lapCount * samplesPerLap;
        List<TelemetryFrame> frames = new(totalFrames);

        for (int i = 0; i < totalFrames; i++)
        {
            // Single cumulative source: lap number and in-lap position both come from the frame index.
            int lapNumber = (i / samplesPerLap) + 1;
            float pos = (i % samplesPerLap) / (float)samplesPerLap; // 0..<1, wraps each lap
            bool isValid = dirtyLaps is null || !dirtyLaps.Contains(lapNumber);
            bool inPit = pitLaps is not null && pitLaps.Contains(lapNumber);

            frames.Add(BuildFrame(
                track, lapNumber, pos, radiusM, isValid, inPit, start + (i * _frameInterval), weatherBucket));
        }

        return frames;
    }

    private static TelemetryFrame BuildFrame(
        SyntheticTrack track, int lapNumber, float pos, float radiusM, bool isValid, bool inPit, DateTimeOffset t,
        string weatherBucket)
    {
        CornerState corner = CornerStateAt(track.Corners, pos);
        int sectorIndex = Math.Min((int)(pos * track.SectorCount), track.SectorCount - 1);

        var frame = new TelemetryFrame
        {
            T = Timestamp.FromDateTimeOffset(t),
            Sim = "acc",
            TrackId = track.TrackId,
            CarId = "synthetic_gt3",
            WeatherBucket = weatherBucket,
            LapNumber = lapNumber,
            LapDistanceM = pos * track.LapLengthM,
            NormalizedCarPosition = pos,
            SpeedMps = corner.SpeedMps,
            ThrottlePct = corner.ThrottlePct,
            BrakePct = corner.BrakePct,
            SteerRad = corner.SteerRad,
            Gear = 4,
            Rpm = 7000f,
            WorldPos = new Vec3
            {
                X = MathF.Cos(TwoPi * pos) * radiusM,
                Y = 0f,
                Z = MathF.Sin(TwoPi * pos) * radiusM,
            },
            CurrentSectorIndex = sectorIndex,
            SectorCount = track.SectorCount,
            TyresOut = 0,
            IsValidLap = isValid,
            // Pit laps carry a skewed (out-lap-reset) per-lap fuel estimate that must be excluded from the avg.
            FuelPerLapL = inPit ? 0f : 2.5f,
            IsInPitLane = inPit,
        };

        // Deterministic tip-quality inputs derived from the corner state (Phase 3 kernels exercise these
        // end-to-end). Values stay below the abuse bands so overheat flags are false by default, but the
        // peaks are non-zero so the kernel→event wiring is actually proven (not all-zero).
        float tyreTemp = 90f + (corner.BrakePct * 15f);          // < 110 abuse band
        float brakeTemp = 300f + (corner.BrakePct * 300f);       // < 700 abuse band
        float rearSlip = corner.ThrottlePct * 0.25f;             // drive-wheel slip on power
        frame.TyreTempC.AddRange([tyreTemp, tyreTemp, tyreTemp, tyreTemp]);
        frame.BrakeTempC.AddRange([brakeTemp, brakeTemp, brakeTemp, brakeTemp]);
        frame.SlipRatio.AddRange([0.02f, 0.02f, rearSlip, rearSlip]);
        return frame;
    }

    private readonly record struct CornerState(float SpeedMps, float ThrottlePct, float BrakePct, float SteerRad);

    private static CornerState CornerStateAt(IReadOnlyList<SyntheticCorner> corners, float pos)
    {
        foreach (SyntheticCorner c in corners)
        {
            if (pos < c.EntryPos || pos > c.ExitPos)
            {
                continue;
            }

            if (pos <= c.ApexPos)
            {
                // Braking phase: brake rises, speed falls toward the apex, throttle off.
                float u = Fraction(c.EntryPos, c.ApexPos, pos);
                return new CornerState(
                    SpeedMps: Lerp(StraightSpeedMps, c.MinSpeedMps, u),
                    ThrottlePct: 0f,
                    BrakePct: c.BrakePeak * u,
                    SteerRad: 0.4f * u);
            }

            // Exit phase: throttle resumes, speed recovers, brake released.
            float v = Fraction(c.ApexPos, c.ExitPos, pos);
            return new CornerState(
                SpeedMps: Lerp(c.MinSpeedMps, StraightSpeedMps, v),
                ThrottlePct: v,
                BrakePct: 0f,
                SteerRad: 0.4f * (1f - v));
        }

        // Straight: full throttle, no brake, no steering.
        return new CornerState(StraightSpeedMps, 1f, 0f, 0f);
    }

    private static float Fraction(float from, float to, float value) =>
        Math.Clamp((value - from) / (to - from), 0f, 1f);

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
