using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class SessionLossAccumulatorTests
{
    [Fact]
    public void Aggregates_per_corner_totals_average_and_modal_reason_ordered_by_loss()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(Contribution("t01", 100, "early_brake"));
        acc.Accept(Contribution("t01", 200, "early_brake"));
        acc.Accept(Contribution("t01", 60, "low_min_speed"));   // t01 modal reason = early_brake
        acc.Accept(Contribution("t02", 90, "late_throttle"));

        IReadOnlyList<AggregatedLoss> losses = acc.Build(topN: 5);

        losses.Should().HaveCount(2);
        losses[0].CornerId.Should().Be("t01");
        losses[0].TotalLossMs.Should().Be(360);
        losses[0].SampleCount.Should().Be(3);
        losses[0].AvgLossMs.Should().Be(120);                   // 360 / 3
        losses[0].DominantReason.Should().Be("early_brake");
        losses[1].CornerId.Should().Be("t02");
        losses[1].TotalLossMs.Should().Be(90);
    }

    [Fact]
    public void Ignores_non_positive_deltas_so_a_no_reference_session_is_empty()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(Contribution("t01", 0, ""));        // no reference → delta 0
        acc.Accept(Contribution("t02", -50, "slower")); // faster than reference

        acc.Build(topN: 5).Should().BeEmpty();
    }

    [Fact]
    public void Bounds_output_to_top_n_by_total_loss()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(Contribution("t01", 100, "slower"));
        acc.Accept(Contribution("t02", 300, "slower"));
        acc.Accept(Contribution("t03", 200, "slower"));

        IReadOnlyList<AggregatedLoss> losses = acc.Build(topN: 2);

        losses.Should().HaveCount(2);
        losses.Select(l => l.CornerId).Should().ContainInOrder("t02", "t03");
    }

    [Fact]
    public void Breaks_ties_deterministically_by_corner_id()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(Contribution("t09", 100, "slower"));
        acc.Accept(Contribution("t02", 100, "slower"));

        IReadOnlyList<AggregatedLoss> losses = acc.Build(topN: 5);

        losses.Select(l => l.CornerId).Should().ContainInOrder("t02", "t09");
    }

    [Fact]
    public void Zero_cap_yields_empty()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(Contribution("t01", 100, "slower"));

        acc.Build(topN: 0).Should().BeEmpty();
    }

    [Fact]
    public void Aggregates_per_channel_diffs_abs_then_average_over_multiple_lossy_samples()
    {
        // abs-then-average (ADR-0020 decision 1): a corner braked 5 m early on one lap and 3 m late on the
        // next has a typical brake-point error of (5 + 3) / 2 = 4 m — NOT |(-5 + 3) / 2| = 1 m, which the
        // rejected average-then-abs order would report by letting the two mistakes cancel.
        var acc = new SessionLossAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -5f, throttleResume: -2f, minSpeed: -4f, line: 3f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: 3f, throttleResume: -6f, minSpeed: -2f, line: 1f));

        ChannelDiffAverages avg = acc.DiffAverages("t01");

        avg.BrakePointDiffM.Should().BeApproximately(4f, 1e-4f, "mean(|-5|, |3|) = 4 — abs-then-average, not |mean| = 1");
        avg.ThrottleResumeDiffM.Should().BeApproximately(4f, 1e-4f, "mean(|-2|, |-6|) = 4");
        avg.MinSpeedDiffKmh.Should().BeApproximately(3f, 1e-4f, "mean(|-4|, |-2|) = 3");
        avg.LineDeviationM.Should().BeApproximately(2f, 1e-4f, "mean(|3|, |1|) = 2");
    }

    [Fact]
    public void Diff_averages_are_conditioned_on_the_lossy_corner_set()
    {
        // MF-4 / ADR-0020 decision 6: the diagnostic-diff averages ride the SAME DeltaMs>0 gate as the loss
        // roll-up. A non-lossy contribution early-returns in Accept, so its (here huge) diffs never reach the
        // sums or the sample count — the average reflects the single lossy sample only, not an all-corner mean.
        var acc = new SessionLossAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: 10f, throttleResume: 10f, minSpeed: 10f, line: 10f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 0, brakePoint: 1000f, throttleResume: 1000f, minSpeed: 1000f, line: 1000f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: -50, brakePoint: 1000f, throttleResume: 1000f, minSpeed: 1000f, line: 1000f));

        ChannelDiffAverages avg = acc.DiffAverages("t01");

        avg.BrakePointDiffM.Should().BeApproximately(10f, 1e-4f, "only the DeltaMs>0 sample counts");
        avg.ThrottleResumeDiffM.Should().BeApproximately(10f, 1e-4f);
        avg.MinSpeedDiffKmh.Should().BeApproximately(10f, 1e-4f);
        avg.LineDeviationM.Should().BeApproximately(10f, 1e-4f);
    }

    [Fact]
    public void Diff_averages_are_zero_for_a_corner_with_no_lossy_contribution()
    {
        var acc = new SessionLossAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 0, brakePoint: 42f, throttleResume: 42f, minSpeed: 42f, line: 42f));

        ChannelDiffAverages avg = acc.DiffAverages("t01");

        avg.Should().Be(default(ChannelDiffAverages), "no lossy sample landed → the diagnostic averages stay zero");
    }

    private static CornerContribution Contribution(string cornerId, int deltaMs, string reason) =>
        new(cornerId, deltaMs, ApexPosition: 0.5f, reason, UndersteerScore: 0f, OversteerScore: 0f,
            BrakePointDiffM: 0f, ThrottleResumeDiffM: 0f, MinSpeedDiffKmh: 0f, RacingLineDeviationM: 0f);

    private static CornerContribution ContributionWithDiffs(
        string cornerId, int deltaMs, float brakePoint, float throttleResume, float minSpeed, float line) =>
        new(cornerId, deltaMs, ApexPosition: 0.5f, Reason: "slower", UndersteerScore: 0f, OversteerScore: 0f,
            BrakePointDiffM: brakePoint, ThrottleResumeDiffM: throttleResume, MinSpeedDiffKmh: minSpeed,
            RacingLineDeviationM: line);
}
