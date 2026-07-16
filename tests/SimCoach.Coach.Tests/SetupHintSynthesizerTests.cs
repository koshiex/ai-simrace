using FluentAssertions;
using SimCoach.Coach;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class SetupHintSynthesizerTests
{
    private const double Threshold = 0.15;

    [Fact]
    public void Grounds_understeer_on_the_dominant_band_when_it_clears_the_threshold()
    {
        IReadOnlyList<BalancePhaseTrend> trends =
        [
            Trend("entry", 0.32f, 6),
            Trend("apex", 0.05f, 6),
            Trend("exit", -0.10f, 6),
        ];

        SetupHintSynthesizer.Synthesize(trends, Threshold).Should().Be("устойчивый снос на входе в поворот");
    }

    [Fact]
    public void Grounds_oversteer_when_the_dominant_band_is_negative()
    {
        IReadOnlyList<BalancePhaseTrend> trends =
        [
            Trend("entry", 0.05f, 6),
            Trend("apex", 0.08f, 6),
            Trend("exit", -0.41f, 6),
        ];

        SetupHintSynthesizer.Synthesize(trends, Threshold).Should().Be("устойчивый занос на выходе");
    }

    [Fact]
    public void Drops_to_null_when_no_band_clears_the_threshold()
    {
        IReadOnlyList<BalancePhaseTrend> trends =
        [
            Trend("entry", 0.05f, 6),
            Trend("apex", -0.08f, 6),
            Trend("exit", 0.10f, 6),
        ];

        SetupHintSynthesizer.Synthesize(trends, Threshold).Should().BeNull();
    }

    [Fact]
    public void Drops_to_null_on_an_empty_trend()
    {
        SetupHintSynthesizer.Synthesize([], Threshold).Should().BeNull();
    }

    [Fact]
    public void Ignores_unsampled_bands_when_picking_the_dominant()
    {
        // The exit band has the biggest magnitude but zero samples — it must not ground the hint; the sampled
        // entry band (understeer) wins instead.
        IReadOnlyList<BalancePhaseTrend> trends =
        [
            Trend("entry", 0.22f, 6),
            Trend("apex", 0.01f, 6),
            Trend("exit", -0.90f, 0),
        ];

        SetupHintSynthesizer.Synthesize(trends, Threshold).Should().Be("устойчивый снос на входе в поворот");
    }

    private static BalancePhaseTrend Trend(string phase, float balance, int sampleCount) =>
        new() { Phase = phase, Balance = balance, SampleCount = sampleCount };
}
