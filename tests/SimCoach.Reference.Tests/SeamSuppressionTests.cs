using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Contracts.V1;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// M2 (MUST-FIX #1 runtime half, PR-B3): the alien LINE reference NaN-masks its noisy seam bins. A corner
/// whose <c>[Start,End]</c> band STRADDLES a masked seam (real bins then NaN bins) must not voice a
/// fabricated partial-RMS <c>tighten_apex</c> ("ближе к апексу"). The straddling case is deliberately chosen
/// over a fully-masked corner: a fully-masked corner greens even while the leak ships, whereas this goes RED
/// without the corner-level M1 gate (the unmasked portion's ~3 m RMS &gt; 0.5 fires tighten_apex) and GREEN
/// only with it. The unmasked Ascari-like corner alongside proves the mask does not over-suppress real signal.
/// </summary>
public sealed class SeamSuppressionTests
{
    private const float LapLengthM = 1000f;
    private const int GridLength = 101;
    private const float BrakeWindowUpstreamM = 300f;
    private const double ApexWindowFraction = 0.25;
    private const float SeamStartPn = 0.92f; // bins at pn >= 0.92 are NaN-masked (Parabolica seam)

    private static readonly ActionRegistry _registry = ActionRegistry.Load();

    // Straddles the seam: real bins 0.88..0.919, NaN bins 0.92..0.98.
    private static readonly Corner _straddlingCorner = new()
    {
        Id = "monza_parabolica",
        StartPosition = 0.88f,
        ApexPosition = 0.93f,
        EndPosition = 0.98f,
    };

    // Fully-real (no masked bin): the Ascari-like strong-difference corner that must survive as signal.
    private static readonly Corner _ascariCorner = new()
    {
        Id = "monza_ascari",
        StartPosition = 0.73f,
        ApexPosition = 0.755f,
        EndPosition = 0.78f,
    };

    [Fact]
    public void A_straddling_seam_corner_suppresses_the_unsigned_line_deviation_and_tighten_apex()
    {
        ResampledLap time = FlatReference();
        ResampledLap alien = SeamMaskedAlienLine();
        IReadOnlyList<TelemetryFrame> self = OffLineVCorner(0.88f, 0.93f, 0.98f);

        (CornerEvent ev, CornerContribution contribution) = CornerEventBuilder.Build(
            _straddlingCorner, self, time, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction,
            lineReference: alien);

        ev.MinSpeedDiffKmh.Should().BeNegative("the self apex is slower than the reference (tighten_apex's second clause)");
        ev.RacingLineDeviationM.Should().Be(
            0f, "the corner band straddles a masked seam → the unsigned RMS is corner-gated to 0 (M1)");
        contribution.RacingLineDeviationM.Should().Be(
            0f, "the top-loss contribution carries the gated 0, not a partial RMS");

        _registry.ValidSubset(GoldFrom(ev), new CoachOptions())
            .Select(a => a.Id)
            .Should().NotContain(
                "tighten_apex", "a straddling-seam corner must not voice a fabricated apex-line cue");
    }

    [Fact]
    public void An_unmasked_strong_corner_still_produces_a_line_deviation_and_tighten_apex()
    {
        ResampledLap time = FlatReference();
        ResampledLap alien = SeamMaskedAlienLine();
        IReadOnlyList<TelemetryFrame> self = OffLineVCorner(0.73f, 0.755f, 0.78f);

        (CornerEvent ev, _) = CornerEventBuilder.Build(
            _ascariCorner, self, time, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction,
            lineReference: alien);

        ev.RacingLineDeviationM.Should().BeGreaterThan(
            0.5f, "the unmasked Ascari band carries the real ~3 m off-line offset");
        ev.MinSpeedDiffKmh.Should().BeNegative();

        _registry.ValidSubset(GoldFrom(ev), new CoachOptions())
            .Select(a => a.Id)
            .Should().Contain(
                "tighten_apex", "an unmasked strong corner still surfaces the apex-line cue");
    }

    private static DictionaryGoldView GoldFrom(CornerEvent ev) => new(
        CoachCadence.Corner,
        hasReference: true,
        new Dictionary<string, double>
        {
            ["racing_line_deviation_m"] = ev.RacingLineDeviationM,
            ["min_speed_diff_kmh"] = ev.MinSpeedDiffKmh,
        },
        new Dictionary<string, bool> { ["off_track"] = ev.OffTrack });

    // TIME reference: constant 60 m/s straight world line — drives min_speed_diff (self apex dips to 30).
    private static ResampledLap FlatReference()
    {
        float[] position = new float[GridLength];
        int[] tMs = new int[GridLength];
        float[] speed = new float[GridLength];
        float[] worldX = new float[GridLength];
        for (int k = 0; k < GridLength; k++)
        {
            float pos = k / 100f;
            position[k] = pos;
            tMs[k] = k * 10;
            speed[k] = 60f;
            worldX[k] = pos * LapLengthM;
        }

        return Grid(position, tMs, speed, worldX, new float[GridLength]);
    }

    // LINE reference: straight world line, NaN world coords for pn >= 0.92 (the seam-mask sentinel).
    private static ResampledLap SeamMaskedAlienLine()
    {
        float[] position = new float[GridLength];
        float[] worldX = new float[GridLength];
        float[] worldZ = new float[GridLength];
        for (int k = 0; k < GridLength; k++)
        {
            float pos = k / 100f;
            position[k] = pos;
            bool masked = pos >= SeamStartPn;
            worldX[k] = masked ? float.NaN : pos * LapLengthM;
            worldZ[k] = masked ? float.NaN : 0f;
        }

        return Grid(position, new int[GridLength], new float[GridLength], worldX, worldZ);
    }

    // Self path: a slow-apex V (min 30 m/s at the apex) running a constant ~3 m off the reference world line.
    private static IReadOnlyList<TelemetryFrame> OffLineVCorner(float start, float apex, float end)
    {
        List<TelemetryFrame> frames = [];
        const int n = 20;
        for (int i = 0; i <= n; i++)
        {
            float pos = start + ((end - start) * i / n);
            float speed = pos <= apex ? Lerp(60f, 30f, Frac(start, apex, pos)) : Lerp(30f, 60f, Frac(apex, end, pos));
            frames.Add(new TelemetryFrame
            {
                T = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddMilliseconds(15 * i)),
                NormalizedCarPosition = pos,
                SpeedMps = speed,
                BrakePct = 0.3f,
                ThrottlePct = 0.3f,
                SteerRad = 0.3f,
                WorldPos = new Vec3 { X = pos * LapLengthM, Y = 0f, Z = 3f },
                IsValidLap = true,
            });
        }

        return frames;
    }

    private static ResampledLap Grid(float[] position, int[] tMs, float[] speed, float[] worldX, float[] worldZ) =>
        new()
        {
            LapNumber = 1,
            GridLength = GridLength,
            PositionNormalized = position,
            TMsFromLapStart = tMs,
            SpeedMps = speed,
            ThrottlePct = new float[GridLength],
            BrakePct = new float[GridLength],
            SteerRad = new float[GridLength],
            Gear = new int[GridLength],
            TyreTempFl = new float[GridLength],
            TyreTempFr = new float[GridLength],
            TyreTempRl = new float[GridLength],
            TyreTempRr = new float[GridLength],
            GLat = new float[GridLength],
            GLong = new float[GridLength],
            WorldX = worldX,
            WorldY = new float[GridLength],
            WorldZ = worldZ,
        };

    private static float Frac(float from, float to, float value) => Math.Clamp((value - from) / (to - from), 0f, 1f);

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
