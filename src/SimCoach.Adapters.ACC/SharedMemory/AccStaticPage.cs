using System.Runtime.InteropServices;

namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Native layout of ACC's <c>Local\acpmf_static</c> shared-memory page
/// (Kunos shared-memory documentation V1.8.12, struct <c>SPageFileStatic</c>, pack 4, 820 bytes).
/// Field order, types and array sizes mirror the C++ struct exactly — never reorder or retype;
/// the golden layout tests pin every offset. Note: <see cref="TrackConfiguration"/> is
/// wchar_t[33] — several popular C# ports wrongly use [15], shifting the tail by 36 bytes.
/// Mutable public fields are required by the marshaler; treat instances as read-only snapshots.
/// Struct copies share the array instances — copying a page does not deep-copy its arrays.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct AccStaticPage
{
    /// <summary>Marshaled size of the native page in bytes.</summary>
    public const int SizeBytes = 820;

    /// <summary>Shared-memory layout version (e.g. "1.8") — not kept in sync with the game version.</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string SmVersion;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string AcVersion;
    public int NumberOfSessions;
    public int NumCars;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string CarModel;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string Track;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerSurname;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerNick;
    public int SectorCount;

    /// <summary>Not shown in ACC.</summary>
    public float MaxTorque;

    /// <summary>Not shown in ACC.</summary>
    public float MaxPower;
    public int MaxRpm;
    public float MaxFuel;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionMaxTravel;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreRadius;
    public float MaxTurboBoost;
    public float Deprecated1;
    public float Deprecated2;
    public int PenaltiesEnabled;
    public float AidFuelRate;
    public float AidTireRate;
    public float AidMechanicalDamage;
    public int AidAllowTyreBlankets;
    public float AidStability;
    public int AidAutoClutch;

    /// <summary>Always 1 in ACC.</summary>
    public int AidAutoBlip;

    /// <summary>Not used in ACC.</summary>
    public int HasDrs;

    /// <summary>Not used in ACC.</summary>
    public int HasErs;

    /// <summary>Not used in ACC.</summary>
    public int HasKers;

    /// <summary>Not used in ACC.</summary>
    public float KersMaxJ;

    /// <summary>Not used in ACC.</summary>
    public int EngineBrakeSettingsCount;

    /// <summary>Not used in ACC.</summary>
    public int ErsPowerControllerCount;

    /// <summary>Not used in ACC.</summary>
    public float TrackSplineLength;

    /// <summary>Not used in ACC. wchar_t[33] in the native layout — NOT [15].</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TrackConfiguration;

    /// <summary>Not used in ACC.</summary>
    public float ErsMaxJ;

    /// <summary>Not used in ACC.</summary>
    public int IsTimedRace;

    /// <summary>Not used in ACC.</summary>
    public int HasExtraLap;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string CarSkin;

    /// <summary>Not used in ACC.</summary>
    public int ReversedGridPositions;
    public int PitWindowStart;
    public int PitWindowEnd;
    public int IsOnline;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string DryTyresName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string WetTyresName;
}
