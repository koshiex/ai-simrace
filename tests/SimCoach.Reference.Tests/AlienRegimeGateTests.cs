using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// M38 alien-regime review (MUST-FIX #5, OD7): against a real 2–4 m alien corridor the same fast corners now
/// show genuine offsets, but the owner decision is to KEEP the config relevance-gate + the <c>LateralG</c>
/// neutralisation and NOT signed-line-coach fast/LateralG corners. These tests LOCK that decision and prove
/// the gate is config-driven (no magic number): lowering <c>LineRelevanceMaxRadiusM</c> flips whether a
/// fast-corner alien difference becomes coachable. The apex is still handled by the (seam-gated) unsigned cue.
/// </summary>
public sealed class AlienRegimeGateTests
{
    private const float LapLengthM = 1000f;
    private const int GridLength = 101;
    private const float BrakeWindowUpstreamM = 300f;
    private const double ApexWindowFraction = 0.25;

    private static readonly Corner _fastCorner = new()
    {
        Id = "monza_curva_grande",
        StartPosition = 0.30f,
        ApexPosition = 0.40f,
        EndPosition = 0.50f,
        ApexRadiusM = 200f, // a fast, large-radius corner
    };

    private static readonly Corner _lateralGCorner = new()
    {
        Id = "monza_sweep",
        StartPosition = 0.30f,
        ApexPosition = 0.40f,
        EndPosition = 0.50f,
        ApexRadiusM = 50f,
        Trigger = "LateralG",
    };

    [Fact]
    public void The_relevance_gate_is_config_driven_a_lower_ceiling_silences_a_fast_corner_alien_line()
    {
        ResampledLap time = StraightReference();
        ResampledLap alien = CurvedAlienLine(100f);
        IReadOnlyList<TelemetryFrame> self = OffLineArc(103f); // ~3 m outside the alien corridor

        (CornerEvent coachable, _) = CornerEventBuilder.Build(
            _fastCorner, self, time, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction,
            lineRelevanceMaxRadiusM: 300f, lineReference: alien);
        (CornerEvent gated, _) = CornerEventBuilder.Build(
            _fastCorner, self, time, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction,
            lineRelevanceMaxRadiusM: 150f, lineReference: alien);

        coachable.ExitLineDeviationM.Should().BeGreaterThan(
            1f, "radius 200 <= ceiling 300 → the fast-corner alien offset is line-relevant and surfaces");
        gated.ExitLineDeviationM.Should().Be(
            0f, "radius 200 > ceiling 150 → the same corner is gated: coachability flips on config, not a magic number");
    }

    [Fact]
    public void A_lateral_g_corner_is_not_signed_line_coached_even_against_a_real_alien_offset()
    {
        ResampledLap time = StraightReference();
        ResampledLap alien = CurvedAlienLine(100f);
        IReadOnlyList<TelemetryFrame> self = OffLineArc(103f);

        (CornerEvent ev, _) = CornerEventBuilder.Build(
            _lateralGCorner, self, time, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction,
            lineRelevanceMaxRadiusM: 300f, lineReference: alien);

        ev.EntryLineDeviationM.Should().Be(0f);
        ev.ApexLineDeviationM.Should().Be(0f);
        ev.ExitLineDeviationM.Should().Be(0f, "a LateralG fast corner is intentionally NOT signed-line-coached (OD7)");
        ev.RacingLineDeviationM.Should().BeGreaterThan(
            0.5f, "the unsigned apex cue still fires — it is gated only by the seam mask, not by LateralG");
    }

    private static ResampledLap StraightReference()
    {
        float[] position = new float[GridLength];
        float[] worldX = new float[GridLength];
        for (int k = 0; k < GridLength; k++)
        {
            float pos = k / 100f;
            position[k] = pos;
            worldX[k] = pos * LapLengthM;
        }

        return Grid(position, worldX, new float[GridLength]);
    }

    private static ResampledLap CurvedAlienLine(float radius)
    {
        float[] position = new float[GridLength];
        float[] worldX = new float[GridLength];
        float[] worldZ = new float[GridLength];
        for (int k = 0; k < GridLength; k++)
        {
            float pos = k / 100f;
            float theta = MathF.PI / 2f * pos;
            position[k] = pos;
            worldX[k] = radius * MathF.Cos(theta);
            worldZ[k] = radius * MathF.Sin(theta);
        }

        return Grid(position, worldX, worldZ);
    }

    private static IReadOnlyList<TelemetryFrame> OffLineArc(float radius)
    {
        List<TelemetryFrame> frames = [];
        for (int i = 0; i <= 20; i++)
        {
            float pos = 0.30f + (0.01f * i);
            float theta = MathF.PI / 2f * pos;
            frames.Add(new TelemetryFrame
            {
                T = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddMilliseconds(15 * i)),
                NormalizedCarPosition = pos,
                SpeedMps = 50f,
                BrakePct = 0.3f,
                ThrottlePct = 0.3f,
                SteerRad = 0.3f,
                WorldPos = new Vec3 { X = radius * MathF.Cos(theta), Y = 0f, Z = radius * MathF.Sin(theta) },
                IsValidLap = true,
            });
        }

        return frames;
    }

    private static ResampledLap Grid(float[] position, float[] worldX, float[] worldZ) => new()
    {
        LapNumber = 1,
        GridLength = GridLength,
        PositionNormalized = position,
        TMsFromLapStart = new int[GridLength],
        SpeedMps = new float[GridLength],
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
}
