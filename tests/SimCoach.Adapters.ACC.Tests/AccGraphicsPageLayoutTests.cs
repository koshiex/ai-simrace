using System.Runtime.InteropServices;
using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Golden layout tests for <see cref="AccGraphicsPage"/> against the Kunos shared-memory
/// documentation V1.8.12 (SPageFileGraphic, pack 4, 1588 bytes). Pack=4 inserts 2 padding
/// bytes after each odd-length wchar_t array followed by an int/float — those spots are the
/// historically bug-prone offsets and are all asserted below.
/// </summary>
public sealed class AccGraphicsPageLayoutTests
{
    private const int DocumentedSizeBytes = 1588;

    [Fact]
    public void Graphics_page_size_matches_documented_native_layout()
    {
        // Act
        int marshaledSize = Marshal.SizeOf<AccGraphicsPage>();

        // Assert
        marshaledSize.Should().Be(DocumentedSizeBytes);
        AccGraphicsPage.SizeBytes.Should().Be(DocumentedSizeBytes);
    }

    [Theory]
    [InlineData(nameof(AccGraphicsPage.PacketId), 0)]
    [InlineData(nameof(AccGraphicsPage.Status), 4)]
    [InlineData(nameof(AccGraphicsPage.Session), 8)]
    [InlineData(nameof(AccGraphicsPage.CurrentTime), 12)]
    [InlineData(nameof(AccGraphicsPage.LastTime), 42)]
    [InlineData(nameof(AccGraphicsPage.BestTime), 72)]
    [InlineData(nameof(AccGraphicsPage.Split), 102)]
    [InlineData(nameof(AccGraphicsPage.CompletedLaps), 132)]
    [InlineData(nameof(AccGraphicsPage.Position), 136)]
    [InlineData(nameof(AccGraphicsPage.ICurrentTime), 140)]
    [InlineData(nameof(AccGraphicsPage.ILastTime), 144)]
    [InlineData(nameof(AccGraphicsPage.IBestTime), 148)]
    [InlineData(nameof(AccGraphicsPage.SessionTimeLeft), 152)]
    [InlineData(nameof(AccGraphicsPage.DistanceTraveled), 156)]
    [InlineData(nameof(AccGraphicsPage.IsInPit), 160)]
    [InlineData(nameof(AccGraphicsPage.CurrentSectorIndex), 164)]
    [InlineData(nameof(AccGraphicsPage.LastSectorTime), 168)]
    [InlineData(nameof(AccGraphicsPage.NumberOfLaps), 172)]
    [InlineData(nameof(AccGraphicsPage.TyreCompound), 176)]
    [InlineData(nameof(AccGraphicsPage.ReplayTimeMultiplier), 244)] // 2-byte pad after tyreCompound[33]
    [InlineData(nameof(AccGraphicsPage.NormalizedCarPosition), 248)]
    [InlineData(nameof(AccGraphicsPage.ActiveCars), 252)]
    [InlineData(nameof(AccGraphicsPage.CarCoordinates), 256)]
    [InlineData(nameof(AccGraphicsPage.CarId), 976)]
    [InlineData(nameof(AccGraphicsPage.PlayerCarId), 1216)]
    [InlineData(nameof(AccGraphicsPage.PenaltyTime), 1220)]
    [InlineData(nameof(AccGraphicsPage.Flag), 1224)]
    [InlineData(nameof(AccGraphicsPage.Penalty), 1228)]
    [InlineData(nameof(AccGraphicsPage.IdealLineOn), 1232)]
    [InlineData(nameof(AccGraphicsPage.IsInPitLane), 1236)]
    [InlineData(nameof(AccGraphicsPage.SurfaceGrip), 1240)]
    [InlineData(nameof(AccGraphicsPage.MandatoryPitDone), 1244)]
    [InlineData(nameof(AccGraphicsPage.WindSpeed), 1248)]
    [InlineData(nameof(AccGraphicsPage.WindDirection), 1252)]
    [InlineData(nameof(AccGraphicsPage.IsSetupMenuVisible), 1256)]
    [InlineData(nameof(AccGraphicsPage.MainDisplayIndex), 1260)]
    [InlineData(nameof(AccGraphicsPage.SecondaryDisplayIndex), 1264)]
    [InlineData(nameof(AccGraphicsPage.Tc), 1268)]
    [InlineData(nameof(AccGraphicsPage.TcCut), 1272)]
    [InlineData(nameof(AccGraphicsPage.EngineMap), 1276)]
    [InlineData(nameof(AccGraphicsPage.Abs), 1280)]
    [InlineData(nameof(AccGraphicsPage.FuelXLap), 1284)]
    [InlineData(nameof(AccGraphicsPage.RainLights), 1288)]
    [InlineData(nameof(AccGraphicsPage.FlashingLights), 1292)]
    [InlineData(nameof(AccGraphicsPage.LightsStage), 1296)]
    [InlineData(nameof(AccGraphicsPage.ExhaustTemperature), 1300)]
    [InlineData(nameof(AccGraphicsPage.WiperLv), 1304)]
    [InlineData(nameof(AccGraphicsPage.DriverStintTotalTimeLeft), 1308)]
    [InlineData(nameof(AccGraphicsPage.DriverStintTimeLeft), 1312)]
    [InlineData(nameof(AccGraphicsPage.RainTyres), 1316)]
    [InlineData(nameof(AccGraphicsPage.SessionIndex), 1320)]
    [InlineData(nameof(AccGraphicsPage.UsedFuel), 1324)]
    [InlineData(nameof(AccGraphicsPage.DeltaLapTime), 1328)]
    [InlineData(nameof(AccGraphicsPage.IDeltaLapTime), 1360)] // 2-byte pad after deltaLapTime[15]
    [InlineData(nameof(AccGraphicsPage.EstimatedLapTime), 1364)]
    [InlineData(nameof(AccGraphicsPage.IEstimatedLapTime), 1396)] // 2-byte pad after estimatedLapTime[15]
    [InlineData(nameof(AccGraphicsPage.IsDeltaPositive), 1400)]
    [InlineData(nameof(AccGraphicsPage.ISplit), 1404)]
    [InlineData(nameof(AccGraphicsPage.IsValidLap), 1408)]
    [InlineData(nameof(AccGraphicsPage.FuelEstimatedLaps), 1412)]
    [InlineData(nameof(AccGraphicsPage.TrackStatus), 1416)]
    [InlineData(nameof(AccGraphicsPage.MissingMandatoryPits), 1484)] // 2-byte pad after trackStatus[33]
    [InlineData(nameof(AccGraphicsPage.Clock), 1488)]
    [InlineData(nameof(AccGraphicsPage.DirectionLightsLeft), 1492)]
    [InlineData(nameof(AccGraphicsPage.DirectionLightsRight), 1496)]
    [InlineData(nameof(AccGraphicsPage.GlobalYellow), 1500)]
    [InlineData(nameof(AccGraphicsPage.GlobalYellow1), 1504)]
    [InlineData(nameof(AccGraphicsPage.GlobalYellow2), 1508)]
    [InlineData(nameof(AccGraphicsPage.GlobalYellow3), 1512)]
    [InlineData(nameof(AccGraphicsPage.GlobalWhite), 1516)]
    [InlineData(nameof(AccGraphicsPage.GlobalGreen), 1520)]
    [InlineData(nameof(AccGraphicsPage.GlobalChequered), 1524)]
    [InlineData(nameof(AccGraphicsPage.GlobalRed), 1528)]
    [InlineData(nameof(AccGraphicsPage.MfdTyreSet), 1532)]
    [InlineData(nameof(AccGraphicsPage.MfdFuelToAdd), 1536)]
    [InlineData(nameof(AccGraphicsPage.MfdTyrePressureFl), 1540)]
    [InlineData(nameof(AccGraphicsPage.MfdTyrePressureFr), 1544)]
    [InlineData(nameof(AccGraphicsPage.MfdTyrePressureRl), 1548)]
    [InlineData(nameof(AccGraphicsPage.MfdTyrePressureRr), 1552)]
    [InlineData(nameof(AccGraphicsPage.TrackGripStatus), 1556)]
    [InlineData(nameof(AccGraphicsPage.RainIntensity), 1560)]
    [InlineData(nameof(AccGraphicsPage.RainIntensityIn10Min), 1564)]
    [InlineData(nameof(AccGraphicsPage.RainIntensityIn30Min), 1568)]
    [InlineData(nameof(AccGraphicsPage.CurrentTyreSet), 1572)]
    [InlineData(nameof(AccGraphicsPage.StrategyTyreSet), 1576)]
    [InlineData(nameof(AccGraphicsPage.GapAhead), 1580)]
    [InlineData(nameof(AccGraphicsPage.GapBehind), 1584)]
    public void Graphics_field_offset_matches_documented_native_layout(string fieldName, int expectedOffset)
    {
        // Act
        int actualOffset = Marshal.OffsetOf<AccGraphicsPage>(fieldName).ToInt32();

        // Assert
        actualOffset.Should().Be(
            expectedOffset,
            $"native SPageFileGraphic places {fieldName} at byte {expectedOffset}");
    }

    [Fact]
    public void Graphics_string_without_null_terminator_reads_full_capacity_and_does_not_bleed()
    {
        // Arrange — a torn read upstream can leave a wchar_t field with no terminator
        byte[] page = new PageFixtureBuilder(DocumentedSizeBytes)
            .WithUtf16(12, new string('A', 15), 15) // currentTime filled to capacity, unterminated
            .WithUtf16(42, "1:41.000", 15)          // lastTime — must stay untouched
            .Build();

        // Act
        AccGraphicsPage parsed = AccPageMarshaller.Read<AccGraphicsPage>(page);

        // Assert
        parsed.CurrentTime.Should().Be(new string('A', 15));
        parsed.LastTime.Should().Be("1:41.000");
    }

    [Fact]
    public void Graphics_fixture_page_parses_known_values_at_documented_offsets()
    {
        // Arrange
        byte[] page = new PageFixtureBuilder(DocumentedSizeBytes)
            .WithInt32(0, 7)               // packetId
            .WithInt32(4, 2)               // status = AC_LIVE
            .WithInt32(8, 1)               // session = AC_QUALIFY
            .WithUtf16(12, "1:43.512", 15) // currentTime
            .WithInt32(132, 5)             // completedLaps
            .WithUtf16(176, "dry_compound", 33)
            .WithSingle(248, 0.42f)        // normalizedCarPosition
            .WithSingle(268, 100.5f)       // carCoordinates[1][0] = second car X
            .WithInt32(976, 1001)          // carID[0]
            .WithInt32(1216, 1001)         // playerCarID
            .WithInt32(1360, -120)         // iDeltaLapTime
            .WithInt32(1556, 2)            // trackGripStatus = ACC_OPTIMUM
            .WithInt32(1560, 1)            // rainIntensity = ACC_DRIZZLE
            .WithInt32(1584, 850)          // gapBehind
            .Build();

        // Act
        AccGraphicsPage parsed = AccPageMarshaller.Read<AccGraphicsPage>(page);

        // Assert
        parsed.PacketId.Should().Be(7);
        parsed.Status.Should().Be(2);
        parsed.Session.Should().Be(1);
        parsed.CurrentTime.Should().Be("1:43.512");
        parsed.CompletedLaps.Should().Be(5);
        parsed.TyreCompound.Should().Be("dry_compound");
        parsed.NormalizedCarPosition.Should().Be(0.42f);
        parsed.CarCoordinates[3].Should().Be(100.5f);
        parsed.CarId[0].Should().Be(1001);
        parsed.PlayerCarId.Should().Be(1001);
        parsed.IDeltaLapTime.Should().Be(-120);
        parsed.TrackGripStatus.Should().Be(2);
        parsed.RainIntensity.Should().Be(1);
        parsed.GapBehind.Should().Be(850);
    }
}
