using System.Runtime.InteropServices;

namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Native layout of ACC's <c>Local\acpmf_physics</c> shared-memory page
/// (Kunos shared-memory documentation V1.8.12, struct <c>SPageFilePhysics</c>, pack 4, 800 bytes).
/// Field order, types and array sizes mirror the C++ struct exactly — never reorder or retype;
/// the golden layout tests pin every offset. Wheel arrays are ordered [FL, FR, RL, RR].
/// Mutable public fields are required by the marshaler; treat instances as read-only snapshots.
/// Struct copies share the array instances — copying a page does not deep-copy its arrays.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct AccPhysicsPage
{
    /// <summary>Marshaled size of the native page in bytes.</summary>
    public const int SizeBytes = 800;

    public int PacketId;
    public float Gas;
    public float Brake;
    public float Fuel;

    /// <summary>Native encoding: 0 = reverse, 1 = neutral, 2 = first gear.</summary>
    public int Gear;
    public int Rpm;
    public float SteerAngle;
    public float SpeedKmh;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] Velocity;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] AccG;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelSlip;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelLoad;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelsPressure;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelAngularSpeed;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreWear;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreDirtyLevel;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreCoreTemperature;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] CamberRad;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionTravel;

    /// <summary>Not used in ACC.</summary>
    public float Drs;

    /// <summary>Traction control intervention (0..1).</summary>
    public float Tc;
    public float Heading;
    public float Pitch;
    public float Roll;

    /// <summary>Not used in ACC.</summary>
    public float CgHeight;

    /// <summary>[front, rear, left, right, centre].</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public float[] CarDamage;

    /// <summary>Not used in ACC.</summary>
    public int NumberOfTyresOut;
    public int PitLimiterOn;

    /// <summary>ABS intervention (0..1).</summary>
    public float Abs;

    /// <summary>Not used in ACC.</summary>
    public float KersCharge;

    /// <summary>Not used in ACC.</summary>
    public float KersInput;
    public int AutoShifterOn;

    /// <summary>Not used in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] RideHeight;
    public float TurboBoost;

    /// <summary>Not implemented in ACC.</summary>
    public float Ballast;

    /// <summary>Not used in ACC.</summary>
    public float AirDensity;
    public float AirTemp;
    public float RoadTemp;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalAngularVel;
    public float FinalFf;

    /// <summary>Not used in ACC.</summary>
    public float PerformanceMeter;

    /// <summary>Not used in ACC.</summary>
    public int EngineBrake;

    /// <summary>Not used in ACC.</summary>
    public int ErsRecoveryLevel;

    /// <summary>Not used in ACC.</summary>
    public int ErsPowerLevel;

    /// <summary>Not used in ACC.</summary>
    public int ErsHeatCharging;

    /// <summary>Not used in ACC.</summary>
    public int ErsIsCharging;

    /// <summary>Not used in ACC.</summary>
    public float KersCurrentKj;

    /// <summary>Not used in ACC.</summary>
    public int DrsAvailable;

    /// <summary>Not used in ACC.</summary>
    public int DrsEnabled;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakeTemp;
    public float Clutch;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempI;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempM;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempO;
    public int IsAiControlled;

    /// <summary>Flattened native float[4][3]: [FL, FR, RL, RR] x [x, y, z].</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public float[] TyreContactPoint;

    /// <summary>Flattened native float[4][3]: [FL, FR, RL, RR] x [x, y, z].</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public float[] TyreContactNormal;

    /// <summary>Flattened native float[4][3]: [FL, FR, RL, RR] x [x, y, z].</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public float[] TyreContactHeading;
    public float BrakeBias;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalVelocity;

    /// <summary>Not used in ACC.</summary>
    public int P2PActivations;

    /// <summary>Not used in ACC.</summary>
    public int P2PStatus;
    public int CurrentMaxRpm;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Mz;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Fx;

    /// <summary>Not shown in ACC.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Fy;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SlipRatio;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SlipAngle;

    /// <summary>Not used in ACC.</summary>
    public int TcInAction;

    /// <summary>Not used in ACC.</summary>
    public int AbsInAction;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionDamage;

    /// <summary>Tyre core temperatures (duplicate of <see cref="TyreCoreTemperature"/>).</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTemp;
    public float WaterTemp;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakePressure;
    public int FrontBrakeCompound;
    public int RearBrakeCompound;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] PadLife;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] DiscLife;
    public int IgnitionOn;
    public int StarterEngineOn;
    public int IsEngineRunning;
    public float KerbVibration;
    public float SlipVibrations;
    public float GVibrations;
    public float AbsVibrations;
}
