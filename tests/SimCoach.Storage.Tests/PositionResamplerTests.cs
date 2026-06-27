using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class PositionResamplerTests
{
    [Fact]
    public void Produces_a_fixed_length_monotonic_grid()
    {
        IReadOnlyList<TelemetryFrame> lap = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1);

        ResampledLap resampled = PositionResampler.Resample(lap, SyntheticTracks.Spa.LapLengthM, lapNumber: 5);

        resampled.GridLength.Should().Be((int)MathF.Ceiling(SyntheticTracks.Spa.LapLengthM));
        resampled.PositionNormalized.Should().HaveCount(resampled.GridLength);
        resampled.PositionNormalized.Should().BeInAscendingOrder();
        resampled.PositionNormalized[0].Should().BeApproximately(0f, 1e-4f);
        resampled.TMsFromLapStart[0].Should().Be(0);
        resampled.TMsFromLapStart.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Labels_the_lap_with_the_supplied_number_not_the_frame_counter()
    {
        // The frames carry the sim's raw counter (1); the resampler must use the explicit session-local
        // number it is handed, so laps.parquet stays joinable to the renumbered laps rows.
        IReadOnlyList<TelemetryFrame> lap = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1);

        ResampledLap resampled = PositionResampler.Resample(lap, SyntheticTracks.Spa.LapLengthM, lapNumber: 42);

        resampled.LapNumber.Should().Be(42);
    }

    [Fact]
    public void Carries_world_coordinates_onto_the_grid()
    {
        IReadOnlyList<TelemetryFrame> lap = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1);

        ResampledLap resampled = PositionResampler.Resample(lap, SyntheticTracks.Spa.LapLengthM, lapNumber: 1);

        // Synthetic world_pos traces a circle of radius lapLength/2π (~1114 m) — far from zero.
        resampled.WorldX.Max(MathF.Abs).Should().BeGreaterThan(1f);
        resampled.WorldZ.Max(MathF.Abs).Should().BeGreaterThan(1f);
    }

    [Fact]
    public void Rejects_non_monotonic_position()
    {
        TelemetryFrame[] frames =
        [
            Frame(0.10f, 0),
            Frame(0.50f, 100),
            Frame(0.40f, 200), // backward step — a pit detour
        ];

        Action resample = () => PositionResampler.Resample(frames, 2000f, lapNumber: 1);

        resample.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Clamps_a_backward_step_when_asked_instead_of_throwing()
    {
        // The same backstep a strict resample rejects: a crash/spin lap bound for review (never a
        // reference) clamps it to the running max so the grid resamples without throwing.
        TelemetryFrame[] frames =
        [
            Frame(0.10f, 0),
            Frame(0.50f, 100),
            Frame(0.40f, 200), // backward step — clamped up to 0.50
            Frame(0.80f, 300),
        ];

        ResampledLap resampled = PositionResampler.Resample(frames, 100f, lapNumber: 1, clampNonMonotonic: true);

        resampled.GridLength.Should().Be(100);
        resampled.PositionNormalized.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Tolerates_consecutive_frames_at_an_identical_position()
    {
        // A stalled position (equal, not decreasing) must not trip the monotonic guard or divide by zero.
        TelemetryFrame[] frames =
        [
            Frame(0.20f, 0),
            Frame(0.20f, 50),
            Frame(0.60f, 150),
        ];

        ResampledLap resampled = PositionResampler.Resample(frames, 100f, lapNumber: 1);

        resampled.GridLength.Should().Be(100);
        resampled.PositionNormalized.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Requires_at_least_two_frames()
    {
        TelemetryFrame[] frames = [Frame(0.1f, 0)];

        Action resample = () => PositionResampler.Resample(frames, 2000f, lapNumber: 1);

        resample.Should().Throw<ArgumentException>();
    }

    private static TelemetryFrame Frame(float position, int tMs) => new()
    {
        T = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(tMs)),
        NormalizedCarPosition = position,
    };
}
