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

    private static CornerContribution Contribution(string cornerId, int deltaMs, string reason) =>
        new(cornerId, deltaMs, ApexPosition: 0.5f, reason, UndersteerScore: 0f, OversteerScore: 0f);
}
