using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerEventBuilderTests
{
    private const float LapLengthM = 1000f;
    private const int GridLength = 101;
    private const float BrakeWindowUpstreamM = 300f;
    private const double ApexWindowFraction = 0.25;

    private static readonly Corner _corner = new()
    {
        Id = "spa_t01",
        StartPosition = 0.30f,
        ApexPosition = 0.40f,
        EndPosition = 0.50f,
    };

    [Fact]
    public void A_slower_lap_yields_positive_delta_and_signed_diffs()
    {
        IReadOnlyList<TelemetryFrame> self = SlowerSelfCorner();
        ResampledLap reference = ReferenceGrid();

        (CornerEvent ev, CornerContribution contribution) =
            CornerEventBuilder.Build(_corner, self, reference, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        ev.DeltaMs.Should().BePositive("the self window takes longer than the reference window");
        ev.MinSpeedDiffKmh.Should().BeNegative("self min speed is lower");
        ev.ThrottleResumeDiffM.Should().BeNegative("self gets back on throttle later");
        ev.BrakePointDiffM.Should().BeNegative("self brakes earlier");
        ev.RacingLineDeviationM.Should().BePositive("self runs a different world line");
        contribution.DeltaMs.Should().Be(ev.DeltaMs);
        contribution.Reason.Should().NotBeNullOrEmpty();
        ev.Reason.Should().Be(contribution.Reason, "the event surfaces the same reason the contribution carries");
        // Self-derived B1 overlap is populated (constant 0.3 steer through the 0.8-brake phase).
        ev.BrakeOverlapSteerPct.Should().BePositive();
    }

    [Fact]
    public void A_flat_full_throttle_corner_yields_near_zero_delta_and_suppresses_min_speed()
    {
        // M2 regression guard: a flat full-throttle transit measured over the same [Start,End] span as
        // the (distinct) reference must report ~0 delta — not the phantom ≈ -refDuration a collapsed
        // throttle-resume stub produced. D-minspeed also silences its min-speed field.
        IReadOnlyList<TelemetryFrame> self = FlatFullThrottleCorner();
        ResampledLap reference = ReferenceGrid();

        (CornerEvent ev, _) = CornerEventBuilder.Build(_corner, self, reference, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        Math.Abs(ev.DeltaMs).Should().BeLessThanOrEqualTo(
            10, "self measures the full [Start,End] span, matching the reference span duration");
        ev.MinSpeedDiffKmh.Should().Be(
            0f, "a flat transit corner has no true in-span minimum → min-speed is suppressed (D-minspeed)");
    }

    [Fact]
    public void With_no_reference_only_self_fields_are_populated()
    {
        IReadOnlyList<TelemetryFrame> self = SlowerSelfCorner();

        (CornerEvent ev, CornerContribution contribution) =
            CornerEventBuilder.Build(_corner, self, reference: null, LapLengthM, gridLength: 0, BrakeWindowUpstreamM, ApexWindowFraction);

        ev.DeltaMs.Should().Be(0);
        ev.MinSpeedDiffKmh.Should().Be(0);
        ev.ThrottleResumeDiffM.Should().Be(0);
        ev.RacingLineDeviationM.Should().Be(0);
        ev.CornerId.Should().Be("spa_t01");
        ev.Reason.Should().BeEmpty("no reference and on-track → no quantifiable reason");
        contribution.DeltaMs.Should().Be(0, "no reference means no top-loss contribution");
    }

    [Fact]
    public void Off_track_frames_set_the_flag_and_reason()
    {
        List<TelemetryFrame> self = [.. SlowerSelfCorner()];
        self[5] = self[5].Clone();
        self[5].TyresOut = 3;

        (CornerEvent ev, CornerContribution contribution) =
            CornerEventBuilder.Build(_corner, self, ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        ev.OffTrack.Should().BeTrue();
        contribution.Reason.Should().Be("off_track");
        ev.Reason.Should().Be("off_track");
    }

    [Fact]
    public void Torn_or_missing_world_position_frames_are_skipped_and_do_not_inflate_line_deviation()
    {
        // M43: a frame whose WorldPos is null or the (0,0,0) honest-zero sentinel (slot out of range /
        // torn frame) must be skipped in the racing-line RMS. On an otherwise on-line lap, folding a (0,0)
        // origin in would add the car's full distance-to-track-origin (~hundreds of m) as a phantom line
        // error. FlatFullThrottleCorner rides exactly the reference world line, so a clean deviation is ~0.
        List<TelemetryFrame> self = [.. FlatFullThrottleCorner()];
        self[5] = self[5].Clone();
        self[5].WorldPos = new Vec3();   // (0,0,0) sentinel
        self[7] = self[7].Clone();
        self[7].WorldPos = null;          // missing world position

        (CornerEvent torn, _) =
            CornerEventBuilder.Build(_corner, self, ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);
        (CornerEvent clean, _) =
            CornerEventBuilder.Build(_corner, FlatFullThrottleCorner(), ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        torn.RacingLineDeviationM.Should().BeLessThan(
            1f, "the torn (0,0,0)/null frames are skipped, not folded into the RMS as a phantom origin distance");
        torn.RacingLineDeviationM.Should().BeApproximately(
            clean.RacingLineDeviationM, 0.01f, "skipping the torn frames leaves the deviation identical to the all-valid lap");
    }

    [Fact]
    public void Braking_upstream_of_the_start_is_captured_without_leaking_into_delta()
    {
        // M16: the real braking zone opens before the geometric corner start. With the tracker armed
        // upstream, the self buffer carries pre-roll frames; the brake-onset scan must read them (a
        // sign-correct, non-collapsed diff) while delta/min-speed stay on the [Start,End] sub-window.
        IReadOnlyList<TelemetryFrame> full = UpstreamBrakingSelfCorner(includePreRoll: true);
        IReadOnlyList<TelemetryFrame> inSpanOnly = UpstreamBrakingSelfCorner(includePreRoll: false);
        ResampledLap reference = UpstreamBrakingReference();

        (CornerEvent widened, _) =
            CornerEventBuilder.Build(_corner, full, reference, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);
        (CornerEvent spanOnly, _) =
            CornerEventBuilder.Build(_corner, inSpanOnly, reference, LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        // Self brakes at 0.24, reference at 0.27 → (0.24 - 0.27) * 1000 m ≈ -30 m (self braked earlier).
        // Without the upstream pre-roll the self onset would collapse to the corner start (fallback),
        // fabricating a +30 m diff — the strict-window bug M16 removes.
        widened.BrakePointDiffM.Should().BeApproximately(-30f, 1.5f);
        widened.BrakePointDiffM.Should().BeNegative("the upstream pre-roll exposes the real brake onset");
        spanOnly.BrakePointDiffM.Should().BeApproximately(
            30f, 1.5f, "dropping the pre-roll collapses the self onset back to the corner start");

        // M2 isolation regression: dropping the pre-roll frames leaves delta/min-speed identical — the
        // widened brake scan never leaks into the [Start,End] kernels.
        widened.DeltaMs.Should().Be(spanOnly.DeltaMs);
        widened.MinSpeedDiffKmh.Should().Be(spanOnly.MinSpeedDiffKmh);
        widened.MinSpeedDiffKmh.Should().BeNegative("self's in-span apex is slower than the reference apex");
    }

    [Fact]
    public void An_empty_in_span_self_window_yields_zero_delta_not_an_upstream_inflated_value()
    {
        // M2/M16 interaction guard: if no buffered self frame lands inside [Start,End] (a degenerate
        // window — here only upstream pre-roll frames survive), the self duration must NOT be measured
        // over the M16-widened buffer, which would inflate delta by the upstream travel time. The
        // self-side degenerate guard mirrors the reference branch and takes a self-only return (deltaMs=0).
        List<TelemetryFrame> upstreamOnly = [];
        for (int k = 0; k < 10; k++)
        {
            // 0.10..0.19 — all strictly upstream of the 0.30 corner start; none falls inside [Start,End].
            float pos = 0.10f + (0.01f * k);
            upstreamOnly.Add(Frame(pos, speed: 50f, brake: 0f, throttle: 0f, tMs: 50 * k, worldX: pos * LapLengthM));
        }

        (CornerEvent ev, CornerContribution contribution) =
            CornerEventBuilder.Build(_corner, upstreamOnly, ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        ev.DeltaMs.Should().Be(
            0, "no self frame lands inside [Start,End] → self-only fallback, never an upstream-inflated delta");
        contribution.DeltaMs.Should().Be(0, "the degenerate window contributes nothing to top-losses");
    }

    [Fact]
    public void M9_phase_scopes_overlap_to_the_turn_in_apex_band_silencing_a_straight_line_chicane()
    {
        // A braking-chicane / straight-line approach whose brake+steer overlap sits ENTIRELY outside the
        // turn-in→apex band (in the approach [0.30,0.33] and the exit [0.43,0.50]). Whole-window scoring
        // (the pre-M9 metric) is high and would trip straighter_braking; the phase-scoped metric is ~0.
        IReadOnlyList<TelemetryFrame> self = StraightLineChicaneCorner();

        float wholeWindow = BrakeOverlapSteerKernels.OverlapPct(self);
        (CornerEvent ev, _) =
            CornerEventBuilder.Build(_corner, self, reference: null, LapLengthM, gridLength: 0, BrakeWindowUpstreamM, ApexWindowFraction);

        wholeWindow.Should().BeGreaterThan(0.5f, "the whole-window fraction (pre-M9) is high — the mis-fire source");
        ev.BrakeOverlapSteerPct.Should().Be(
            0f, "no brake+steer overlap lands inside the turn-in→apex band → phase-scoped metric is 0 (requires_reference:false path still computes)");
    }

    [Fact]
    public void M9_a_genuine_sustained_brake_into_apex_still_trips_the_overlap()
    {
        // The slower-self corner trail-brakes (0.8 brake) all the way from turn-in through the apex with a
        // constant steer load — genuine over-braking the tip SHOULD flag. The phase-scoped fraction stays
        // above the recalibrated 0.5 registry threshold.
        IReadOnlyList<TelemetryFrame> self = SlowerSelfCorner();

        (CornerEvent ev, _) =
            CornerEventBuilder.Build(_corner, self, ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM, ApexWindowFraction);

        ev.BrakeOverlapSteerPct.Should().BeGreaterThan(
            0.5f, "sustained brake+steer through the turn-in→apex band survives the recalibrated threshold");
    }

    // Self corner [0.30..0.50] whose brake+steer overlap is confined to the straight-line approach
    // ([0.30,0.33]) and the exit ([0.43,0.50]) — both OUTSIDE the turn-in→apex band [0.3375,0.425].
    // Constant 0.3 steer (Frame default), so overlap tracks the brake trace only.
    private static IReadOnlyList<TelemetryFrame> StraightLineChicaneCorner()
    {
        List<TelemetryFrame> frames = [];
        for (int k = 0; k <= 20; k++)
        {
            float pos = 0.30f + (0.01f * k);
            float brake = pos <= 0.33f || pos >= 0.43f ? 0.8f : 0f;
            frames.Add(Frame(pos, speed: 50f, brake, throttle: 0f, tMs: 10 * k, worldX: pos * LapLengthM));
        }

        return frames;
    }

    // Self corner [0.30..0.50]: brakes earlier (0.31), slower apex (30 m/s), later throttle (0.48),
    // and longer in time (15 ms/frame vs the reference's 10 ms grid) — every diff sign is exercised.
    private static IReadOnlyList<TelemetryFrame> SlowerSelfCorner()
    {
        List<TelemetryFrame> frames = [];
        for (int k = 0; k <= 20; k++)
        {
            float pos = 0.30f + (0.01f * k);
            float speed = pos <= 0.40f ? Lerp(60f, 30f, Frac(0.30f, 0.40f, pos)) : Lerp(30f, 60f, Frac(0.40f, 0.50f, pos));
            float brake = pos is >= 0.31f and <= 0.40f ? 0.8f : 0f;
            float throttle = pos >= 0.48f ? 0.8f : 0f;
            frames.Add(Frame(pos, speed, brake, throttle, tMs: 15 * k, worldX: (pos * LapLengthM) + 2f));
        }

        return frames;
    }

    // Flat self corner [0.30..0.50], constant 60 m/s at full throttle, 10 ms/frame — the same span
    // duration (200 ms) the reference grid slice covers, so the span-aligned delta is ~0.
    private static IReadOnlyList<TelemetryFrame> FlatFullThrottleCorner()
    {
        List<TelemetryFrame> frames = [];
        for (int k = 0; k <= 20; k++)
        {
            float pos = 0.30f + (0.01f * k);
            frames.Add(Frame(pos, speed: 60f, brake: 0f, throttle: 1.0f, tMs: 10 * k, worldX: pos * LapLengthM));
        }

        return frames;
    }

    // Self corner covering [0.20..0.50] whose braking zone (0.24..0.28) opens upstream of the 0.30 start
    // and whose apex minimum (30 m/s at 0.40) sits strictly inside [Start,End]. tMs keys off the absolute
    // grid index so the in-span frames carry identical timestamps whether or not the pre-roll is included.
    private static IReadOnlyList<TelemetryFrame> UpstreamBrakingSelfCorner(bool includePreRoll)
    {
        List<TelemetryFrame> frames = [];
        for (int k = 0; k <= 30; k++)
        {
            float pos = 0.20f + (0.01f * k);
            if (!includePreRoll && pos < _corner.StartPosition)
            {
                continue;
            }

            float speed = pos <= 0.40f ? Lerp(60f, 30f, Frac(0.20f, 0.40f, pos)) : Lerp(30f, 60f, Frac(0.40f, 0.50f, pos));
            float brake = pos is >= 0.24f and <= 0.28f ? 0.8f : 0f;
            float throttle = pos >= 0.45f ? 0.8f : 0f;
            frames.Add(Frame(pos, speed, brake, throttle, tMs: 15 * k, worldX: pos * LapLengthM));
        }

        return frames;
    }

    // Reference whose braking zone (0.27..0.29) is entirely upstream of the 0.30 start — so a strict
    // [Start,End] scan finds no onset, exactly the collapse M16 targets — with a slower apex (40 m/s).
    private static ResampledLap UpstreamBrakingReference() => ReferenceGrid(0.27f, 0.29f);

    private static ResampledLap ReferenceGrid() => ReferenceGrid(0.33f, 0.40f);

    private static ResampledLap ReferenceGrid(float brakeFrom, float brakeTo)
    {
        float[] position = new float[GridLength];
        int[] tMs = new int[GridLength];
        float[] speed = new float[GridLength];
        float[] brake = new float[GridLength];
        float[] throttle = new float[GridLength];
        float[] worldX = new float[GridLength];
        for (int k = 0; k < GridLength; k++)
        {
            float pos = k / 100f;
            position[k] = pos;
            tMs[k] = k * 10;
            speed[k] = pos is >= 0.30f and <= 0.40f ? Lerp(60f, 40f, Frac(0.30f, 0.40f, pos))
                : pos is > 0.40f and <= 0.50f ? Lerp(40f, 60f, Frac(0.40f, 0.50f, pos))
                : 60f;
            brake[k] = pos >= brakeFrom && pos <= brakeTo ? 0.8f : 0f;
            throttle[k] = pos >= 0.45f ? 0.8f : 0f;
            worldX[k] = pos * LapLengthM;
        }

        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = GridLength,
            PositionNormalized = position,
            TMsFromLapStart = tMs,
            SpeedMps = speed,
            ThrottlePct = throttle,
            BrakePct = brake,
            SteerRad = new float[GridLength],
            Gear = new int[GridLength],
            TyreTempFl = new float[GridLength],
            TyreTempFr = new float[GridLength],
            TyreTempRl = new float[GridLength],
            TyreTempRr = new float[GridLength],
            GLat = new float[GridLength],
            GLong = new float[GridLength],
            WorldX = worldX,
            WorldY = new float[GridLength],
            WorldZ = new float[GridLength],
        };
    }

    private static TelemetryFrame Frame(float pos, float speed, float brake, float throttle, double tMs, float worldX) =>
        new()
        {
            T = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddMilliseconds(tMs)),
            NormalizedCarPosition = pos,
            SpeedMps = speed,
            BrakePct = brake,
            ThrottlePct = throttle,
            SteerRad = 0.3f,
            WorldPos = new Vec3 { X = worldX, Y = 0f, Z = 0f },
            IsValidLap = true,
        };

    private static float Frac(float from, float to, float value) => Math.Clamp((value - from) / (to - from), 0f, 1f);

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
