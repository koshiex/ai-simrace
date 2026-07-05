using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class SignedLineDeviationTests
{
    private static readonly float[] _bandPositions = [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f];

    [Fact]
    public void Self_outside_the_reference_line_yields_a_positive_signed_deviation()
    {
        // Reference is a CCW quarter-circle of radius 100; the self line rides the same arc 5 m WIDER
        // (radius 105). Outside the reference line → positive (ADR-0018 sign convention).
        ResampledLap reference = Arc(radius: 100f, n: 101);
        IReadOnlyList<TelemetryFrame> self = ArcSelf(radius: 105f, _bandPositions);

        float signed = SignedLineDeviation.MedianSignedOffset(self, reference, new PhaseBand(0.1f, 0.9f));

        signed.Should().BeApproximately(5f, 0.5f, "self runs ~5 m outside (wider than) the reference line");
    }

    [Fact]
    public void Self_inside_the_reference_line_yields_a_negative_signed_deviation()
    {
        ResampledLap reference = Arc(radius: 100f, n: 101);
        IReadOnlyList<TelemetryFrame> self = ArcSelf(radius: 95f, _bandPositions);

        float signed = SignedLineDeviation.MedianSignedOffset(self, reference, new PhaseBand(0.1f, 0.9f));

        signed.Should().BeApproximately(-5f, 0.5f, "self runs ~5 m inside (tighter than) the reference line");
    }

    [Fact]
    public void A_near_straight_band_neutralises_the_sign_to_zero()
    {
        // A straight reference has no defined inside/outside; even a laterally-offset self returns 0.
        ResampledLap reference = Straight(n: 101);
        IReadOnlyList<TelemetryFrame> self =
            [.. _bandPositions.Select(p => Frame(p, worldX: p * 1000f, worldZ: 5f))];

        float signed = SignedLineDeviation.MedianSignedOffset(self, reference, new PhaseBand(0.1f, 0.9f));

        signed.Should().Be(0f, "a straight band has an undefined side → neutralised");
    }

    [Fact]
    public void Entry_apex_exit_bands_are_contiguous_over_the_corner_window()
    {
        (PhaseBand entry, PhaseBand apex, PhaseBand exit) =
            SignedLineDeviation.EntryApexExitBands(0.30, 0.40, 0.50, 0.25);

        entry.Lo.Should().BeApproximately(0.30f, 1e-4f);
        entry.Hi.Should().Be(apex.Lo);
        apex.Hi.Should().Be(exit.Lo);
        exit.Hi.Should().BeApproximately(0.50f, 1e-4f);
    }

    private static ResampledLap Arc(float radius, int n)
    {
        float[] pos = new float[n];
        float[] worldX = new float[n];
        float[] worldZ = new float[n];
        for (int k = 0; k < n; k++)
        {
            float theta = MathF.PI / 2f * k / (n - 1);
            pos[k] = (float)k / (n - 1);
            worldX[k] = radius * MathF.Cos(theta);
            worldZ[k] = radius * MathF.Sin(theta);
        }

        return Grid(pos, worldX, worldZ);
    }

    private static ResampledLap Straight(int n)
    {
        float[] pos = new float[n];
        float[] worldX = new float[n];
        for (int k = 0; k < n; k++)
        {
            pos[k] = (float)k / (n - 1);
            worldX[k] = pos[k] * 1000f;
        }

        return Grid(pos, worldX, new float[n]);
    }

    private static IReadOnlyList<TelemetryFrame> ArcSelf(float radius, IReadOnlyList<float> positions)
    {
        List<TelemetryFrame> frames = [];
        foreach (float p in positions)
        {
            float theta = MathF.PI / 2f * p;
            frames.Add(Frame(p, radius * MathF.Cos(theta), radius * MathF.Sin(theta)));
        }

        return frames;
    }

    private static TelemetryFrame Frame(float pos, float worldX, float worldZ) => new()
    {
        NormalizedCarPosition = pos,
        WorldPos = new Vec3 { X = worldX, Y = 0f, Z = worldZ },
    };

    private static ResampledLap Grid(float[] pos, float[] worldX, float[] worldZ)
    {
        int n = pos.Length;
        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = n,
            PositionNormalized = pos,
            TMsFromLapStart = new int[n],
            SpeedMps = new float[n],
            ThrottlePct = new float[n],
            BrakePct = new float[n],
            SteerRad = new float[n],
            Gear = new int[n],
            TyreTempFl = new float[n],
            TyreTempFr = new float[n],
            TyreTempRl = new float[n],
            TyreTempRr = new float[n],
            GLat = new float[n],
            GLong = new float[n],
            WorldX = worldX,
            WorldY = new float[n],
            WorldZ = worldZ,
        };
    }
}
