using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;

namespace SimCoach.Coach.Tests;

/// <summary>
/// Shared fixtures for the Gold builder tests: a builder over the embedded corner names + default options, a
/// session context, and fully-populated proto domain events with known values (so rounding / drops / name
/// resolution are checkable). Proto messages are mutable, so a test may tweak one field after construction.
/// </summary>
internal static class GoldTestData
{
    public static GoldArtifactBuilder Builder() => new(CornerNameMap.Load(), new CoachOptions());

    public static GoldSessionContext Ctx(bool hasReference = true, string track = "spa") =>
        new(track, "gt3", "dry-cool", LapNumber: 7, hasReference);

    public static CornerEvent Corner(string cornerId = "spa_t02") => new()
    {
        CornerId = cornerId,
        DeltaMs = 140,
        BrakePointDiffM = -3.44f,
        MinSpeedDiffKmh = -5.06f,
        TrailBrakePctSelf = 0.22f,
        PeakBrakePct = 0.85f,
        TrailBrakePctRef = 0.41f,
        ThrottleResumeDiffM = -2.75f,
        RacingLineDeviationM = 0.74f,
        EntryLineDeviationM = 0.62f,
        ApexLineDeviationM = -0.44f,
        ExitLineDeviationM = 1.18f,
        BrakeReleaseDiffM = -3.10f,
        OffTrack = false,
        UndersteerScore = 0.71f,
        OversteerScore = 0.12f,
        WheelspinScore = 0.18f,
        BrakeLockupScore = 0.55f,
        ShortShiftScore = 0.42f,
        BrakeOverlapSteerPct = 0.31f,
        SteeringJitter = 0.094f,
        Reason = "low_min_speed",
    };

    public static CornerEvent CornerNeutral(string cornerId = "spa_t02") => new()
    {
        CornerId = cornerId,
        OffTrack = false,
        // A neutral corner still trail-brakes; a proto-default 0 would trip the absolute low-trail-brake action.
        TrailBrakePctSelf = 0.22f,
        Reason = "neutral",
    };

    public static SectorEvent Sector() => new()
    {
        SectorIdx = 1,
        SectorTimeMs = 41230,
        DeltaMs = 180,
        TopLosses =
        {
            new CornerLoss { CornerId = "spa_t05", DeltaMs = 120, Reason = "late_throttle" },
            new CornerLoss { CornerId = "spa_t02", DeltaMs = 90, Reason = "early_brake" },
        },
    };

    public static LapEvent Lap() => new()
    {
        LapNumber = 7,
        LapTimeMs = 139450,
        DeltaMs = 210,
        IsPb = false,
        IsClean = true,
        Thermal = new LapEvent.Types.ThermalSummary
        {
            MaxTyreTempC = 98.64f,
            MaxBrakeTempC = 512.36f,
            TyreOverheat = true,
            BrakeOverheat = false,
        },
        TopLosses =
        {
            new CornerLoss { CornerId = "spa_t08", DeltaMs = 130, Reason = "understeer" },
        },
    };

    public static SessionEvent Session() => new()
    {
        SessionId = "sess-123",
        Sim = "acc",
        TrackId = "spa",
        CarId = "audi_r8_lms_evo_ii",
        WeatherBucket = "dry-cool",
        LapCount = 12,
        CleanLapCount = 4,
        PbTimeMs = 138500,
        AverageLapMs = 139200,
        UndersteerTrend = 0.137f,
        ConsistencyStddevMs = 230.4f,
        TheoreticalBestGapMs = 320,
        AvgFuelPerLapL = 2.834f,
        EndTyreWearPct = 0f,
        SectorAvgDeltaMs = { 120, -30, 45 },
        AggregatedLosses =
        {
            new AggregatedLoss { CornerId = "spa_t02", TotalLossMs = 600, AvgLossMs = 86, SampleCount = 7, DominantReason = "low_min_speed", DominantChannel = "min_speed", DominantChannelValue = 48 },
            new AggregatedLoss { CornerId = "spa_t05", TotalLossMs = 450, AvgLossMs = 64, SampleCount = 7, DominantReason = "late_throttle", DominantChannel = "throttle_resume", DominantChannelValue = 33 },
            new AggregatedLoss { CornerId = "spa_t08", TotalLossMs = 300, AvgLossMs = 50, SampleCount = 6, DominantReason = "understeer" },
            new AggregatedLoss { CornerId = "spa_t99", TotalLossMs = 250, AvgLossMs = 40, SampleCount = 6, DominantReason = "early_brake" },
            new AggregatedLoss { CornerId = "spa_t01", TotalLossMs = 200, AvgLossMs = 30, SampleCount = 6, DominantReason = "oversteer" },
            new AggregatedLoss { CornerId = "spa_t03", TotalLossMs = 100, AvgLossMs = 16, SampleCount = 6, DominantReason = "wheelspin" },
        },
    };
}
