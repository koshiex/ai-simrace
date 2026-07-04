using FluentAssertions;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class SectorDeltaAggregatorTests
{
    [Fact]
    public void Median_of_two_clean_flying_laps_is_the_gain_not_a_poisoned_mean()
    {
        // Two coachable flying-lap S1 crossings, both gains vs the reference.
        int[] deltas = [-473, -20];

        SectorDeltaAggregator.Median(deltas).Should().Be(-246, "the median of two is their mid-average");
    }

    [Fact]
    public void Median_rejects_a_single_anomalous_crossing_that_would_flip_the_mean_positive()
    {
        // The "-14.8s S1" pathology in aggregate form: one implausible +14799 crossing sits alongside two
        // real gains. The mean is +4768 (a fabricated loss); the median stays at the true -20 gain.
        int[] deltas = [-473, -20, 14799];

        int mean = (deltas[0] + deltas[1] + deltas[2]) / deltas.Length;
        mean.Should().BePositive("the arithmetic mean is dragged positive by the outlier — the old bug");
        SectorDeltaAggregator.Median(deltas).Should().Be(-20, "the median rejects the lone outlier");
    }

    [Fact]
    public void Median_of_a_single_crossing_is_that_crossing()
    {
        SectorDeltaAggregator.Median([-42]).Should().Be(-42);
    }

    [Fact]
    public void Median_even_count_truncates_toward_zero()
    {
        // (-1 + -2) / 2 == -1 under C# integer division (truncation toward zero), documented tolerance.
        SectorDeltaAggregator.Median([-2, -1]).Should().Be(-1);
    }
}
