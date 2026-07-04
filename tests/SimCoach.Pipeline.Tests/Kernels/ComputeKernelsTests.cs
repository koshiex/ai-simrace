using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using Xunit;

namespace SimCoach.Pipeline.Tests.Kernels;

public sealed class ComputeKernelsTests
{
    // A hand-built braking corner: brake rises to 0.8 then releases, speed dips to 20 m/s, throttle
    // resumes at pos 0.40, steering present throughout the braking phase (trail-braking).
    private static readonly TelemetryFrame[] _brakingCorner =
    [
        Frame(pos: 0.00f, brake: 0.0f, throttle: 1.0f, speed: 60f, steer: 0.0f),
        Frame(pos: 0.10f, brake: 0.2f, throttle: 0.0f, speed: 50f, steer: 0.2f),
        Frame(pos: 0.20f, brake: 0.8f, throttle: 0.0f, speed: 30f, steer: 0.3f),
        Frame(pos: 0.30f, brake: 0.3f, throttle: 0.0f, speed: 20f, steer: 0.2f),
        Frame(pos: 0.40f, brake: 0.0f, throttle: 0.6f, speed: 35f, steer: 0.1f),
        Frame(pos: 0.50f, brake: 0.0f, throttle: 1.0f, speed: 55f, steer: 0.0f),
    ];

    [Fact]
    public void Brake_profile_reports_peak_on_off_and_trail_brake()
    {
        BrakeProfile profile = BrakeKernels.Analyze(_brakingCorner);

        profile.PeakBrakePct.Should().BeApproximately(0.8f, 1e-4f);
        profile.BrakeOnPosition.Should().BeApproximately(0.10f, 1e-4f);
        profile.BrakeOffPosition.Should().BeApproximately(0.40f, 1e-4f);
        // All three braking frames (0.2, 0.8, 0.3) also carry steering > 0.1 → fully trail-braked.
        profile.TrailBrakePct.Should().BeApproximately(1.0f, 1e-4f);
    }

    [Fact]
    public void Brake_onset_is_found_in_the_upstream_pre_roll()
    {
        // M16 feeds BrakeKernels a window that arms upstream of the geometric corner start; the kernel is
        // unchanged, but the earliest brake-on frame now sits in that pre-roll. The onset must report that
        // upstream position, not the first in-corner frame — that is what makes brake_point_diff non-zero.
        TelemetryFrame[] withPreRoll =
        [
            Frame(pos: 0.05f, brake: 0.0f, throttle: 1.0f, speed: 70f, steer: 0.0f),
            Frame(pos: 0.12f, brake: 0.6f, throttle: 0.0f, speed: 55f, steer: 0.1f), // onset, upstream of a 0.30 start
            Frame(pos: 0.30f, brake: 0.8f, throttle: 0.0f, speed: 35f, steer: 0.3f),
            Frame(pos: 0.45f, brake: 0.0f, throttle: 0.6f, speed: 40f, steer: 0.1f),
        ];

        BrakeProfile profile = BrakeKernels.Analyze(withPreRoll);

        profile.BrakeOnPosition.Should().BeApproximately(0.12f, 1e-4f);
    }

    [Fact]
    public void Corner_metrics_report_min_speed_and_throttle_resume()
    {
        CornerMetrics metrics = ThrottleSpeedKernels.Analyze(_brakingCorner);

        metrics.MinSpeedMps.Should().BeApproximately(20f, 1e-4f);
        metrics.MinSpeedPosition.Should().BeApproximately(0.30f, 1e-4f);
        metrics.ThrottleOnPosition.Should().BeApproximately(0.40f, 1e-4f);
        metrics.HasInSpanMinimum.Should().BeTrue("the speed dips to a genuine apex strictly inside the window");
    }

    [Fact]
    public void Corner_metrics_flag_no_in_span_minimum_for_a_monotonic_transit()
    {
        // Speed only decelerates through the window, so the minimum lands on the trailing endpoint —
        // not a coachable apex. D-minspeed relies on this flag to suppress the min-speed contribution.
        TelemetryFrame[] monotonic =
        [
            Frame(pos: 0.00f, brake: 0.0f, throttle: 1.0f, speed: 60f, steer: 0.0f),
            Frame(pos: 0.25f, brake: 0.0f, throttle: 1.0f, speed: 55f, steer: 0.0f),
            Frame(pos: 0.50f, brake: 0.0f, throttle: 1.0f, speed: 50f, steer: 0.0f),
        ];

        CornerMetrics metrics = ThrottleSpeedKernels.Analyze(monotonic);

        metrics.HasInSpanMinimum.Should().BeFalse("the minimum sits on the window endpoint, not strictly inside");
    }

    [Fact]
    public void Full_throttle_lap_returns_sentinels_without_throwing()
    {
        TelemetryFrame[] noBraking =
        [
            Frame(pos: 0.0f, brake: 0.0f, throttle: 1.0f, speed: 70f, steer: 0.0f),
            Frame(pos: 0.5f, brake: 0.0f, throttle: 1.0f, speed: 72f, steer: 0.0f),
        ];

        BrakeProfile brake = BrakeKernels.Analyze(noBraking);
        CornerMetrics metrics = ThrottleSpeedKernels.Analyze(noBraking);

        brake.PeakBrakePct.Should().Be(0f);
        brake.BrakeOnPosition.Should().BeNull();
        brake.BrakeOffPosition.Should().BeNull();
        brake.TrailBrakePct.Should().Be(0f);
        metrics.MinSpeedMps.Should().Be(70f);
        metrics.ThrottleOnPosition.Should().BeApproximately(0f, 1e-4f, "throttle is already open at the start");
        metrics.HasInSpanMinimum.Should().BeFalse("a full-throttle window has no deceleration apex");
    }

    [Fact]
    public void Balance_scores_separate_understeer_from_oversteer()
    {
        // Steady-state frames (no brake, no long-g). Scores are the scale-free asymmetry ratio
        // |front − rear| / (front + rear): understeer front 0.4 / rear 0.1 → 0.3/0.5 = 0.6;
        // oversteer front 0.1 / rear 0.5 → 0.4/0.6 ≈ 0.6667. Both land in [0,1]. Each window carries
        // MinSteadyStateFrames identical frames so it clears the min-sample guard; the mean is unchanged.
        TelemetryFrame[] understeer =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
        ];
        TelemetryFrame[] oversteer =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
            FrameWithSlip(steer: 0.3f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
            FrameWithSlip(steer: 0.3f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
        ];

        BalanceScores under = BalanceKernels.Analyze(understeer);
        BalanceScores over = BalanceKernels.Analyze(oversteer);

        under.UndersteerScore.Should().BeApproximately(0.6f, 1e-4f);
        under.UndersteerScore.Should().BeInRange(0f, 1f);
        under.OversteerScore.Should().Be(0f);
        over.OversteerScore.Should().BeApproximately(0.6667f, 1e-4f);
        over.OversteerScore.Should().BeInRange(0f, 1f);
        over.UndersteerScore.Should().Be(0f);
    }

    [Fact]
    public void Balance_ignores_braking_frames_so_load_transfer_is_not_read_as_understeer()
    {
        // SIS#9 regression: under braking the front axle carries transfer load and slips more, so a
        // neutral car used to read as understeer. A heavy-braking front>rear frame must now be gated
        // out (steady-state only) and contribute nothing — UndersteerScore == 0, not a positive score.
        TelemetryFrame[] braking =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f, brake: 0.8f, longG: 1.2f),
        ];

        BalanceKernels.Analyze(braking)
            .Should().Be(new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f });
    }

    [Fact]
    public void Balance_all_braking_window_scores_zero()
    {
        TelemetryFrame[] allBraking =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f, brake: 0.6f, longG: 0.9f),
            FrameWithSlip(steer: 0.3f, fl: 0.5f, fr: 0.5f, rl: 0.1f, rr: 0.1f, brake: 0.4f, longG: 0.7f),
        ];

        BalanceKernels.Analyze(allBraking)
            .Should().Be(new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f });
    }

    [Fact]
    public void Balance_gated_frame_is_excluded_from_the_denominator_not_averaged_in()
    {
        // Denominator invariant: a gated (heavy-braking, front>rear) frame must be excluded from BOTH the
        // numerator AND the running frame count — never counted as a zero-contribution sample that dilutes
        // the mean. Three steady 0.4/0.1 frames (ratio 0.6 each) plus one gated braking frame → the score
        // is the steady mean 0.6, not 1.8/4 = 0.45. A regression that keeps the gated frame in the
        // denominator would halve toward 0.45 and fail here.
        TelemetryFrame[] mixed =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.9f, fr: 0.9f, rl: 0.1f, rr: 0.1f, brake: 0.8f, longG: 1.2f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
        ];

        BalanceScores score = BalanceKernels.Analyze(mixed);

        score.UndersteerScore.Should().BeApproximately(0.6f, 1e-4f);
        score.OversteerScore.Should().Be(0f);
    }

    [Fact]
    public void Balance_below_min_steady_frames_scores_zero_but_scores_at_the_threshold()
    {
        // Min-sample guard: one surviving steady-state frame rides on sampling noise, so the window is too
        // sparse to score → {0,0}. The same fixture repeated to the guard threshold yields the real ratio,
        // proving the guard damps single-frame noise without altering the ratio formula.
        static TelemetryFrame OneFrame() => FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f);

        BalanceKernels.Analyze([OneFrame()])
            .Should().Be(new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f });

        BalanceKernels.Analyze([OneFrame(), OneFrame(), OneFrame()])
            .UndersteerScore.Should().BeApproximately(0.6f, 1e-4f);
    }

    [Fact]
    public void Balance_score_is_bounded_by_one_for_an_extreme_raw_delta()
    {
        // Raw slip range is ~0..12.37; a pathological front 12 / rear 0 frame used to fold a huge raw
        // delta into the score. The scale-free ratio caps it: |12 − 0| / (12 + 0) = 1. Repeated to clear
        // the min-sample guard without changing the ratio.
        TelemetryFrame[] extreme =
        [
            FrameWithSlip(steer: 0.3f, fl: 12f, fr: 12f, rl: 0f, rr: 0f),
            FrameWithSlip(steer: 0.3f, fl: 12f, fr: 12f, rl: 0f, rr: 0f),
            FrameWithSlip(steer: 0.3f, fl: 12f, fr: 12f, rl: 0f, rr: 0f),
        ];

        BalanceScores score = BalanceKernels.Analyze(extreme);

        score.UndersteerScore.Should().BeLessThanOrEqualTo(1f);
        score.UndersteerScore.Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Balance_long_g_gate_degrades_to_brake_only_when_g_force_absent()
    {
        // ACC omits g-force (null vector). Steady-state (no-brake) frames must still score even
        // though the long-g clause has no data — the gate degrades to brake-only, not all-zero.
        // MinSteadyStateFrames identical frames clear the min-sample guard; the mean is unchanged.
        TelemetryFrame[] noGForce =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
        ];

        BalanceKernels.Analyze(noGForce).UndersteerScore.Should().BeApproximately(0.6f, 1e-4f);
    }

    [Fact]
    public void Balance_scores_zero_when_no_slip_channels_present()
    {
        // The synthetic fixture sets no wheel_slip — balance must degrade to zero, not throw.
        TelemetryFrame[] noSlip = [Frame(pos: 0.2f, brake: 0.5f, throttle: 0f, speed: 30f, steer: 0.3f)];

        BalanceKernels.Analyze(noSlip).Should().Be(new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f });
    }

    private static TelemetryFrame Frame(float pos, float brake, float throttle, float speed, float steer) => new()
    {
        NormalizedCarPosition = pos,
        BrakePct = brake,
        ThrottlePct = throttle,
        SpeedMps = speed,
        SteerRad = steer,
    };

    private static TelemetryFrame FrameWithSlip(float steer, float fl, float fr, float rl, float rr)
    {
        TelemetryFrame frame = new() { SteerRad = steer };
        frame.WheelSlip.AddRange([fl, fr, rl, rr]);
        return frame;
    }

    private static TelemetryFrame FrameWithSlip(
        float steer, float fl, float fr, float rl, float rr, float brake, float longG)
    {
        TelemetryFrame frame = new()
        {
            SteerRad = steer,
            BrakePct = brake,
            GForceG = new Vec3 { Z = longG },
        };
        frame.WheelSlip.AddRange([fl, fr, rl, rr]);
        return frame;
    }
}
