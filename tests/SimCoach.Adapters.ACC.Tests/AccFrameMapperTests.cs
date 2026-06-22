using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Tests for the pure snapshot→frame mapping: unit conversions (km/h→m/s, PSI→kPa),
/// ACC gear offset (0=R,1=N,2=first → -1/0/1), id normalization and weather buckets.
/// Native page offsets in the fixtures come from the Kunos V1.8.12 layout (see layout tests).
/// </summary>
public sealed class AccFrameMapperTests
{
    [Fact]
    public void Golden_snapshot_maps_every_frame_field()
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page
                .WithSingle(4, 0.8f)        // gas
                .WithSingle(8, 0.2f)        // brake
                .WithSingle(12, 42.5f)      // fuel (l)
                .WithInt32(16, 5)           // gear (native: 4th)
                .WithInt32(20, 7200)        // rpm
                .WithSingle(24, -0.31f)     // steerAngle
                .WithSingle(28, 216f)       // speedKmh → 60 m/s
                .WithSingle(44, 1.2f)       // accG[0] = lateral
                .WithSingle(48, 0.95f)      // accG[1] = vertical
                .WithSingle(52, -0.4f)      // accG[2] = longitudinal
                .WithSingle(60, 0.12f)      // wheelSlip[1] = FR
                .WithSingle(76, 3200f)      // wheelLoad[1] = FR (NU in ACC — always 0 live)
                .WithSingle(92, 27.6f)      // wheelsPressure[1] = FR (psi)
                .WithSingle(124, 0.03f)     // tyreWear[1] = FR (NU in ACC — always 0 live)
                .WithSingle(156, 82.5f)     // tyreCoreTemperature[1] = FR
                .WithSingle(188, 0.011f)    // suspensionTravel[1] = FR (m)
                .WithSingle(204, 0.35f)     // tc in action
                .WithSingle(252, 0f)        // abs not in action
                .WithSingle(288, 21.5f)     // airTemp
                .WithSingle(292, 28.5f)     // roadTemp
                .WithSingle(352, 412f)      // brakeTemp[1] = FR
                .WithSingle(364, 0.25f)     // clutch engagement → pedal 0.75
                .WithInt32(244, 2),         // numberOfTyresOut (NU in ACC — passthrough)
            graphics: page => page
                .WithInt32(132, 5)          // completedLaps → lap 6
                .WithInt32(164, 1)          // currentSectorIndex
                .WithSingle(248, 0.42f)     // normalizedCarPosition
                .WithSingle(256, 12.5f)     // carCoordinates[slot0].x
                .WithSingle(260, 3.0f)      // carCoordinates[slot0].y
                .WithSingle(264, -8.0f)     // carCoordinates[slot0].z
                .WithInt32(976, 7)          // carId[0] = 7
                .WithInt32(1216, 7)         // playerCarId = 7 → slot 0
                .WithInt32(1224, 2)         // flag = AC_YELLOW_FLAG
                .WithSingle(1248, 3.5f)     // windSpeed (m/s)
                .WithSingle(1284, 2.9f)     // fuelXLap
                .WithInt32(1408, 1)         // isValidLap = 1
                .WithInt32(1556, 2)         // trackGripStatus = OPTIMUM
                .WithInt32(1560, 0),        // rainIntensity = NO_RAIN
            @static: page => page
                .WithUtf16(68, "audi_r8_lms_evo_ii", 33) // carModel
                .WithUtf16(134, "Spa", 33)               // track
                .WithInt32(400, 3));                     // sectorCount

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.T.Should().Be(Timestamp.FromDateTimeOffset(AccSnapshotFixture.CapturedAt));
        frame.Sim.Should().Be("acc");
        frame.TrackId.Should().Be("spa");
        frame.CarId.Should().Be("audi_r8_lms_evo_ii");
        frame.WeatherBucket.Should().Be("dry-warm"); // road 28.5 °C ≥ 25 °C threshold
        frame.LapNumber.Should().Be(6);
        frame.LapDistanceM.Should().BeApproximately(0.42f * 7004f, 0.01f); // spa lap length from catalog
        frame.NormalizedCarPosition.Should().Be(0.42f);
        frame.SpeedMps.Should().BeApproximately(60f, 0.001f);
        frame.ThrottlePct.Should().Be(0.8f);
        frame.BrakePct.Should().Be(0.2f);
        frame.ClutchPct.Should().Be(0.75f); // ACC engagement 0.25 inverted to pedal application
        // audi_r8_lms_evo_ii lock 720° → ±360°; -0.31 × 360° in radians
        frame.SteerRad.Should().BeApproximately(-0.31f * 360f * (MathF.PI / 180f), 0.0001f);
        frame.Gear.Should().Be(4); // native 5 → contract 4
        frame.Rpm.Should().Be(7200f);
        frame.TyreTempC[1].Should().Be(82.5f);
        frame.TyrePressureKpa[1].Should().BeApproximately(27.6f * 6.894757f, 0.01f);
        frame.TyreWearPct[1].Should().Be(0.03f);
        frame.BrakeTempC[1].Should().Be(412f);
        frame.WheelSlip[1].Should().Be(0.12f);
        frame.WheelLoadN[1].Should().Be(3200f);
        frame.SuspensionTravelM[1].Should().Be(0.011f);
        frame.GForceG.Should().Be(new Vec3 { X = 1.2f, Y = 0.95f, Z = -0.4f });
        frame.AirTempC.Should().Be(21.5f);
        frame.TrackTempC.Should().Be(28.5f);
        frame.WindSpeedMps.Should().Be(3.5f);
        frame.FuelL.Should().Be(42.5f);
        frame.FuelPerLapL.Should().Be(2.9f);
        frame.TcActive.Should().BeTrue();
        frame.AbsActive.Should().BeFalse();
        frame.FlagsActive.Should().Be(1 << 1); // AC_YELLOW_FLAG (2) → bit 1
        frame.WorldPos.Should().Be(new Vec3 { X = 12.5f, Y = 3.0f, Z = -8.0f });
        frame.CurrentSectorIndex.Should().Be(1);
        frame.SectorCount.Should().Be(3);
        frame.TyresOut.Should().Be(2);
        frame.IsValidLap.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, -1)] // reverse
    [InlineData(1, 0)]  // neutral
    [InlineData(2, 1)]  // first
    [InlineData(7, 6)]
    public void Native_gear_maps_to_contract_gear(int nativeGear, int expectedGear)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page.WithInt32(16, nativeGear));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.Gear.Should().Be(expectedGear);
    }

    [Theory]
    [InlineData(0, 0, 24.9f, "dry-cool")] // boundary: just under the threshold
    [InlineData(0, 0, 25.0f, "dry-warm")] // boundary: at the threshold
    [InlineData(0, 0, 0f, "dry-warm")]    // roadTemp 0 = sensor not ready, not a cold track (#2)
    [InlineData(0, 0, -1f, "dry-warm")]   // negative temp = no data → never dry-cool (#2)
    [InlineData(0, 2, 32.0f, "dry-warm")] // optimum grip stays dry
    [InlineData(0, 3, 28.0f, "dry-warm")] // greasy still counts as dry
    [InlineData(0, 4, 28.0f, "damp")]     // damp track without rain (drying line)
    [InlineData(0, 5, 20.0f, "wet")]      // wet surface without rain — standing water
    [InlineData(0, 6, 20.0f, "wet")]      // flooded without rain
    [InlineData(1, 0, 30.0f, "damp")]     // drizzle
    [InlineData(2, 0, 30.0f, "wet")]      // light rain
    [InlineData(5, 5, 15.0f, "wet")]      // thunderstorm
    public void Weather_bucket_derives_from_rain_grip_and_track_temp(
        int rainIntensity, int trackGripStatus, float roadTempC, string expectedBucket)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page.WithSingle(292, roadTempC),
            graphics: page => page
                .WithInt32(1556, trackGripStatus)
                .WithInt32(1560, rainIntensity));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.WeatherBucket.Should().Be(expectedBucket);
    }

    [Theory]
    [InlineData(2, "bmw_m4_gt3", "spa", true)]   // LIVE + identity → recordable
    [InlineData(0, "bmw_m4_gt3", "spa", false)]  // OFF
    [InlineData(1, "bmw_m4_gt3", "spa", false)]  // REPLAY
    [InlineData(3, "bmw_m4_gt3", "spa", false)]  // PAUSE
    [InlineData(2, "", "spa", false)]            // LIVE but no car identity yet (box/pre-live)
    [InlineData(2, "bmw_m4_gt3", "", false)]     // LIVE but no track identity yet
    public void Recordable_requires_live_status_and_populated_identity(
        int status, string carModel, string track, bool expectedRecordable)
    {
        // Arrange — Status at graphics offset 4; CarModel at static 68, Track at static 134
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            graphics: page => page.WithInt32(4, status),
            @static: page => page
                .WithUtf16(68, carModel, 33)
                .WithUtf16(134, track, 33));

        // Act / Assert
        AccFrameMapper.IsRecordable(snapshot).Should().Be(expectedRecordable);
    }

    [Theory]
    [InlineData("Spa", "spa")]
    [InlineData("  Brands_Hatch ", "brands_hatch")]
    [InlineData("Mount Panorama", "mount_panorama")] // spaces become underscores
    [InlineData("", "")]
    public void Track_and_car_ids_are_normalized(string nativeName, string expectedId)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            @static: page => page
                .WithUtf16(68, nativeName, 33)
                .WithUtf16(134, nativeName, 33));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.TrackId.Should().Be(expectedId);
        frame.CarId.Should().Be(expectedId);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.01f, true)]
    [InlineData(1f, true)]
    public void Abs_in_action_maps_to_boolean(float absValue, bool expectedActive)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page.WithSingle(252, absValue));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.AbsActive.Should().Be(expectedActive);
    }

    [Theory]
    [InlineData(0, 0)]       // AC_NO_FLAG
    [InlineData(1, 1 << 0)]  // blue
    [InlineData(8, 1 << 7)]  // orange — last documented value
    [InlineData(9, 0)]       // beyond the documented range → no flags
    [InlineData(33, 0)]      // would alias bit 0 via C# 5-bit shift masking
    public void Flag_value_maps_to_contract_bits(int nativeFlag, int expectedBits)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            graphics: page => page.WithInt32(1224, nativeFlag));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.FlagsActive.Should().Be(expectedBits);
    }

    [Theory]
    [InlineData(1001, 10f)]  // player id 1001 → slot 0 → x = 10
    [InlineData(2002, 20f)]  // player id 2002 → slot 1 → x = 20 (NOT carCoordinates[2002*3])
    [InlineData(9999, 0f)]   // id absent from CarId → slot -1 → zeroed world_pos
    public void World_pos_resolves_player_slot_via_car_id_indirection(int playerCarId, float expectedX)
    {
        // Arrange — PlayerCarId is a car id VALUE; the slot is its index in CarId.
        // carId[0]=1001 @ 976, carId[1]=2002 @ 980; slot0.x=10 @ 256, slot1.x=20 @ 268.
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            graphics: page => page
                .WithInt32(976, 1001)
                .WithInt32(980, 2002)
                .WithSingle(256, 10f)
                .WithSingle(268, 20f)
                .WithInt32(1216, playerCarId));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.WorldPos.X.Should().Be(expectedX);
    }

    [Theory]
    [InlineData(0, false)] // sim flagged the lap invalid
    [InlineData(1, true)]  // valid lap
    public void Is_valid_lap_maps_int_to_boolean(int isValidLap, bool expectedValid)
    {
        // Arrange — IsValidLap at graphics offset 1408
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            graphics: page => page.WithInt32(1408, isValidLap));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.IsValidLap.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.01f, true)]
    public void Tc_in_action_maps_to_boolean(float tcValue, bool expectedActive)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page.WithSingle(204, tcValue));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.TcActive.Should().Be(expectedActive);
    }

    [Theory]
    [InlineData("monza", 0.5f, 2896.5f)]      // 5793 m × 0.5
    [InlineData("Paul_Ricard", 0.1f, 577f)]   // mixed-case shared-memory id resolves via normalization
    [InlineData("moon_ring", 0.5f, 0f)]       // unknown track → no lap distance
    public void Lap_distance_derives_from_track_catalog(
        string nativeTrack, float normalizedPosition, float expectedDistanceM)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            graphics: page => page.WithSingle(248, normalizedPosition),
            @static: page => page.WithUtf16(134, nativeTrack, 33));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.LapDistanceM.Should().BeApproximately(expectedDistanceM, 0.01f);
    }

    [Theory]
    [InlineData("ferrari_488_gt3", 1f, 240f)]   // lock 480° → full right = +240°
    [InlineData("ferrari_488_gt3", -0.5f, -120f)]
    [InlineData("spaceship_gt1", 1f, 180f)]     // unknown car → fallback 360° lock
    public void Steer_angle_converts_to_wheel_radians_via_car_lock(
        string nativeCar, float steerInput, float expectedWheelDeg)
    {
        // Arrange
        AccTelemetrySnapshot snapshot = AccSnapshotFixture.Build(
            physics: page => page.WithSingle(24, steerInput),
            @static: page => page.WithUtf16(68, nativeCar, 33));

        // Act
        TelemetryFrame frame = AccFrameMapper.Map(snapshot);

        // Assert
        frame.SteerRad.Should().BeApproximately(expectedWheelDeg * (MathF.PI / 180f), 0.0001f);
    }

    [Fact]
    public void Hand_built_snapshot_with_null_page_arrays_fails_fast()
    {
        // Arrange — production snapshots are always marshaled; a default struct has null arrays
        AccTelemetrySnapshot snapshot = new(
            AccSnapshotFixture.CapturedAt,
            AccSnapshotFixture.CapturedAt.Ticks,
            default,
            default,
            default);

        // Act
        Action act = () => AccFrameMapper.Map(snapshot);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*marshaled*");
    }

    [Fact]
    public void Null_snapshot_fails_fast()
    {
        // Act
        Action act = () => AccFrameMapper.Map(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
