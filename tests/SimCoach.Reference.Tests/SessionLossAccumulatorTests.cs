using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class SessionLossAccumulatorTests
{
    [Fact]
    public void Aggregates_per_corner_totals_average_and_modal_reason_ordered_by_loss()
    {
        SessionLossAccumulator acc = NewAccumulator();
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
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(Contribution("t01", 0, ""));        // no reference → delta 0
        acc.Accept(Contribution("t02", -50, "slower")); // faster than reference

        acc.Build(topN: 5).Should().BeEmpty();
    }

    [Fact]
    public void Bounds_output_to_top_n_by_total_loss()
    {
        SessionLossAccumulator acc = NewAccumulator();
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
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(Contribution("t09", 100, "slower"));
        acc.Accept(Contribution("t02", 100, "slower"));

        IReadOnlyList<AggregatedLoss> losses = acc.Build(topN: 5);

        losses.Select(l => l.CornerId).Should().ContainInOrder("t02", "t09");
    }

    [Fact]
    public void Zero_cap_yields_empty()
    {
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(Contribution("t01", 100, "slower"));

        acc.Build(topN: 0).Should().BeEmpty();
    }

    [Fact]
    public void Aggregates_per_channel_diffs_abs_then_average_over_multiple_lossy_samples()
    {
        // abs-then-average (ADR-0020 decision 1): a corner braked 5 m early on one lap and 3 m late on the
        // next has a typical brake-point error of (5 + 3) / 2 = 4 m — NOT |(-5 + 3) / 2| = 1 m, which the
        // rejected average-then-abs order would report by letting the two mistakes cancel.
        SessionLossAccumulator acc = NewAccumulator();
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
        SessionLossAccumulator acc = NewAccumulator();
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
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 0, brakePoint: 42f, throttleResume: 42f, minSpeed: 42f, line: 42f));

        ChannelDiffAverages avg = acc.DiffAverages("t01");

        avg.Should().Be(default(ChannelDiffAverages), "no lossy sample landed → the diagnostic averages stay zero");
    }

    [Fact]
    public void Build_emits_the_four_diagnostic_diffs_abs_then_average()
    {
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -5f, throttleResume: -2f, minSpeed: -4f, line: 3f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: 3f, throttleResume: -6f, minSpeed: -2f, line: 1f));

        AggregatedLoss loss = acc.Build(topN: 5).Single();

        loss.AvgBrakePointDiffM.Should().BeApproximately(4f, 1e-4f, "mean(|-5|, |3|) = 4");
        loss.AvgThrottleResumeDiffM.Should().BeApproximately(4f, 1e-4f, "mean(|-2|, |-6|) = 4");
        loss.AvgMinSpeedDiffKmh.Should().BeApproximately(3f, 1e-4f, "mean(|-4|, |-2|) = 3");
        loss.AvgLineDeviationM.Should().BeApproximately(2f, 1e-4f, "mean(|3|, |1|) = 2");
    }

    [Fact]
    public void Build_populates_the_concrete_ADR0020_diagnostic_channel_set()
    {
        // Completeness probe (commit 8): the emitted AggregatedLoss carries ALL FOUR concrete ADR-0020
        // diagnostic channels — brake_point, throttle_resume, min_speed, line_deviation — not a subset and
        // not a count. A non-zero, distinct value per channel guards against a copy-paste that wires one
        // channel into another's field (each field must read back the channel it names).
        SessionLossAccumulator acc = NewAccumulator();
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: 7f, throttleResume: 11f, minSpeed: 13f, line: 17f));

        AggregatedLoss loss = acc.Build(topN: 1).Single();

        loss.AvgBrakePointDiffM.Should().BeApproximately(7f, 1e-4f, "field 6 carries the brake_point channel");
        loss.AvgThrottleResumeDiffM.Should().BeApproximately(11f, 1e-4f, "field 7 carries the throttle_resume channel");
        loss.AvgMinSpeedDiffKmh.Should().BeApproximately(13f, 1e-4f, "field 8 carries the min_speed channel");
        loss.AvgLineDeviationM.Should().BeApproximately(17f, 1e-4f, "field 9 carries the line_deviation channel");
    }

    [Fact]
    public void Sum_invariant_is_abs_then_average_and_falsifiable_on_a_bidirectional_channel()
    {
        // MF-3 / ADR-0020: the sum-invariant aggregate == mean(|per_corner_diff|) is only FALSIFIABLE on a
        // BIDIRECTIONAL channel with mixed-sign per-corner diffs. brake_point is bidirectional; the fixture
        // below mixes an early (-6) and a late (+2) brake. On a same-sign channel abs-then-average equals
        // |average-then-abs| identically and the assertion below could never fire — so this test fail-fasts
        // on the mixed-sign precondition, then proves the injected sign fault (average-then-abs) would RED.
        float[] brakeDiffs = { -6f, 2f };

        brakeDiffs.Should().Contain(d => d > 0f, "the fixture MUST contain a positive brake diff or the invariant is vacuous");
        brakeDiffs.Should().Contain(d => d < 0f, "the fixture MUST contain a negative brake diff or the invariant is vacuous");

        SessionLossAccumulator acc = NewAccumulator();
        foreach (float d in brakeDiffs)
        {
            acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: d, throttleResume: 0f, minSpeed: 0f, line: 0f));
        }

        AggregatedLoss loss = acc.Build(topN: 1).Single();

        float absThenAverage = brakeDiffs.Select(MathF.Abs).Average();   // (6 + 2) / 2 = 4 — CHOSEN
        float averageThenAbs = MathF.Abs(brakeDiffs.Average());          // |(-6 + 2) / 2| = 2 — the sign FAULT

        // The two aggregation orders MUST diverge on this fixture, else the invariant proves nothing.
        averageThenAbs.Should().NotBeApproximately(absThenAverage, 0.5f,
            "on a mixed-sign fixture abs-then-average (4) and the average-then-abs fault (2) diverge");

        loss.AvgBrakePointDiffM.Should().BeApproximately(absThenAverage, 1e-4f, "the emitted aggregate is abs-then-average");
        loss.AvgBrakePointDiffM.Should().NotBeApproximately(averageThenAbs, 0.5f,
            "injecting the sign fault (average-then-abs) would make this assertion RED — the invariant is falsifiable");
    }

    [Fact]
    public void Dominant_channel_argmax_scales_signed_diffs_onto_a_common_ms_axis()
    {
        // M36: replaces DominantReason as the authoritative pick. brake_point 5 m x 10 ms/m = 50 beats
        // min_speed 2 km/h x 20 ms/kmh = 40, so brake_point wins with value 50.
        var acc = new SessionLossAccumulator(Scales(brake: 10f, throttle: 10f, minSpeed: 20f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -5f, throttleResume: 0f, minSpeed: -2f, line: 0f));

        AggregatedLoss loss = acc.Build(topN: 1).Single();

        loss.DominantChannel.Should().Be("brake_point");
        loss.DominantChannelValue.Should().Be(50);
        loss.DominantReason.Should().Be("slower", "field 5 is retained for back-compat, no longer authoritative");
    }

    [Fact]
    public void Changing_only_a_scale_flips_the_picked_channel()
    {
        // MF-6 config rule: the pick is decided purely by IOptions scales. Same corner diffs, ONLY the
        // min-speed scale changes (20 -> 30 ms per km/h) — min_speed (2 x 30 = 60) now beats brake (5 x 10 = 50).
        CornerContribution corner =
            ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -5f, throttleResume: 0f, minSpeed: -2f, line: 0f);

        var brakeWins = new SessionLossAccumulator(Scales(brake: 10f, throttle: 10f, minSpeed: 20f));
        brakeWins.Accept(corner);

        var minSpeedWins = new SessionLossAccumulator(Scales(brake: 10f, throttle: 10f, minSpeed: 30f));
        minSpeedWins.Accept(corner);

        brakeWins.Build(topN: 1).Single().DominantChannel.Should().Be("brake_point");
        minSpeedWins.Build(topN: 1).Single().DominantChannel.Should().Be("min_speed",
            "flipping ONLY the min-speed scale changes the winner");
    }

    [Fact]
    public void Line_deviation_is_never_picked_even_when_it_dwarfs_a_real_signed_loss()
    {
        // MF-2 / ADR-0020: the unsigned RMS line-deviation is excluded from the argmax DOMAIN. A corner with
        // a genuine signed brake-point loss AND a huge line deviation must pick the signed channel, never a
        // (non-existent) "line_deviation" channel — even though |line| swamps every signed diff.
        var acc = new SessionLossAccumulator(Scales(brake: 10f, throttle: 10f, minSpeed: 20f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -3f, throttleResume: 0f, minSpeed: 0f, line: 1000f));

        AggregatedLoss loss = acc.Build(topN: 1).Single();

        loss.DominantChannel.Should().Be("brake_point");
        loss.DominantChannel.Should().NotBe("line_deviation", "the unsigned RMS channel is not in the argmax domain");
    }

    [Fact]
    public void Dominant_channel_is_empty_when_no_signed_channel_has_a_magnitude()
    {
        // A lossy corner whose signed diffs are all zero (loss came from elsewhere) has no dominant channel.
        var acc = new SessionLossAccumulator(Scales(brake: 10f, throttle: 10f, minSpeed: 20f));
        acc.Accept(ContributionWithDiffs("t01", deltaMs: 100, brakePoint: 0f, throttleResume: 0f, minSpeed: 0f, line: 42f));

        AggregatedLoss loss = acc.Build(topN: 1).Single();

        loss.DominantChannel.Should().BeEmpty();
        loss.DominantChannelValue.Should().Be(0);
    }

    [Fact]
    public void Dominant_channel_pick_is_idempotent_for_the_same_input_and_scales()
    {
        // Same contributions + same scales must yield a stable pick and value across independent runs.
        ChannelLossScales scales = Scales(brake: 10f, throttle: 10f, minSpeed: 20f);
        CornerContribution corner =
            ContributionWithDiffs("t01", deltaMs: 100, brakePoint: -5f, throttleResume: -2f, minSpeed: -4f, line: 3f);

        var first = new SessionLossAccumulator(scales);
        first.Accept(corner);
        var second = new SessionLossAccumulator(scales);
        second.Accept(corner);

        AggregatedLoss a = first.Build(topN: 1).Single();
        AggregatedLoss b = second.Build(topN: 1).Single();

        a.DominantChannel.Should().Be(b.DominantChannel);
        a.DominantChannelValue.Should().Be(b.DominantChannelValue);
    }

    private static SessionLossAccumulator NewAccumulator() =>
        new(Scales(brake: 10f, throttle: 10f, minSpeed: 20f));

    private static ChannelLossScales Scales(float brake, float throttle, float minSpeed) =>
        new(brake, throttle, minSpeed);

    private static CornerContribution Contribution(string cornerId, int deltaMs, string reason) =>
        new(cornerId, deltaMs, ApexPosition: 0.5f, reason, UndersteerScore: 0f, OversteerScore: 0f,
            BrakePointDiffM: 0f, ThrottleResumeDiffM: 0f, MinSpeedDiffKmh: 0f, RacingLineDeviationM: 0f);

    private static CornerContribution ContributionWithDiffs(
        string cornerId, int deltaMs, float brakePoint, float throttleResume, float minSpeed, float line) =>
        new(cornerId, deltaMs, ApexPosition: 0.5f, Reason: "slower", UndersteerScore: 0f, OversteerScore: 0f,
            BrakePointDiffM: brakePoint, ThrottleResumeDiffM: throttleResume, MinSpeedDiffKmh: minSpeed,
            RacingLineDeviationM: line);
}
