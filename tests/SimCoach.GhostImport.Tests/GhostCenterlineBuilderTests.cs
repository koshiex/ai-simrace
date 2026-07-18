using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// The ghost-median centerline builder (B1b) must recover a track's shape from several ghost laps that each
/// have their OWN lap-start phase and arc-length total — the crux the bootstrap shared axis solves. These
/// tests build a known circle from phase-offset synthetic ghosts (NEVER the network) and assert the median
/// tracks that circle, the aggregate is trustworthy (LapCount &gt;= 3, non-empty bins, full span), and the
/// min-laps guard fires. A naive floor(LapDistanceM) binner would smear the phase-offset laps and fail these.
/// </summary>
public sealed class GhostCenterlineBuilderTests
{
    private const float Radius = 200f;
    private static readonly float _circumference = (float)(2d * Math.PI * Radius);
    private static readonly GhostImportOptions _options = new();

    /// <summary>
    /// One complete circular loop of <paramref name="points"/> samples at <paramref name="radius"/>, starting
    /// at <paramref name="phase"/> radians. Distinct phases give each ghost its own lap-start point (as the
    /// real loop-splitter would), which the shared-axis bootstrap must reconcile before binning.
    /// </summary>
    private static IReadOnlyList<GhostRecord> CircleLoop(float radius, float phase, int points = 1500)
    {
        var list = new List<GhostRecord>(points + 1);
        for (int i = 0; i <= points; i++)
        {
            double angle = phase + (2d * Math.PI * i / points);
            list.Add(new GhostRecord(
                WorldX: (float)(radius * Math.Cos(angle)),
                WorldY: 0f,
                WorldZ: (float)(radius * Math.Sin(angle)),
                Yaw: 0f,
                BrakeNorm: 0f,
                ThrottleNorm: 0f,
                RawTimestamp: i));
        }

        return list;
    }

    private static IReadOnlyList<IReadOnlyList<GhostRecord>> ThreePhaseOffsetGhosts() =>
    [
        CircleLoop(Radius - 1f, phase: 0f),
        CircleLoop(Radius, phase: 0.7f),
        CircleLoop(Radius + 1f, phase: 1.9f),
    ];

    [Fact]
    public void Build_yields_a_trustworthy_full_lap_centerline_from_phase_offset_ghosts()
    {
        GhostCenterlineResult result =
            GhostCenterlineBuilder.Build("monza", _circumference, ThreePhaseOffsetGhosts(), _options);

        result.Centerline.LapCount.Should().BeGreaterThanOrEqualTo(MedianCenterlineBuilder.MinLapsForTrust);
        result.Coherence.LapCount.Should().Be(3);
        result.Centerline.Bins.Should().NotBeEmpty();
        result.SpanFraction.Should().BeGreaterThanOrEqualTo(_options.MinGhostSpanFraction);
        result.Go.Should().BeTrue();
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Build_median_tracks_the_known_circle_within_tolerance()
    {
        const float toleranceM = 2f;

        GhostCenterlineResult result =
            GhostCenterlineBuilder.Build("monza", _circumference, ThreePhaseOffsetGhosts(), _options);

        IReadOnlyList<CenterlineBin> sampled = result.Centerline.Bins.Where(b => b.LapSamples > 0).ToList();
        sampled.Should().NotBeEmpty();
        foreach (CenterlineBin bin in sampled)
        {
            float r = MathF.Sqrt((bin.X * bin.X) + (bin.Z * bin.Z));
            r.Should().BeApproximately(Radius, toleranceM);
            bin.LateralG.Should().Be(0f);
        }
    }

    [Fact]
    public void Build_throws_when_fewer_than_min_laps_are_supplied()
    {
        IReadOnlyList<IReadOnlyList<GhostRecord>> tooFew =
        [
            CircleLoop(Radius, phase: 0f),
            CircleLoop(Radius, phase: 1.0f),
        ];

        Action build = () => GhostCenterlineBuilder.Build("monza", _circumference, tooFew, _options);

        build.Should().Throw<InvalidDataException>().WithMessage("*usable ghost lap*");
    }
}
