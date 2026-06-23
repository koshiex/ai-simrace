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

        ResampledLap resampled = PositionResampler.Resample(lap, SyntheticTracks.Spa.LapLengthM);

        resampled.GridLength.Should().Be((int)MathF.Ceiling(SyntheticTracks.Spa.LapLengthM));
        resampled.PositionNormalized.Should().HaveCount(resampled.GridLength);
        resampled.PositionNormalized.Should().BeInAscendingOrder();
        resampled.PositionNormalized[0].Should().BeApproximately(0f, 1e-4f);
        resampled.TMsFromLapStart[0].Should().Be(0);
        resampled.TMsFromLapStart.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Carries_world_coordinates_onto_the_grid()
    {
        IReadOnlyList<TelemetryFrame> lap = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1);

        ResampledLap resampled = PositionResampler.Resample(lap, SyntheticTracks.Spa.LapLengthM);

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

        Action resample = () => PositionResampler.Resample(frames, 2000f);

        resample.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Requires_at_least_two_frames()
    {
        TelemetryFrame[] frames = [Frame(0.1f, 0)];

        Action resample = () => PositionResampler.Resample(frames, 2000f);

        resample.Should().Throw<ArgumentException>();
    }

    private static TelemetryFrame Frame(float position, int tMs) => new()
    {
        T = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(tMs)),
        NormalizedCarPosition = position,
    };
}
