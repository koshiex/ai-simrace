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

    [Theory]
    // Locked front, no ABS: front slip saturates the lock band (−0.5) with the brake hard on → full score.
    [InlineData("locked_front_no_abs", 0.95f, 1.0f)]
    // Same lock, but ABS engaged on the peak frame → discounted to raw × 0.35, present but far below a true lockup.
    [InlineData("abs_modulated", 0.25f, 0.45f)]
    // Exit wheelspin: positive front slip on throttle, brake off → not a lockup at all → 0.
    [InlineData("exit_wheelspin", 0.0f, 0.0f)]
    public void Brake_lockup_score_distinguishes_locked_front_from_abs_and_wheelspin(string scenario, float lo, float hi)
    {
        TelemetryFrame[] window = scenario switch
        {
            "locked_front_no_abs" => [FrameLock(frontSlip: -0.1f, brake: 0.9f, abs: false), FrameLock(frontSlip: -0.5f, brake: 0.9f, abs: false)],
            "abs_modulated" => [FrameLock(frontSlip: -0.1f, brake: 0.9f, abs: true), FrameLock(frontSlip: -0.5f, brake: 0.9f, abs: true)],
            // Front rolling free (+slip), rear spinning under power, brake released — nothing to lock.
            "exit_wheelspin" => [FrameLock(frontSlip: 0.02f, brake: 0f, abs: false, rearSlip: 0.5f, throttle: 0.9f)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        BrakeLockupKernels.BrakeLockupScore(window).Should().BeInRange(lo, hi);
    }

    [Fact]
    public void Brake_lockup_score_ignores_light_brake_and_missing_slip_channel()
    {
        // A deep lock but the brake barely applied (coasting) must not read as a braking lockup.
        BrakeLockupKernels.BrakeLockupScore([FrameLock(frontSlip: -0.6f, brake: 0.1f, abs: false)]).Should().Be(0f);
        // No slip_ratio channel at all → 0, never a throw.
        BrakeLockupKernels.BrakeLockupScore([new TelemetryFrame { BrakePct = 0.9f }]).Should().Be(0f);
    }

    [Fact]
    public void Brake_lockup_score_abs_attenuates_below_the_same_unaided_lockup()
    {
        TelemetryFrame[] unaided = [FrameLock(frontSlip: -0.5f, brake: 0.9f, abs: false)];
        TelemetryFrame[] withAbs = [FrameLock(frontSlip: -0.5f, brake: 0.9f, abs: true)];

        float open = BrakeLockupKernels.BrakeLockupScore(unaided);
        float aided = BrakeLockupKernels.BrakeLockupScore(withAbs);

        aided.Should().BeLessThan(open, "an ABS-equipped GT3 rarely fully locks — the reading is discounted");
        aided.Should().BeGreaterThan(0f, "but ABS can still be overwhelmed, so it is not zeroed");
    }

    [Theory]
    // Upshift taken near the rev ceiling (peak 7000, shifts at 6800) → inside the power band → ~0.
    [InlineData("power_band", 6800, 0.0f, 0.05f)]
    // Same 7000 ceiling, but the driver upshifts at 4200 rpm — well below the band → high short-shift score.
    [InlineData("short_shift", 4200, 0.7f, 1.0f)]
    public void Short_shift_score_flags_upshift_below_the_power_band(string scenario, int shiftRpm, float lo, float hi)
    {
        _ = scenario;
        // A common 7000 rpm ceiling is reached on entry, then the graded upshift out of the corner.
        TelemetryFrame[] window =
        [
            FrameShift(gear: 4, rpm: 7000),      // entry: engine revved to the ceiling before braking
            FrameShift(gear: 2, rpm: 3200),      // downshifted for the corner (ignored — not an upshift)
            FrameShift(gear: 2, rpm: shiftRpm),  // accelerating in-gear up to the chosen shift point
            FrameShift(gear: 3, rpm: 3000),      // UPSHIFT here → pre-shift rpm is the frame above
        ];

        ShortShiftKernels.ShortShiftScore(window).Should().BeInRange(lo, hi);
    }

    [Fact]
    public void Short_shift_score_ignores_downshifts_neutral_pull_away_and_missing_rpm()
    {
        // A pure downshift chain (braking into the corner) is never a short-shift.
        ShortShiftKernels.ShortShiftScore([FrameShift(gear: 5, rpm: 7000), FrameShift(gear: 3, rpm: 5000)]).Should().Be(0f);
        // Pulling away out of neutral (0 → 1) is not an upshift below the power band.
        ShortShiftKernels.ShortShiftScore([FrameShift(gear: 0, rpm: 1200), FrameShift(gear: 1, rpm: 3000)]).Should().Be(0f);
        // No rpm data at all → 0, never a throw.
        ShortShiftKernels.ShortShiftScore([new TelemetryFrame { Gear = 3 }, new TelemetryFrame { Gear = 4 }]).Should().Be(0f);
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

    // Thermal bands/exposure mirror the ComputeOptions defaults (the kernel itself holds no constants).
    private const float TyreBandC = 110f;
    private const float BrakeBandC = 800f;
    private const float MinOverheatFraction = 0.02f;

    private static ThermalResult AnalyzeThermal(params TelemetryFrame[] frames) =>
        ThermalKernels.Analyze(frames, TyreBandC, BrakeBandC, MinOverheatFraction);

    [Fact]
    public void Thermal_reports_peaks_and_flags_sustained_overheat()
    {
        // Half the lap above both bands — genuinely sustained abuse, well past the 2% exposure floor.
        TelemetryFrame[] hot =
        [
            FrameWithTemps(tyre: 95f, brake: 400f),
            FrameWithTemps(tyre: 120f, brake: 850f),
        ];

        ThermalResult thermal = AnalyzeThermal(hot);

        thermal.MaxTyreTempC.Should().BeApproximately(120f, 1e-4f);
        thermal.MaxBrakeTempC.Should().BeApproximately(850f, 1e-4f);
        thermal.TyreOverheat.Should().BeTrue();
        thermal.BrakeOverheat.Should().BeTrue();
    }

    [Fact]
    public void Thermal_transient_spike_reports_the_peak_but_does_not_flag_overheat()
    {
        // Regression: one hard stop legitimately spikes a GT3 disc for a few frames. Measured in the wild —
        // 701 °C for 67 ms (0.015% of the lap, median 414 °C) — and the old peak-crossed-once rule announced
        // "brakes overheated" while the HUD read cold. One frame in 200 (0.5%) is under the 2% floor.
        TelemetryFrame[] lap = [.. Enumerable.Repeat(FrameWithTemps(tyre: 90f, brake: 420f), 199)];
        TelemetryFrame[] withSpike = [.. lap, FrameWithTemps(tyre: 90f, brake: 900f)];

        ThermalResult thermal = AnalyzeThermal(withSpike);

        thermal.MaxBrakeTempC.Should().BeApproximately(900f, 1e-4f, "the peak is still reported as a metric");
        thermal.BrakeOverheat.Should().BeFalse("a single-frame spike is not sustained overheating");
    }

    [Fact]
    public void Thermal_within_band_does_not_flag_overheat()
    {
        ThermalResult thermal = AnalyzeThermal(FrameWithTemps(tyre: 90f, brake: 300f));

        thermal.MaxTyreTempC.Should().BeApproximately(90f, 1e-4f);
        thermal.TyreOverheat.Should().BeFalse();
        thermal.BrakeOverheat.Should().BeFalse();
    }

    [Fact]
    public void Thermal_empty_temp_arrays_return_zero_without_throwing()
    {
        ThermalResult thermal = AnalyzeThermal(new TelemetryFrame { ThrottlePct = 1f });

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

    private static TelemetryFrame FrameLock(float frontSlip, float brake, bool abs, float rearSlip = 0f, float throttle = 0f)
    {
        TelemetryFrame frame = new() { BrakePct = brake, ThrottlePct = throttle, AbsActive = abs, Abs = abs ? 1 : 0 };
        frame.SlipRatio.AddRange([frontSlip, frontSlip, rearSlip, rearSlip]);
        return frame;
    }

    private static TelemetryFrame FrameShift(int gear, float rpm) => new() { Gear = gear, Rpm = rpm };

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
