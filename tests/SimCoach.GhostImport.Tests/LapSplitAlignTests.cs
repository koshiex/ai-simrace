using FluentAssertions;
using SimCoach.Reference;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Loop-closure lap split, centerline alignment (with the ~2 m median-deviation fail-fast, OD5), and
/// per-metre LINE-only resample. Synthetic circular geometry only (<see cref="SyntheticTrackFixture"/>).
/// </summary>
public sealed class LapSplitAlignTests
{
    private static readonly GhostImportOptions _options = new();

    [Fact]
    public void Loop_closure_split_yields_one_lap_per_loop()
    {
        IReadOnlyList<GhostRecord> path = SyntheticTrackFixture.CircularGhost(laps: 2, radius: SyntheticTrackFixture.Radius);

        IReadOnlyList<IReadOnlyList<GhostRecord>> laps = LapSplitter.Split(path, _options);

        laps.Should().HaveCount(2);
        laps.Should().OnlyContain(lap => lap.Count > 0);
    }

    [Fact]
    public void Split_of_an_empty_path_yields_no_laps()
    {
        IReadOnlyList<IReadOnlyList<GhostRecord>> laps = LapSplitter.Split([], _options);

        laps.Should().BeEmpty();
    }

    [Fact]
    public void Alignment_reports_the_lateral_offset_as_median_deviation()
    {
        MedianCenterline centerline = SyntheticTrackFixture.CircleCenterline();
        IReadOnlyList<GhostRecord> lap = SyntheticTrackFixture.CircularGhost(laps: 1, radius: SyntheticTrackFixture.Radius + 1.5f);

        float median = CenterlineAligner.MedianDeviationM(lap, centerline);

        median.Should().BeApproximately(1.5f, 0.2f);
    }

    [Fact]
    public void Alignment_succeeds_below_the_ceiling_and_spans_the_pn_grid()
    {
        MedianCenterline centerline = SyntheticTrackFixture.CircleCenterline();
        IReadOnlyList<GhostRecord> lap = SyntheticTrackFixture.CircularGhost(laps: 1, radius: SyntheticTrackFixture.Radius + 1.5f);

        IReadOnlyList<AlignedPoint> aligned = CenterlineAligner.Align(lap, centerline, _options);

        aligned.Should().NotBeEmpty();
        aligned.Min(p => p.PositionNormalized).Should().BeLessThan(0.05f);
        aligned.Max(p => p.PositionNormalized).Should().BeGreaterThan(0.95f);
    }

    [Fact]
    public void Alignment_fails_fast_when_median_deviation_exceeds_the_ceiling()
    {
        MedianCenterline centerline = SyntheticTrackFixture.CircleCenterline();
        IReadOnlyList<GhostRecord> lap = SyntheticTrackFixture.CircularGhost(laps: 1, radius: SyntheticTrackFixture.Radius + 3f);

        Action align = () => CenterlineAligner.Align(lap, centerline, _options);

        align.Should().Throw<InvalidDataException>().WithMessage("*median alignment deviation*");
    }

    [Fact]
    public void Resample_produces_a_monotonic_pn_grid_with_only_line_channels_populated()
    {
        MedianCenterline centerline = SyntheticTrackFixture.CircleCenterline();
        IReadOnlyList<GhostRecord> lap = SyntheticTrackFixture.CircularGhost(laps: 1, radius: SyntheticTrackFixture.Radius + 1.5f);
        IReadOnlyList<AlignedPoint> aligned = CenterlineAligner.Align(lap, centerline, _options);

        ResampledLap grid = LineResampler.Resample(aligned, centerline.LapLengthM, lapNumber: 7, _options);

        grid.LapNumber.Should().Be(7);
        grid.GridLength.Should().Be(SyntheticTrackFixture.Bins);
        grid.PositionNormalized.Should().BeInAscendingOrder();
        grid.PositionNormalized[0].Should().BeApproximately(0f, 1e-4f);
        grid.PositionNormalized[^1].Should().BeApproximately((SyntheticTrackFixture.Bins - 1f) / SyntheticTrackFixture.Bins, 1e-3f);

        // World path is populated at the offset radius; every non-line channel stays zero.
        grid.WorldX.Should().Contain(x => Math.Abs(x) > 1f);
        for (int k = 0; k < grid.GridLength; k++)
        {
            float r = MathF.Sqrt((grid.WorldX[k] * grid.WorldX[k]) + (grid.WorldZ[k] * grid.WorldZ[k]));
            r.Should().BeApproximately(SyntheticTrackFixture.Radius + 1.5f, 1f);
        }

        grid.SpeedMps.Should().OnlyContain(v => v == 0f);
        grid.TMsFromLapStart.Should().OnlyContain(v => v == 0);
        grid.BrakePct.Should().OnlyContain(v => v == 0f);
        grid.ThrottlePct.Should().OnlyContain(v => v == 0f);
        grid.SteerRad.Should().OnlyContain(v => v == 0f);
        grid.Gear.Should().OnlyContain(v => v == 0);
        grid.GLat.Should().OnlyContain(v => v == 0f);
        grid.GLong.Should().OnlyContain(v => v == 0f);
        grid.WorldY.Should().OnlyContain(v => v == 0f);
    }
}
