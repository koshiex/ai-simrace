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
            CornerEventBuilder.Build(_corner, self, reference, LapLengthM, GridLength);

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
    public void With_no_reference_only_self_fields_are_populated()
    {
        IReadOnlyList<TelemetryFrame> self = SlowerSelfCorner();

        (CornerEvent ev, CornerContribution contribution) =
            CornerEventBuilder.Build(_corner, self, reference: null, LapLengthM, gridLength: 0);

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
            CornerEventBuilder.Build(_corner, self, ReferenceGrid(), LapLengthM, GridLength);

        ev.OffTrack.Should().BeTrue();
        contribution.Reason.Should().Be("off_track");
        ev.Reason.Should().Be("off_track");
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

    private static ResampledLap ReferenceGrid()
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
            brake[k] = pos is >= 0.33f and <= 0.40f ? 0.8f : 0f;
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
