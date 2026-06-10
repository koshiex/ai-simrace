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

        TelemetryFrame frame = new()
        {
            T = Timestamp.FromDateTimeOffset(snapshot.CapturedAt),
            Sim = AccSharedMemoryReader.SimId,
            TrackId = NormalizeId(staticPage.Track),
            CarId = NormalizeId(staticPage.CarModel),
            WeatherBucket = DeriveWeatherBucket(graphics.RainIntensity, graphics.TrackGripStatus, physics.RoadTemp),
            LapNumber = graphics.CompletedLaps + 1,
            // ACC does not populate trackSPlineLength (see KB: acc-shared-memory-layout), so this
            // stays 0 until Phase 2 derives lap distance from track data.
            LapDistanceM = staticPage.TrackSplineLength > 0f
                ? graphics.NormalizedCarPosition * staticPage.TrackSplineLength
                : 0f,
            NormalizedCarPosition = graphics.NormalizedCarPosition,
            SpeedMps = physics.SpeedKmh * KmhToMps,
            ThrottlePct = physics.Gas,
            BrakePct = physics.Brake,
            // ACC reports clutch ENGAGEMENT (0 = pedal fully pressed, 1 = released) — inverted
            // so all three pedal fields uniformly mean application: pressed pedal → 1.
            ClutchPct = 1f - physics.Clutch,
            // ACC reports a normalized steering value; per-car conversion to radians needs the
            // steering lock, which shared memory does not expose. Passed through as-is and
            // verified against a real dump in B7.
            SteerRad = physics.SteerAngle,
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
        frame.WheelLoadN.AddRange(physics.WheelLoad);
        frame.SuspensionTravelM.AddRange(physics.SuspensionTravel);
        return frame;
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

        return roadTempC < DryCoolMaxTrackTempC ? WeatherDryCool : WeatherDryWarm;
    }
}
