using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using Xunit;

namespace SimCoach.Pipeline.Tests.Kernels;

public sealed class Phase3KernelsTests
{
    [Fact]
    public void Wheelspin_score_normalizes_rear_slip_between_onset_and_saturation()
    {
        // Rear slip peaks at 0.25 → (0.25 − 0.10) / (0.40 − 0.10) = 0.5.
        TelemetryFrame[] window =
        [
            FrameWithSlip(0.02f, 0.02f, 0.10f, 0.10f),
            FrameWithSlip(0.02f, 0.02f, 0.25f, 0.20f),
        ];

        WheelspinKernels.WheelspinScore(window).Should().BeApproximately(0.5f, 1e-4f);
    }

    [Fact]
    public void Wheelspin_score_saturates_and_floors()
    {
        WheelspinKernels.WheelspinScore([FrameWithSlip(0f, 0f, 0.9f, 0.9f)]).Should().Be(1f);
        WheelspinKernels.WheelspinScore([FrameWithSlip(0f, 0f, 0.05f, 0.05f)]).Should().Be(0f);
    }

    [Fact]
    public void Wheelspin_score_zero_when_no_slip_ratio_channel()
    {
        TelemetryFrame[] noSlip = [new() { ThrottlePct = 1f }];

        WheelspinKernels.WheelspinScore(noSlip).Should().Be(0f);
    }

    [Fact]
    public void Wheelspin_score_ignores_braking_lockup_and_off_throttle_frames()
    {
        // Braking-zone frames: throttle off, rear slip NEGATIVE (lockup). Must not score as wheelspin.
        TelemetryFrame[] braking =
        [
            FrameWithSlip(-0.4f, -0.4f, -0.5f, -0.5f, throttle: 0f),
            FrameWithSlip(-0.4f, -0.4f, -0.5f, -0.5f, throttle: 0f),
        ];

        WheelspinKernels.WheelspinScore(braking).Should().Be(0f);
    }

    [Fact]
    public void Wheelspin_score_uses_only_throttle_phase_positive_slip()
    {
        // Mixed window: big negative slip while braking (ignored), modest positive slip on throttle.
        TelemetryFrame[] mixed =
        [
            FrameWithSlip(-0.4f, -0.4f, -0.9f, -0.9f, throttle: 0f),   // lockup, off throttle → ignored
            FrameWithSlip(0.02f, 0.02f, 0.25f, 0.20f, throttle: 0.8f), // power-down wheelspin → 0.5
        ];

        WheelspinKernels.WheelspinScore(mixed).Should().BeApproximately(0.5f, 1e-4f);
    }

    [Fact]
    public void Brake_overlap_is_fraction_of_window_braking_while_steering()
    {
        // 2 of 4 frames carry both brake > 0.1 and |steer| > 0.1 → 0.5.
        TelemetryFrame[] window =
        [
            Frame(brake: 0.5f, steer: 0.3f),
            Frame(brake: 0.5f, steer: 0.0f),  // braking, not steering
            Frame(brake: 0.0f, steer: 0.3f),  // steering, not braking
            Frame(brake: 0.4f, steer: 0.2f),
        ];

        BrakeOverlapSteerKernels.OverlapPct(window).Should().BeApproximately(0.5f, 1e-4f);
    }

    [Fact]
    public void Brake_overlap_empty_window_returns_zero()
    {
        BrakeOverlapSteerKernels.OverlapPct([]).Should().Be(0f);
    }

    [Fact]
    public void Steering_jitter_zero_for_constant_steer_rate()
    {
        // Steer increases linearly at a constant 10 ms cadence → constant rate → zero variance.
        TelemetryFrame[] smooth =
        [
            FrameAt(ms: 0, steer: 0.0f),
            FrameAt(ms: 10, steer: 0.1f),
            FrameAt(ms: 20, steer: 0.2f),
            FrameAt(ms: 30, steer: 0.3f),
        ];

        SteeringJitterKernels.SteeringJitter(smooth).Should().BeApproximately(0f, 1e-3f);
    }

    [Fact]
    public void Steering_jitter_positive_for_busy_wheel()
    {
        TelemetryFrame[] busy =
        [
            FrameAt(ms: 0, steer: 0.0f),
            FrameAt(ms: 10, steer: 0.3f),
            FrameAt(ms: 20, steer: 0.05f),
            FrameAt(ms: 30, steer: 0.4f),
        ];

        SteeringJitterKernels.SteeringJitter(busy).Should().BeGreaterThan(1f);
    }

    [Fact]
    public void Steering_jitter_skips_duplicate_timestamps_without_nan()
    {
        TelemetryFrame[] duped =
        [
            FrameAt(ms: 0, steer: 0.0f),
            FrameAt(ms: 0, steer: 0.5f),   // dt == 0 → skipped
            FrameAt(ms: 0, steer: 0.9f),   // dt == 0 → skipped
        ];

        float jitter = SteeringJitterKernels.SteeringJitter(duped);

        float.IsNaN(jitter).Should().BeFalse();
        float.IsInfinity(jitter).Should().BeFalse();
        jitter.Should().Be(0f);
    }

    [Fact]
    public void Steering_jitter_single_frame_returns_zero()
    {
        SteeringJitterKernels.SteeringJitter([FrameAt(ms: 0, steer: 0.2f)]).Should().Be(0f);
    }

    [Fact]
    public void Thermal_reports_peaks_and_overheat_flags()
    {
        TelemetryFrame[] hot =
        [
            FrameWithTemps(tyre: 95f, brake: 400f),
            FrameWithTemps(tyre: 120f, brake: 750f),  // both over the abuse bands
        ];

        ThermalResult thermal = ThermalKernels.Analyze(hot);

        thermal.MaxTyreTempC.Should().BeApproximately(120f, 1e-4f);
        thermal.MaxBrakeTempC.Should().BeApproximately(750f, 1e-4f);
        thermal.TyreOverheat.Should().BeTrue();
        thermal.BrakeOverheat.Should().BeTrue();
    }

    [Fact]
    public void Thermal_within_band_does_not_flag_overheat()
    {
        ThermalResult thermal = ThermalKernels.Analyze([FrameWithTemps(tyre: 90f, brake: 300f)]);

        thermal.MaxTyreTempC.Should().BeApproximately(90f, 1e-4f);
        thermal.TyreOverheat.Should().BeFalse();
        thermal.BrakeOverheat.Should().BeFalse();
    }

    [Fact]
    public void Thermal_empty_temp_arrays_return_zero_without_throwing()
    {
        TelemetryFrame[] noTemps = [new() { ThrottlePct = 1f }];

        ThermalResult thermal = ThermalKernels.Analyze(noTemps);

        thermal.Should().Be(new ThermalResult
        {
            MaxTyreTempC = 0f,
            MaxBrakeTempC = 0f,
            TyreOverheat = false,
            BrakeOverheat = false,
        });
    }

    private static TelemetryFrame Frame(float brake, float steer) => new() { BrakePct = brake, SteerRad = steer };

    private static TelemetryFrame FrameAt(int ms, float steer) => new()
    {
        T = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(ms)),
        SteerRad = steer,
    };

    private static TelemetryFrame FrameWithSlip(float fl, float fr, float rl, float rr, float throttle = 1f)
    {
        TelemetryFrame frame = new() { ThrottlePct = throttle };
        frame.SlipRatio.AddRange([fl, fr, rl, rr]);
        return frame;
    }

    private static TelemetryFrame FrameWithTemps(float tyre, float brake)
    {
        TelemetryFrame frame = new();
        frame.TyreTempC.AddRange([tyre, tyre, tyre, tyre]);
        frame.BrakeTempC.AddRange([brake, brake, brake, brake]);
        return frame;
    }
}
