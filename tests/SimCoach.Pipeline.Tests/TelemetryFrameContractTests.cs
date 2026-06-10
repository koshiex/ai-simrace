using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Pipeline.Tests;

/// <summary>
/// Smoke tests proving the protobuf codegen pipeline (Grpc.Tools → C#) is wired correctly.
/// </summary>
public sealed class TelemetryFrameContractTests
{
    [Fact]
    public void TelemetryFrame_roundtrips_through_binary_serialization()
    {
        // Arrange
        TelemetryFrame original = new()
        {
            T = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(1_750_000_000_000)),
            Sim = "acc",
            TrackId = "spa",
            CarId = "audi_r8_lms_evo_ii",
            WeatherBucket = "dry-warm",
            LapNumber = 3,
            LapDistanceM = 1234.5f,
            NormalizedCarPosition = 0.42f,
            SpeedMps = 61.1f,
            ThrottlePct = 0.8f,
            BrakePct = 0f,
            Gear = 4,
            Rpm = 7200f,
            GForceG = new Vec3 { X = 1.2f, Y = 0.1f, Z = -0.4f },
        };
        original.TyreTempC.AddRange([82.1f, 83.0f, 80.5f, 81.2f]);

        // Act
        byte[] bytes = original.ToByteArray();
        TelemetryFrame parsed = TelemetryFrame.Parser.ParseFrom(bytes);

        // Assert
        parsed.Should().Be(original);
        parsed.TyreTempC.Should().HaveCount(4);
    }

    [Fact]
    public void LapEvent_with_nested_corner_losses_roundtrips()
    {
        // Arrange
        LapEvent original = new()
        {
            LapNumber = 7,
            LapTimeMs = 138_456,
            DeltaMs = 312,
            IsPb = false,
            IsClean = true,
        };
        original.TopLosses.Add(new CornerLoss { CornerId = "spa_t05_eau_rouge", DeltaMs = 180, Reason = "early_brake" });

        // Act
        LapEvent parsed = LapEvent.Parser.ParseFrom(original.ToByteArray());

        // Assert
        parsed.Should().Be(original);
        parsed.TopLosses.Should().ContainSingle(loss => loss.CornerId == "spa_t05_eau_rouge");
    }
}
