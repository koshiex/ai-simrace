using SimCoach.Contracts.V1;

namespace SimCoach.Coach.Gold;

/// <summary>
/// Builds the deterministic per-cadence Gold artifact from a compute domain event plus session context. All of
/// the drop / round / name-resolution policy lives here; the Gold records are dumb data. Reference-relative
/// fields are left <c>null</c> without a reference (so the serializer omits them rather than emitting misleading
/// zeros); no-data session fields drop on their own clean-lap / sentinel precondition. Same input → same output
/// (no timestamps, no ids), so the artifacts are golden-testable.
/// </summary>
public sealed class GoldArtifactBuilder
{
    private const string SchemaVersion = "gold/1";

    private readonly CornerNameMap _names;
    private readonly CoachOptions _options;

    public GoldArtifactBuilder(CornerNameMap names, CoachOptions options)
    {
        _names = names;
        _options = options;
    }

    public GoldArtifact<GoldCornerEvent> BuildCorner(CornerEvent e, GoldSessionContext ctx)
    {
        bool hasRef = ctx.HasReference;
        var payload = new GoldCornerEvent(
            CornerId: e.CornerId,
            CornerName: _names.ResolveName(ctx.TrackId, e.CornerId),
            DeltaMs: hasRef ? e.DeltaMs : null,
            BrakePointDiffM: hasRef ? Rounding.Meters(e.BrakePointDiffM) : null,
            MinSpeedDiffKmh: hasRef ? Rounding.Kmh(e.MinSpeedDiffKmh) : null,
            ThrottleResumeDiffM: hasRef ? Rounding.Meters(e.ThrottleResumeDiffM) : null,
            RacingLineDeviationM: hasRef ? Rounding.Meters(e.RacingLineDeviationM) : null,
            TrailBrakePctSelf: Rounding.Score(e.TrailBrakePctSelf),
            PeakBrakePct: Rounding.Score(e.PeakBrakePct),
            TrailBrakePctRef: hasRef ? Rounding.Score(e.TrailBrakePctRef) : null,
            TrailBrakeDiffPct: hasRef ? Rounding.Score((double)e.TrailBrakePctSelf - e.TrailBrakePctRef) : null,
            UndersteerScore: Rounding.Score(e.UndersteerScore),
            OversteerScore: Rounding.Score(e.OversteerScore),
            WheelspinScore: Rounding.Score(e.WheelspinScore),
            BrakeOverlapSteerPct: Rounding.Score(e.BrakeOverlapSteerPct),
            SteeringJitter: Rounding.Score(e.SteeringJitter),
            OffTrack: e.OffTrack,
            Reason: string.IsNullOrEmpty(e.Reason) ? null : e.Reason)
        {
            CornerNameRu = _names.GetShort(ctx.TrackId, e.CornerId),
        };

        return Envelope("corner", Header(ctx), payload, ctx.Locale);
    }

    public GoldArtifact<GoldSectorEvent> BuildSector(SectorEvent e, GoldSessionContext ctx)
    {
        IReadOnlyList<GoldCornerLoss> losses = Losses(ctx.TrackId, e.TopLosses);
        var payload = new GoldSectorEvent(
            SectorIdx: e.SectorIdx,
            SectorTimeMs: e.SectorTimeMs,
            DeltaMs: ctx.HasReference ? e.DeltaMs : null,
            TopCorner: TopCornerOf(losses),
            TopLosses: losses);

        return Envelope("sector", Header(ctx), payload, ctx.Locale);
    }

    public GoldArtifact<GoldLapEvent> BuildLap(LapEvent e, GoldSessionContext ctx)
    {
        IReadOnlyList<GoldCornerLoss> losses = Losses(ctx.TrackId, e.TopLosses);
        var payload = new GoldLapEvent(
            LapNumber: e.LapNumber,
            LapTimeMs: e.LapTimeMs,
            DeltaMs: ctx.HasReference ? e.DeltaMs : null,
            IsPb: e.IsPb,
            IsClean: e.IsClean,
            TopCorner: TopCornerOf(losses),
            Thermal: Thermal(e.Thermal),
            TopLosses: losses);

        return Envelope("lap", Header(ctx), payload, ctx.Locale);
    }

    public GoldArtifact<GoldSessionPayload> BuildSession(SessionEvent e, GoldSessionContext ctx)
    {
        var payload = new GoldSessionPayload(
            LapCount: e.LapCount,
            CleanLapCount: e.CleanLapCount,
            PbTimeMs: e.PbTimeMs > 0 ? e.PbTimeMs : null,
            AverageLapMs: e.AverageLapMs > 0 ? e.AverageLapMs : null,
            UndersteerTrend: Rounding.Score(e.UndersteerTrend),
            AggregatedLosses: AggregatedLosses(e.TrackId, e.AggregatedLosses),
            SectorAvgDeltaMs: ctx.HasReference ? e.SectorAvgDeltaMs.ToList() : null,
            ConsistencyStddevMs: e.CleanLapCount >= 2 ? Rounding.Stddev(e.ConsistencyStddevMs) : null,
            TheoreticalBestGapMs: e.CleanLapCount >= 1 ? e.TheoreticalBestGapMs : null,
            SetupHint: null,
            FuelTyre: new GoldFuelTyreSummary(Rounding.Fuel(e.AvgFuelPerLapL), Rounding.Percent(e.EndTyreWearPct)),
            Stints: Stints(e.Stints));

        // Session metadata comes off the event itself; only class/has-reference/locale ride the context.
        var header = new GoldSessionBlock(e.TrackId, ctx.CarClass, e.WeatherBucket, null, ctx.HasReference);
        return Envelope("session", header, payload, ctx.Locale);
    }

    private static GoldArtifact<TEvent> Envelope<TEvent>(string cadence, GoldSessionBlock header, TEvent payload, string locale) =>
        new(SchemaVersion, cadence, locale, header, payload);

    private static GoldSessionBlock Header(GoldSessionContext ctx) =>
        new(ctx.TrackId, ctx.CarClass, ctx.WeatherBucket, ctx.LapNumber, ctx.HasReference);

    private static GoldThermalSummary Thermal(LapEvent.Types.ThermalSummary? thermal) =>
        thermal is null
            ? new GoldThermalSummary(0, 0, false, false)
            : new GoldThermalSummary(
                Rounding.Celsius(thermal.MaxTyreTempC),
                Rounding.Celsius(thermal.MaxBrakeTempC),
                thermal.TyreOverheat,
                thermal.BrakeOverheat);

    // The biggest-loss corner name, reusing the already-resolved first loss (top_losses arrives pre-sorted
    // descending from compute). Null — so the field drops — when there were no losses to talk about.
    private static string? TopCornerOf(IReadOnlyList<GoldCornerLoss> losses) =>
        losses.Count > 0 ? losses[0].Corner : null;

    private IReadOnlyList<GoldCornerLoss> Losses(string trackId, IReadOnlyList<CornerLoss> losses) =>
    [
        .. losses.Select(l => new GoldCornerLoss(_names.ResolveName(trackId, l.CornerId), l.DeltaMs, l.Reason)
        {
            CornerNameRu = _names.GetShort(trackId, l.CornerId),
        }),
    ];

    private IReadOnlyList<GoldAggregatedLoss> AggregatedLosses(string trackId, IReadOnlyList<AggregatedLoss> losses) =>
    [
        .. losses
            .OrderByDescending(l => l.TotalLossMs)
            .ThenBy(l => l.CornerId, StringComparer.Ordinal)
            .Take(_options.MaxDebriefLosses)
            .Select(l => new GoldAggregatedLoss(
                _names.ResolveName(trackId, l.CornerId), l.TotalLossMs, l.AvgLossMs, l.SampleCount, l.DominantReason)
            {
                CornerNameRu = _names.GetShort(trackId, l.CornerId),
            }),
    ];

    private static IReadOnlyList<GoldStint> Stints(IReadOnlyList<StintSummary> stints) =>
    [
        .. stints.Select(s => new GoldStint(
            s.StartLap, s.EndLap, s.TyreCompound, Rounding.Percent(s.TyreDegradationPct), s.AvgLapMs)),
    ];
}
