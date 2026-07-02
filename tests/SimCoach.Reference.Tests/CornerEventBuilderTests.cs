using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerEventBuilderTests
{
    private const float LapLengthM = 1000f;
    private const int GridLength = 101;
    private const float BrakeWindowUpstreamM = 300f;

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
            CornerEventBuilder.Build(_corner, self, reference, LapLengthM, GridLength, BrakeWindowUpstreamM);

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

        (CornerEvent ev, _) = CornerEventBuilder.Build(_corner, self, reference, LapLengthM, GridLength, BrakeWindowUpstreamM);

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
            CornerEventBuilder.Build(_corner, self, reference: null, LapLengthM, gridLength: 0, BrakeWindowUpstreamM);

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
            CornerEventBuilder.Build(_corner, self, ReferenceGrid(), LapLengthM, GridLength, BrakeWindowUpstreamM);

        ev.OffTrack.Should().BeTrue();
        contribution.Reason.Should().Be("off_track");
        ev.Reason.Should().Be("off_track");
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
            CornerEventBuilder.Build(_corner, full, reference, LapLengthM, GridLength, BrakeWindowUpstreamM);
        (CornerEvent spanOnly, _) =
            CornerEventBuilder.Build(_corner, inSpanOnly, reference, LapLengthM, GridLength, BrakeWindowUpstreamM);

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
