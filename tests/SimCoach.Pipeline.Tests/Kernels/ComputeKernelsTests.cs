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
    public void Corner_metrics_report_min_speed_and_throttle_resume()
    {
        CornerMetrics metrics = ThrottleSpeedKernels.Analyze(_brakingCorner);

        metrics.MinSpeedMps.Should().BeApproximately(20f, 1e-4f);
        metrics.MinSpeedPosition.Should().BeApproximately(0.30f, 1e-4f);
        metrics.ThrottleOnPosition.Should().BeApproximately(0.40f, 1e-4f);
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
    }

    [Fact]
    public void Balance_scores_separate_understeer_from_oversteer()
    {
        TelemetryFrame[] understeer =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
            FrameWithSlip(steer: 0.3f, fl: 0.4f, fr: 0.4f, rl: 0.1f, rr: 0.1f),
        ];
        TelemetryFrame[] oversteer =
        [
            FrameWithSlip(steer: 0.3f, fl: 0.1f, fr: 0.1f, rl: 0.5f, rr: 0.5f),
        ];

        BalanceScores under = BalanceKernels.Analyze(understeer);
        BalanceScores over = BalanceKernels.Analyze(oversteer);

        under.UndersteerScore.Should().BeApproximately(0.3f, 1e-4f);
        under.OversteerScore.Should().Be(0f);
        over.OversteerScore.Should().BeApproximately(0.4f, 1e-4f);
        over.UndersteerScore.Should().Be(0f);
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
}
