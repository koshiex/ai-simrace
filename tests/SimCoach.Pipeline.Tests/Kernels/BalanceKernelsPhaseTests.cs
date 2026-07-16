using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using Xunit;

namespace SimCoach.Pipeline.Tests.Kernels;

public sealed class BalanceKernelsPhaseTests
{
    [Fact]
    public void Analyze_phases_scores_entry_apex_exit_independently()
    {
        // Corner [0.10 → 0.15 → 0.20] with apex-band fraction 0.5 → entry [0.10, 0.125], apex [0.125, 0.175],
        // exit [0.175, 0.20]. Entry frames read OVERSTEER (rears slide more), exit frames read UNDERSTEER
        // (fronts slide more), the apex band has no frame. A single-window scalar would blur the two toward a
        // near-neutral average; scored per phase they separate cleanly — the whole point of BalancePhaseTrend.
        List<TelemetryFrame> frames =
        [
            SlipAt(pos: 0.11f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
            SlipAt(pos: 0.11f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
            SlipAt(pos: 0.11f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
            SlipAt(pos: 0.19f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            SlipAt(pos: 0.19f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            SlipAt(pos: 0.19f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
        ];

        PhaseBalanceScores scores = BalanceKernels.AnalyzePhases(
            frames, start: 0.10, apex: 0.15, end: 0.20, apexBandFraction: 0.5);

        scores.Entry.OversteerScore.Should().BeGreaterThan(0f, "entry-band frames read oversteer");
        scores.Entry.UndersteerScore.Should().Be(0f);
        scores.Exit.UndersteerScore.Should().BeGreaterThan(0f, "exit-band frames read understeer");
        scores.Exit.OversteerScore.Should().Be(0f);
        scores.Apex.Should().Be(new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f },
            "the apex band has no frame → neutral, not a leak from the neighbouring bands");
    }

    [Fact]
    public void Analyze_phases_empty_window_is_neutral_not_a_throw()
    {
        // BalanceKernels.Analyze throws on an empty window; the per-phase path must instead degrade each
        // absent band to the neutral {0,0} so a degenerate corner never takes down compute.
        PhaseBalanceScores scores = BalanceKernels.AnalyzePhases(
            [], start: 0.10, apex: 0.15, end: 0.20, apexBandFraction: 0.5);

        BalanceScores neutral = new() { UndersteerScore = 0f, OversteerScore = 0f };
        scores.Entry.Should().Be(neutral);
        scores.Apex.Should().Be(neutral);
        scores.Exit.Should().Be(neutral);
    }

    private static TelemetryFrame SlipAt(float pos, float fl, float fr, float rl, float rr)
    {
        TelemetryFrame frame = new() { NormalizedCarPosition = pos, SteerRad = 0.3f };
        frame.WheelSlip.AddRange([fl, fr, rl, rr]);
        return frame;
    }
}
