using System.Runtime.InteropServices;

namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Native layout of ACC's <c>Local\acpmf_graphics</c> shared-memory page
/// (Kunos shared-memory documentation V1.8.12, struct <c>SPageFileGraphic</c>, pack 4, 1588 bytes).
/// Field order, types and array sizes mirror the C++ struct exactly — never reorder or retype;
/// the golden layout tests pin every offset. Strings are fixed-size null-terminated wchar_t arrays.
/// Mutable public fields are required by the marshaler; treat instances as read-only snapshots.
/// Struct copies share the array instances — copying a page does not deep-copy its arrays.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct AccGraphicsPage
{
    /// <summary>Marshaled size of the native page in bytes.</summary>
    public const int SizeBytes = 1588;

    /// <summary>Maximum number of cars exposed in <see cref="CarCoordinates"/> / <see cref="CarId"/>.</summary>
    public const int MaxCars = 60;

    public int PacketId;

    /// <summary>AC_STATUS: 0 OFF, 1 REPLAY, 2 LIVE, 3 PAUSE.</summary>
    public int Status;

    /// <summary>
    /// AC_SESSION_TYPE: -1 UNKNOWN, 0 PRACTICE, 1 QUALIFY, 2 RACE, 3 HOTLAP,
    /// 4 TIME_ATTACK, 5 DRIFT, 6 DRAG, 7 HOTSTINT, 8 HOTLAPSUPERPOLE.
    /// </summary>
    public int Session;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string CurrentTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string LastTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string BestTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string Split;
    public int CompletedLaps;
    public int Position;
    public int ICurrentTime;
    public int ILastTime;
    public int IBestTime;
    public float SessionTimeLeft;
    public float DistanceTraveled;
    public int IsInPit;
    public int CurrentSectorIndex;
    public int LastSectorTime;
    public int NumberOfLaps;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TyreCompound;

    /// <summary>Not used in ACC.</summary>
    public float ReplayTimeMultiplier;

    /// <summary>Player position on the track spline, 0.0 to 1.0.</summary>
    public float NormalizedCarPosition;
    public int ActiveCars;

    /// <summary>Flattened native float[60][3]: per-car [x, y, z] world coordinates.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCars * 3)]
    public float[] CarCoordinates;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCars)]
    public int[] CarId;
    public int PlayerCarId;
    public float PenaltyTime;

    /// <summary>AC_FLAG_TYPE: 0 none .. 8 orange.</summary>
    public int Flag;

    /// <summary>PenaltyShortcut: 0..21 documented; 22 (disqualified wrong way) observed in the wild.</summary>
    public int Penalty;
    public int IdealLineOn;
    public int IsInPitLane;

    /// <summary>Always returns 0 in ACC.</summary>
    public float SurfaceGrip;
    public int MandatoryPitDone;

    /// <summary>Meters per second.</summary>
    public float WindSpeed;

    /// <summary>Radians.</summary>
    public float WindDirection;
    public int IsSetupMenuVisible;
    public int MainDisplayIndex;
    public int SecondaryDisplayIndex;
    public int Tc;
    public int TcCut;
    public int EngineMap;
    public int Abs;
    public float FuelXLap;
    public int RainLights;
    public int FlashingLights;
    public int LightsStage;
    public float ExhaustTemperature;
    public int WiperLv;

    /// <summary>Milliseconds.</summary>
    public int DriverStintTotalTimeLeft;

    /// <summary>Milliseconds.</summary>
    public int DriverStintTimeLeft;
    public int RainTyres;
    public int SessionIndex;
    public float UsedFuel;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string DeltaLapTime;

    /// <summary>Milliseconds.</summary>
    public int IDeltaLapTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string EstimatedLapTime;

    /// <summary>Milliseconds.</summary>
    public int IEstimatedLapTime;
    public int IsDeltaPositive;

    /// <summary>Milliseconds.</summary>
    public int ISplit;
    public int IsValidLap;
    public float FuelEstimatedLaps;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TrackStatus;
    public int MissingMandatoryPits;

    /// <summary>Time of day, seconds.</summary>
    public float Clock;
    public int DirectionLightsLeft;
    public int DirectionLightsRight;
    public int GlobalYellow;
    public int GlobalYellow1;
    public int GlobalYellow2;
    public int GlobalYellow3;
    public int GlobalWhite;
    public int GlobalGreen;
    public int GlobalChequered;
    public int GlobalRed;
    public int MfdTyreSet;
    public float MfdFuelToAdd;
    public float MfdTyrePressureFl;
    public float MfdTyrePressureFr;
    public float MfdTyrePressureRl;
    public float MfdTyrePressureRr;

    /// <summary>ACC_TRACK_GRIP_STATUS: 0 GREEN, 1 FAST, 2 OPTIMUM, 3 GREASY, 4 DAMP, 5 WET, 6 FLOODED.</summary>
    public int TrackGripStatus;

    /// <summary>ACC_RAIN_INTENSITY: 0 NO_RAIN, 1 DRIZZLE, 2 LIGHT, 3 MEDIUM, 4 HEAVY, 5 THUNDERSTORM.</summary>
    public int RainIntensity;
    public int RainIntensityIn10Min;
    public int RainIntensityIn30Min;
    public int CurrentTyreSet;
    public int StrategyTyreSet;

    /// <summary>Milliseconds.</summary>
    public int GapAhead;

    /// <summary>Milliseconds.</summary>
    public int GapBehind;
}
