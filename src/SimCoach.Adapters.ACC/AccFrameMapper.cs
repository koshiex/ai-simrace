using System.Collections.Frozen;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// Pure mapping from an ACC shared-memory snapshot to the normalized <see cref="TelemetryFrame"/>
/// contract: km/h → m/s, PSI → kPa, ACC gear encoding (0=R, 1=N, 2=first) → contract (-1/0/1),
/// id normalization, weather buckets. Wheel arrays keep ACC's native [FL, FR, RL, RR] order,
/// which matches the contract.
/// </summary>
public static class AccFrameMapper
{
    private const float KmhToMps = 1f / 3.6f;
    private const float PsiToKpa = 6.894757f;
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>Below this track temperature a dry track counts as "dry-cool".</summary>
    private const float DryCoolMaxTrackTempC = 25f;

    // ACC_RAIN_INTENSITY values (graphics page).
    private const int RainIntensityDrizzle = 1;
    private const int RainIntensityLight = 2;

    // ACC_TRACK_GRIP_STATUS: 4 = DAMP (drying surface), 5 = WET, 6 = FLOODED.
    // Standing water means "wet" even after rain stops; a drying line is only "damp".
    private const int TrackGripDamp = 4;
    private const int TrackGripWet = 5;

    // AC_FLAG_TYPE tops out at 8 (orange); ACC is known to emit enum values beyond the
    // documented range (see KB: penalty 22), so anything outside 1..8 maps to no flags.
    private const int MaxFlagValue = 8;

    // AC_STATUS (graphics page): 0 OFF, 1 REPLAY, 2 LIVE, 3 PAUSE.
    private const int AccStatusLive = 2;

    private const string WeatherDryCool = "dry-cool";
    private const string WeatherDryWarm = "dry-warm";
    private const string WeatherDamp = "damp";
    private const string WeatherWet = "wet";

    /// <summary>
    /// Known native-name → normalized-id exceptions. No entries yet: ACC track and car ids are
    /// already snake_case, and lowercase+underscore normalization covers them. Extend when real
    /// shared-memory dumps reveal names the generic normalization can't handle.
    /// </summary>
    private static readonly FrozenDictionary<string, string> _idAliases =
        FrozenDictionary<string, string>.Empty;

    /// <summary>Maps a marshaled snapshot to a telemetry frame. Pure — no state, no clock.</summary>
    /// <exception cref="ArgumentNullException">The snapshot is null.</exception>
    /// <exception cref="ArgumentException">The snapshot holds default (unmarshaled) page structs.</exception>
    public static TelemetryFrame Map(AccTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Physics.TyreCoreTemperature is null)
        {
            throw new ArgumentException(
                "Snapshot pages must be marshaled from page bytes — default structs have null arrays.",
                nameof(snapshot));
        }

        SharedMemory.AccPhysicsPage physics = snapshot.Physics;
        SharedMemory.AccGraphicsPage graphics = snapshot.Graphics;
        SharedMemory.AccStaticPage staticPage = snapshot.Static;

        string trackId = NormalizeId(staticPage.Track);
        string carId = NormalizeId(staticPage.CarModel);

        // PlayerCarId is a car *id value*, not a slot index — resolve the slot into the flattened
        // CarCoordinates[60*3] via its position in the CarId array. An absent id (torn/early frame)
        // yields slot -1 → zeroed world_pos, consistent with the mapper's honest-zeros stance.
        int playerSlot = Array.IndexOf(graphics.CarId, graphics.PlayerCarId);

        TelemetryFrame frame = new()
        {
            T = Timestamp.FromDateTimeOffset(snapshot.CapturedAt),
            Sim = AccSharedMemoryReader.SimId,
            TrackId = trackId,
            CarId = carId,
            WeatherBucket = DeriveWeatherBucket(graphics.RainIntensity, graphics.TrackGripStatus, physics.RoadTemp),
            LapNumber = graphics.CompletedLaps + 1,
            // ACC does not populate trackSPlineLength, so lap length comes from the track catalog.
            LapDistanceM = AccTrackCatalog.TryGetLapLengthM(trackId, out float lapLengthM)
                ? graphics.NormalizedCarPosition * lapLengthM
                : 0f,
            NormalizedCarPosition = graphics.NormalizedCarPosition,
            SpeedMps = physics.SpeedKmh * KmhToMps,
            ThrottlePct = physics.Gas,
            BrakePct = physics.Brake,
            // ACC reports clutch ENGAGEMENT (0 = pedal fully pressed, 1 = released) — inverted
            // so all three pedal fields uniformly mean application: pressed pedal → 1.
            ClutchPct = 1f - physics.Clutch,
            // ACC steerAngle is normalized [-1..1] of full lock (Kunos doc V1.8.12: "Steering
            // input value"); steering-wheel radians = input × half the car's lock-to-lock.
            SteerRad = physics.SteerAngle * (AccCarCatalog.GetSteerLockDeg(carId) / 2f) * DegToRad,
            Gear = physics.Gear - 1,
            Rpm = physics.Rpm,
            GForceG = new Vec3 { X = physics.AccG[0], Y = physics.AccG[1], Z = physics.AccG[2] },
            AirTempC = physics.AirTemp,
            TrackTempC = physics.RoadTemp,
            WindSpeedMps = graphics.WindSpeed,
            FuelL = physics.Fuel,
            FuelPerLapL = graphics.FuelXLap,
            TcActive = physics.Tc > 0f,
            AbsActive = physics.Abs > 0f,
            FlagsActive = ToFlagBits(graphics.Flag),
            WorldPos = playerSlot >= 0 && playerSlot < SharedMemory.AccGraphicsPage.MaxCars
                ? new Vec3
                {
                    X = graphics.CarCoordinates[playerSlot * 3],
                    Y = graphics.CarCoordinates[(playerSlot * 3) + 1],
                    Z = graphics.CarCoordinates[(playerSlot * 3) + 2],
                }
                : new Vec3(),
            CurrentSectorIndex = graphics.CurrentSectorIndex,
            SectorCount = staticPage.SectorCount,
            // NumberOfTyresOut is "Not used in ACC" (always 0 live) — honest passthrough like
            // TyreWear/WheelLoad; real off-track data comes from other sims / synthesized fixtures.
            TyresOut = physics.NumberOfTyresOut,
            // ACC int → bool (mirrors the Tc/Abs int→bool conversions above).
            IsValidLap = graphics.IsValidLap != 0,
            // Phase 3 strategy plumb (data-only, no consumer in MVP). The int aid LEVELS from the
            // graphics page are distinct from the tc_active/abs_active bools (physics intervention) above.
            EngineMap = graphics.EngineMap,
            Tc = graphics.Tc,
            TcCut = graphics.TcCut,
            Abs = graphics.Abs,
            IsInPit = graphics.IsInPit != 0,
            IsInPitLane = graphics.IsInPitLane != 0,
        };

        frame.TyreTempC.AddRange(physics.TyreCoreTemperature);
        foreach (float pressurePsi in physics.WheelsPressure)
        {
            frame.TyrePressureKpa.Add(pressurePsi * PsiToKpa);
        }

        // TyreWear and WheelLoad are "Not used in ACC" (always 0 live) — passed through so the
        // contract fields stay honest zeros; other sims will populate them.
        frame.TyreWearPct.AddRange(physics.TyreWear);
        frame.BrakeTempC.AddRange(physics.BrakeTemp);
        frame.WheelSlip.AddRange(physics.WheelSlip);
        // Longitudinal slip ratio — the drive-wheel wheelspin source (distinct from combined WheelSlip).
        frame.SlipRatio.AddRange(physics.SlipRatio);
        frame.WheelLoadN.AddRange(physics.WheelLoad);
        frame.SuspensionTravelM.AddRange(physics.SuspensionTravel);
        return frame;
    }

    /// <summary>
    /// True only for frames worth recording: ACC is LIVE and the static page has populated
    /// track/car identity. Dormant box/menu/replay/pause frames carry empty ids and zeroed
    /// sensors (issue #1) and would poison Phase 2 compute keyed on track_id/car_id — reject
    /// them at the source. Cheap and array-free, so it is safe to call before <see cref="Map"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The snapshot is null.</exception>
    public static bool IsRecordable(AccTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Graphics.Status != AccStatusLive)
        {
            return false;
        }

        return NormalizeId(snapshot.Static.Track).Length > 0
            && NormalizeId(snapshot.Static.CarModel).Length > 0;
    }

    /// <summary>
    /// AC_FLAG_TYPE (single value 1..8) → contract bit flags: flag N sets bit N-1
    /// (bit assignment defined in telemetry.proto). Out-of-range values map to no flags —
    /// C# masks shift counts to 5 bits, so e.g. flag 33 would otherwise alias bit 0.
    /// </summary>
    private static int ToFlagBits(int flag) => flag is <= 0 or > MaxFlagValue ? 0 : 1 << (flag - 1);

    private static string NormalizeId(string nativeName)
    {
        string normalized = (nativeName ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        return _idAliases.TryGetValue(normalized, out string? alias) ? alias : normalized;
    }

    private static string DeriveWeatherBucket(int rainIntensity, int trackGripStatus, float roadTempC)
    {
        if (rainIntensity >= RainIntensityLight || trackGripStatus >= TrackGripWet)
        {
            return WeatherWet;
        }

        if (rainIntensity == RainIntensityDrizzle || trackGripStatus == TrackGripDamp)
        {
            return WeatherDamp;
        }

        // roadTemp == 0 means the sensor is not ready (non-live / early frame), not a cold
        // track (issue #2) — treat <= 0 as "no data" and fall to the dry-warm branch rather
        // than misclassifying it as dry-cool.
        return roadTempC is > 0f and < DryCoolMaxTrackTempC ? WeatherDryCool : WeatherDryWarm;
    }
}
