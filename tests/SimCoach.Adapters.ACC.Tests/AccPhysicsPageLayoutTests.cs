using System.Runtime.InteropServices;
using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Golden layout tests for <see cref="AccPhysicsPage"/> against the Kunos shared-memory
/// documentation V1.8.12 (SPageFilePhysics, pack 4, 800 bytes). A wrong offset here means
/// every downstream consumer reads garbage — do not "fix" these numbers to match the code.
/// </summary>
public sealed class AccPhysicsPageLayoutTests
{
    private const int DocumentedSizeBytes = 800;

    [Fact]
    public void Physics_page_size_matches_documented_native_layout()
    {
        // Act
        int marshaledSize = Marshal.SizeOf<AccPhysicsPage>();

        // Assert
        marshaledSize.Should().Be(DocumentedSizeBytes);
        AccPhysicsPage.SizeBytes.Should().Be(DocumentedSizeBytes);
    }

    [Theory]
    [InlineData(nameof(AccPhysicsPage.PacketId), 0)]
    [InlineData(nameof(AccPhysicsPage.Gas), 4)]
    [InlineData(nameof(AccPhysicsPage.Brake), 8)]
    [InlineData(nameof(AccPhysicsPage.Fuel), 12)]
    [InlineData(nameof(AccPhysicsPage.Gear), 16)]
    [InlineData(nameof(AccPhysicsPage.Rpm), 20)]
    [InlineData(nameof(AccPhysicsPage.SteerAngle), 24)]
    [InlineData(nameof(AccPhysicsPage.SpeedKmh), 28)]
    [InlineData(nameof(AccPhysicsPage.Velocity), 32)]
    [InlineData(nameof(AccPhysicsPage.AccG), 44)]
    [InlineData(nameof(AccPhysicsPage.WheelSlip), 56)]
    [InlineData(nameof(AccPhysicsPage.WheelLoad), 72)]
    [InlineData(nameof(AccPhysicsPage.WheelsPressure), 88)]
    [InlineData(nameof(AccPhysicsPage.WheelAngularSpeed), 104)]
    [InlineData(nameof(AccPhysicsPage.TyreWear), 120)]
    [InlineData(nameof(AccPhysicsPage.TyreDirtyLevel), 136)]
    [InlineData(nameof(AccPhysicsPage.TyreCoreTemperature), 152)]
    [InlineData(nameof(AccPhysicsPage.CamberRad), 168)]
    [InlineData(nameof(AccPhysicsPage.SuspensionTravel), 184)]
    [InlineData(nameof(AccPhysicsPage.Drs), 200)]
    [InlineData(nameof(AccPhysicsPage.Tc), 204)]
    [InlineData(nameof(AccPhysicsPage.Heading), 208)]
    [InlineData(nameof(AccPhysicsPage.Pitch), 212)]
    [InlineData(nameof(AccPhysicsPage.Roll), 216)]
    [InlineData(nameof(AccPhysicsPage.CgHeight), 220)]
    [InlineData(nameof(AccPhysicsPage.CarDamage), 224)]
    [InlineData(nameof(AccPhysicsPage.NumberOfTyresOut), 244)]
    [InlineData(nameof(AccPhysicsPage.PitLimiterOn), 248)]
    [InlineData(nameof(AccPhysicsPage.Abs), 252)]
    [InlineData(nameof(AccPhysicsPage.KersCharge), 256)]
    [InlineData(nameof(AccPhysicsPage.KersInput), 260)]
    [InlineData(nameof(AccPhysicsPage.AutoShifterOn), 264)]
    [InlineData(nameof(AccPhysicsPage.RideHeight), 268)]
    [InlineData(nameof(AccPhysicsPage.TurboBoost), 276)]
    [InlineData(nameof(AccPhysicsPage.Ballast), 280)]
    [InlineData(nameof(AccPhysicsPage.AirDensity), 284)]
    [InlineData(nameof(AccPhysicsPage.AirTemp), 288)]
    [InlineData(nameof(AccPhysicsPage.RoadTemp), 292)]
    [InlineData(nameof(AccPhysicsPage.LocalAngularVel), 296)]
    [InlineData(nameof(AccPhysicsPage.FinalFf), 308)]
    [InlineData(nameof(AccPhysicsPage.PerformanceMeter), 312)]
    [InlineData(nameof(AccPhysicsPage.EngineBrake), 316)]
    [InlineData(nameof(AccPhysicsPage.ErsRecoveryLevel), 320)]
    [InlineData(nameof(AccPhysicsPage.ErsPowerLevel), 324)]
    [InlineData(nameof(AccPhysicsPage.ErsHeatCharging), 328)]
    [InlineData(nameof(AccPhysicsPage.ErsIsCharging), 332)]
    [InlineData(nameof(AccPhysicsPage.KersCurrentKj), 336)]
    [InlineData(nameof(AccPhysicsPage.DrsAvailable), 340)]
    [InlineData(nameof(AccPhysicsPage.DrsEnabled), 344)]
    [InlineData(nameof(AccPhysicsPage.BrakeTemp), 348)]
    [InlineData(nameof(AccPhysicsPage.Clutch), 364)]
    [InlineData(nameof(AccPhysicsPage.TyreTempI), 368)]
    [InlineData(nameof(AccPhysicsPage.TyreTempM), 384)]
    [InlineData(nameof(AccPhysicsPage.TyreTempO), 400)]
    [InlineData(nameof(AccPhysicsPage.IsAiControlled), 416)]
    [InlineData(nameof(AccPhysicsPage.TyreContactPoint), 420)]
    [InlineData(nameof(AccPhysicsPage.TyreContactNormal), 468)]
    [InlineData(nameof(AccPhysicsPage.TyreContactHeading), 516)]
    [InlineData(nameof(AccPhysicsPage.BrakeBias), 564)]
    [InlineData(nameof(AccPhysicsPage.LocalVelocity), 568)]
    [InlineData(nameof(AccPhysicsPage.P2PActivations), 580)]
    [InlineData(nameof(AccPhysicsPage.P2PStatus), 584)]
    [InlineData(nameof(AccPhysicsPage.CurrentMaxRpm), 588)]
    [InlineData(nameof(AccPhysicsPage.Mz), 592)]
    [InlineData(nameof(AccPhysicsPage.Fx), 608)]
    [InlineData(nameof(AccPhysicsPage.Fy), 624)]
    [InlineData(nameof(AccPhysicsPage.SlipRatio), 640)]
    [InlineData(nameof(AccPhysicsPage.SlipAngle), 656)]
    [InlineData(nameof(AccPhysicsPage.TcInAction), 672)]
    [InlineData(nameof(AccPhysicsPage.AbsInAction), 676)]
    [InlineData(nameof(AccPhysicsPage.SuspensionDamage), 680)]
    [InlineData(nameof(AccPhysicsPage.TyreTemp), 696)]
    [InlineData(nameof(AccPhysicsPage.WaterTemp), 712)]
    [InlineData(nameof(AccPhysicsPage.BrakePressure), 716)]
    [InlineData(nameof(AccPhysicsPage.FrontBrakeCompound), 732)]
    [InlineData(nameof(AccPhysicsPage.RearBrakeCompound), 736)]
    [InlineData(nameof(AccPhysicsPage.PadLife), 740)]
    [InlineData(nameof(AccPhysicsPage.DiscLife), 756)]
    [InlineData(nameof(AccPhysicsPage.IgnitionOn), 772)]
    [InlineData(nameof(AccPhysicsPage.StarterEngineOn), 776)]
    [InlineData(nameof(AccPhysicsPage.IsEngineRunning), 780)]
    [InlineData(nameof(AccPhysicsPage.KerbVibration), 784)]
    [InlineData(nameof(AccPhysicsPage.SlipVibrations), 788)]
    [InlineData(nameof(AccPhysicsPage.GVibrations), 792)]
    [InlineData(nameof(AccPhysicsPage.AbsVibrations), 796)]
    public void Physics_field_offset_matches_documented_native_layout(string fieldName, int expectedOffset)
    {
        // Act
        int actualOffset = Marshal.OffsetOf<AccPhysicsPage>(fieldName).ToInt32();

        // Assert
        actualOffset.Should().Be(
            expectedOffset,
            $"native SPageFilePhysics places {fieldName} at byte {expectedOffset}");
    }

    [Fact]
    public void Physics_fixture_page_parses_known_values_at_documented_offsets()
    {
        // Arrange
        byte[] page = new PageFixtureBuilder(DocumentedSizeBytes)
            .WithInt32(0, 42)            // packetId
            .WithSingle(4, 0.75f)        // gas
            .WithSingle(8, 0.25f)        // brake
            .WithInt32(16, 4)            // gear (native: 0=R, 1=N, 2=first)
            .WithSingle(28, 212.5f)      // speedKmh
            .WithSingle(60, 0.12f)       // wheelSlip[1] = FR
            .WithSingle(164, 81.2f)      // tyreCoreTemperature[3] = RR
            .WithSingle(356, 412f)       // brakeTemp[2] = RL
            .WithInt32(588, 7250)        // currentMaxRpm
            .WithSingle(740, 28.5f)      // padLife[0] = FL
            .WithSingle(796, 0.5f)       // absVibrations
            .Build();

        // Act
        AccPhysicsPage parsed = AccPageMarshaller.Read<AccPhysicsPage>(page);

        // Assert
        parsed.PacketId.Should().Be(42);
        parsed.Gas.Should().Be(0.75f);
        parsed.Brake.Should().Be(0.25f);
        parsed.Gear.Should().Be(4);
        parsed.SpeedKmh.Should().Be(212.5f);
        parsed.WheelSlip[1].Should().Be(0.12f);
        parsed.TyreCoreTemperature[3].Should().Be(81.2f);
        parsed.BrakeTemp[2].Should().Be(412f);
        parsed.CurrentMaxRpm.Should().Be(7250);
        parsed.PadLife[0].Should().Be(28.5f);
        parsed.AbsVibrations.Should().Be(0.5f);
    }
}
