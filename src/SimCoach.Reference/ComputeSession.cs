using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Pipeline.Kernels;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Drives the per-session compute for one telemetry stream: segments laps/sectors, runs the C4 kernels
/// over corner windows, derives time-at-position deltas against the reference, and emits the four
/// domain events. Stateful and single-threaded — fed one frame at a time by <see cref="ComputeService"/>
/// and finished once at stream end. Split out from the hosted service so it is unit-testable without a
/// <c>BackgroundService</c>.
/// </summary>
internal sealed class ComputeSession
{
    private readonly DomainEventFanOut _domain;
    private readonly TrackModelStore _trackModels;
    private readonly CenterlineGeometryDataset _centerlines;
    private readonly ReferenceLookup _lookup;
    private readonly OptimalReferenceLookup _optimalLookup;
    private readonly ReferenceStore _referenceStore;
    private readonly LapRepository _laps;
    private readonly ITrackLengthProvider _lengths;
    private readonly ComputeOptions _options;
    private readonly ILogger _logger;
    private readonly SessionIdentity _identity;

    private readonly LapSegmenter _lapSegmenter = new();
    private readonly SectorSegmenter _sectorSegmenter = new();
    private readonly List<CornerContribution> _lapLosses = [];
    // Emission-scoped per-lap corner attribution: filled whenever a corner tip is EMITTED
    // (CurrentLapEmittable), independent of the stricter accumulation gate. Sources the live
    // SectorEvent.TopLosses so a track-limits-invalid flying lap still carries a well-formed corner
    // name in its sector tip, while _lapLosses/_sessionLosses (accumulation-gated) stay empty. On a
    // clean lap emittable==accumulable, so its content equals _lapLosses and clean-lap tips are unchanged.
    private readonly List<CornerContribution> _emitLosses = [];
    private readonly SessionLossAccumulator _sessionLosses = new();
    private readonly Dictionary<int, int> _bestSectorMs = [];        // clean-lap per-sector minima (best = min)
    private readonly Dictionary<int, List<int>> _sectorDeltaAccum = []; // per-sector coachable-crossing deltas (M25: median input)
    private List<CornerTracker> _cornerTrackers = [];

    private bool _started;
    private string _sim = string.Empty;
    private string _trackId = string.Empty;
    private string _carId = string.Empty;
    private string _weatherBucket = string.Empty;
    private ReferenceTriple _triple;
    private float _lapLengthM;
    private bool _hasLength;
    private TrackModel _trackModel = new() { TrackId = string.Empty, Corners = [], Source = TrackModelSource.None };
    private ResampledLap? _reference;
    private ResampledLap? _lineReference;
    // M46: cumulative sector-boundary times (ms from lap start) of the triple's own-optimal, or null when no
    // optimal is persisted. TIME ONLY — read at sector-cross boundaries by index; never a ResampledLap.
    private int[]? _optimalSectorMs;

    private int _runningBestMs = int.MaxValue;
    private int _lapCount;
    private int _cleanLapCount;
    private long _cleanLapSumMs;
    private double _cleanLapSumSqMs;
    private double _fuelPerLapAccum;
    private int _racingLapCount;
    private float _endTyreWearPct;
    private int? _pbTimeMs;
    private int? _bestLapDeficitMs;
    private double _understeerAccum;
    private double _oversteerAccum;
    private int _balanceCornerCount;
    private float _prevSectorCrossPos;
    private bool _lapPoisoned;
    private bool _lapInPit;
    private TelemetryFrame? _lastFrame;

    public ComputeSession(
        DomainEventFanOut domain,
        TrackModelStore trackModels,
        CenterlineGeometryDataset centerlines,
        ReferenceLookup lookup,
        OptimalReferenceLookup optimalLookup,
        ReferenceStore referenceStore,
        LapRepository laps,
        ITrackLengthProvider lengths,
        ComputeOptions options,
        ILogger logger,
        SessionIdentity identity)
    {
        _domain = domain;
        _trackModels = trackModels;
        _centerlines = centerlines;
        _lookup = lookup;
        _optimalLookup = optimalLookup;
        _referenceStore = referenceStore;
        _laps = laps;
        _lengths = lengths;
        _options = options;
        _logger = logger;
        _identity = identity;
    }

    // M1 two-latch design (do NOT re-merge these): live coaching and aggregate/reference accumulation are
    // gated separately so a track-limits-invalid FLYING lap is still coached live while never skewing stats.
    //
    // Emission gate: a lap is emittable only when it is NOT pit-associated AND its start-line was observed
    // (the latter drops out-lap frames before the first crossing, which have no bounded lap to attribute
    // samples to). An IsValidLap=false excursion does NOT block emission — a tiny track-limits cut is a
    // normal lap the driver still wants coached corner-by-corner. Only the pit flag suppresses live tips
    // (out/in-laps are genuinely not being driven for time).
    private bool CurrentLapEmittable() => !_lapInPit && _lapSegmenter.HasStartedLap;

    // Accumulation gate: stricter — a lap feeds session/sector aggregates and can seed the reference only
    // when it is unpoisoned (valid AND out of pit) AND its start-line was observed. A track-limits-cut lap
    // emits live tips but must not contaminate aggregates or become a PB, so accumulation stays gated here.
    private bool CurrentLapAccumulable() => !_lapPoisoned && _lapSegmenter.HasStartedLap;

    /// <summary>Processes one frame: corner-exit events, then sector crosses, then lap completion.</summary>
    public void Accept(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lastFrame = frame;
        if (!_started)
        {
            InitSession(frame);
        }

        // M1 two-latch. Both latches are one-way and only re-armed at the next start-line crossing
        // (ResetForNextLap), so events already published earlier on a lap that later dives into the pit are
        // not un-emitted (frame-level latch limitation; a buffer-and-flush swap would localise here).
        //   _lapPoisoned (accumulation): the first pit OR invalid frame stops this lap feeding aggregates.
        //   _lapInPit    (emission):     only a pit frame silences this lap's live tips — a track-limits
        //                                (IsValidLap=false) excursion still gets coached corner-by-corner.
        // Trackers still run unconditionally below so their per-lap window state re-arms; only the emit and
        // accumulate steps are gated.
        if (!CoachableFramePredicate.IsCoachable(frame))
        {
            _lapPoisoned = true;
        }

        if (frame.IsInPitLane)
        {
            _lapInPit = true;
        }

        foreach (CornerTracker tracker in _cornerTrackers)
        {
            IReadOnlyList<TelemetryFrame>? window = tracker.Accept(frame);
            if (window is not null && CurrentLapEmittable())
            {
                EmitCorner(tracker.Corner, window);
            }
        }

        SectorSplit? split = _sectorSegmenter.Accept(frame);
        if (split is not null)
        {
            EmitSector(split, frame);
        }

        CompletedLap? completed = _lapSegmenter.Accept(frame);
        if (completed is not null)
        {
            HandleLap(completed, frame);
        }

        // A start-line crossing closes a lap's corner/sector accumulation. It must reset state even when
        // the lap segmenter discards the lap (the first crossing has no observed start) — otherwise the
        // corner trackers, which fire once per lap, would stay latched and never re-arm. The crossing
        // verdict comes from the segmenter so the definition lives in exactly one place.
        if (_lapSegmenter.CrossedThisFrame)
        {
            ResetForNextLap();
        }
    }

    /// <summary>Emits the <see cref="SessionEvent"/> aggregate and completes the domain fan-out.</summary>
    public void Complete()
    {
        if (_started && _lastFrame is not null)
        {
            int averageLapMs = _cleanLapCount > 0 ? (int)(_cleanLapSumMs / _cleanLapCount) : 0;
            float understeerTrend = _balanceCornerCount > 0
                ? Math.Clamp((float)((_understeerAccum - _oversteerAccum) / _balanceCornerCount), -1f, 1f)
                : 0f;

            var session = new SessionEvent
            {
                T = _lastFrame.T,
                SessionId = _identity.SessionId,
                Sim = _sim,
                TrackId = _trackId,
                CarId = _carId,
                WeatherBucket = _weatherBucket,
                LapCount = _lapCount,
                CleanLapCount = _cleanLapCount,
                PbTimeMs = _pbTimeMs ?? 0,
                AverageLapMs = averageLapMs,
                UndersteerTrend = understeerTrend,
                ConsistencyStddevMs = ConsistencyStddevMs(),
                TheoreticalBestGapMs = TheoreticalBestGapMs(),
                AvgFuelPerLapL = _racingLapCount > 0 ? (float)(_fuelPerLapAccum / _racingLapCount) : 0f,
                EndTyreWearPct = _endTyreWearPct,
            };
            // Stints are descoped for Phase 2 — the empty repeated field is proto3-valid.
            session.AggregatedLosses.AddRange(PlausibleAggregatedLosses());
            session.SectorAvgDeltaMs.AddRange(PlausibleSectorAvgDeltas());
            (int Gap, IReadOnlyList<int> SectorDeficits)? optimal = OptimalGap();
            if (optimal is { } value)
            {
                session.OptimalGapMs = value.Gap;
                session.SectorOptimalGapMs.AddRange(value.SectorDeficits);
            }

            _domain.Publish(DomainEvent.Session(session));
        }

        if (_lapSegmenter.SuspiciousResetsIgnored > 0)
        {
            _logger.LogWarning(
                "{Count} position reset(s) ignored for session {Session} (pit/teleport or dropped frames, not lap crossings)",
                _lapSegmenter.SuspiciousResetsIgnored, _identity.SessionId);
        }

        _domain.Complete();
    }

    private void InitSession(TelemetryFrame frame)
    {
        _started = true;
        _sim = frame.Sim;
        _trackId = frame.TrackId;
        _carId = frame.CarId;
        _weatherBucket = frame.WeatherBucket;
        _triple = new ReferenceTriple(_trackId, _carId, _weatherBucket);
        _hasLength = _lengths.TryGetLapLengthM(_trackId, out _lapLengthM);
        if (!_hasLength)
        {
            _logger.LogWarning(
                "No lap length for track {Track}; self resample and metre-based corner diffs disabled", _trackId);
        }

        _trackModel = _trackModels.Get(_trackId);
        RebuildCornerTrackers();
        _reference = _lookup.Get(_triple);
        // M38: the LINE reference is the baked median centerline when one is vendored for this track (and
        // trustworthy); otherwise null → CornerEventBuilder falls back to the PB line (ADR-0019).
        _lineReference =
            _hasLength && _centerlines.TryGetCenterline(_trackId, _lapLengthM, out MedianCenterline? centerline)
                ? CenterlineLineReference.Build(centerline!)
                : null;
        // M46: the own-optimal is TIME ONLY — cumulative per-sector best boundaries, read by index at sector
        // crossings. Loaded LAST and fault-isolated so a corrupt optimal row degrades to "no optimal" instead
        // of breaking the reference/centerline setup above.
        _optimalSectorMs = LoadOptimalSectorTimes(frame);
        _logger.LogInformation(
            "Compute started for {Session}: {Track}/{Car}/{Weather}, model {Source} ({Corners} corners), reference {HasRef}, centerline {HasLine}, optimal {HasOptimal}",
            _identity.SessionId, _trackId, _carId, _weatherBucket, _trackModel.Source,
            _trackModel.Corners.Count, _reference is not null, _lineReference is not null, _optimalSectorMs is not null);
    }

    // M46: read the persisted own-optimal for the triple as cumulative sector-boundary times. The sim's sector
    // count comes off the frame; a zero count (no sector plumb yet) or a corrupt stored row disables the
    // optimal deltas silently rather than crashing compute init.
    private int[]? LoadOptimalSectorTimes(TelemetryFrame frame)
    {
        int sectorCount = frame.SectorCount;
        if (sectorCount <= 0)
        {
            return null;
        }

        try
        {
            return _optimalLookup.GetSectorTimes(_triple, sectorCount);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex, "Optimal reference for {Track}/{Car}/{Weather} is corrupt; optimal deltas disabled",
                _trackId, _carId, _weatherBucket);
            return null;
        }
    }

    private void EmitCorner(Corner corner, IReadOnlyList<TelemetryFrame> window)
    {
        (CornerEvent ev, CornerContribution contribution) = CornerEventBuilder.Build(
            corner, window, _reference, _lapLengthM, _reference?.GridLength ?? 0,
            _options.BrakeWindowUpstreamM, _options.ApexWindowFraction, _options.LineRelevanceMaxRadiusM,
            _lineReference);

        // M3 Tier A: an implausibly large corner delta (either sign) is a detection artefact, not real
        // pace. corner_catch_all renders abs(delta_ms), so a -3929 ms gain would voice a fabricated
        // 3929 ms loss. Zero the reference-relative loss (silent fallback, no registry edit) so the
        // catch-all cannot fire and the corner drops out of top-losses; self-derived kernel/balance
        // fields stay intact. Lap deficit is unknown mid-lap, so only the absolute ceiling applies.
        if (!LossPlausibility.WithinCeiling(ev.DeltaMs, _options.MaxPlausibleCornerLossMs))
        {
            _logger.LogDebug(
                "M3 neutralised implausible corner loss {DeltaMs} ms at {CornerId} (ceiling {Ceiling} ms), session {Session}",
                ev.DeltaMs, corner.Id, _options.MaxPlausibleCornerLossMs, _identity.SessionId);
            ev.DeltaMs = 0;
            contribution = contribution with { DeltaMs = 0 };
        }

        // EmitCorner is only reached when CurrentLapEmittable(), so the corner tip always publishes here.
        // Accumulation is stricter: a track-limits-invalid lap emits the live tip but must not feed the
        // session losses or the balance trend, so the accumulation block is gated on CurrentLapAccumulable().
        _domain.Publish(DomainEvent.Corner(ev));
        // Emission-scoped: the live sector tip's corner attribution must exist whenever the corner tip
        // was voiced, so append here (outside the accumulation gate). Aggregates stay strictly gated below.
        _emitLosses.Add(contribution);
        if (CurrentLapAccumulable())
        {
            _lapLosses.Add(contribution);
            _sessionLosses.Accept(contribution);
            _understeerAccum += contribution.UndersteerScore;
            _oversteerAccum += contribution.OversteerScore;
            _balanceCornerCount++;
        }
    }

    private void EmitSector(SectorSplit split, TelemetryFrame frame)
    {
        float endPos = frame.NormalizedCarPosition;
        bool wrapped = endPos < _prevSectorCrossPos;
        float refEndPos = wrapped ? 1f : endPos;

        // A pit/out-lap crossing must not emit a tip, but a track-limits-invalid FLYING crossing still gets
        // a live sector tip (emission gate = pit only). _prevSectorCrossPos MUST keep advancing regardless
        // (outside the gated block below) so the next crossing on the same lap measures its delta from the
        // correct start position. Feeding the sector-delta MEDIAN is stricter — a poisoned (invalid/pit)
        // crossing is excluded from the accumulator so it cannot skew the session aggregate.
        if (CurrentLapEmittable())
        {
            int deltaMs = 0;
            if (_reference is not null)
            {
                int refSectorMs = GridMetrics.TimeAt(_reference, refEndPos) - GridMetrics.TimeAt(_reference, _prevSectorCrossPos);
                deltaMs = split.SectorTimeMs - refSectorMs;
            }

            // M3 Tier A (mirrors EmitCorner): an implausibly large per-crossing sector delta — e.g. an
            // ungated out-lap crossing if M1 regresses — must not voice a fabricated realtime tip via
            // sector_catch_all, nor poison the median. Lap deficit is unknown mid-lap, so only the
            // absolute ceiling applies; zero it (silent fallback) before it feeds the accumulator or event.
            if (!LossPlausibility.WithinCeiling(deltaMs, _options.MaxPlausibleSectorLossMs))
            {
                _logger.LogDebug(
                    "M3 neutralised implausible sector delta {DeltaMs} ms at sector {Sector} (ceiling {Ceiling} ms), session {Session}",
                    deltaMs, split.SectorIndex, _options.MaxPlausibleSectorLossMs, _identity.SessionId);
                deltaMs = 0;
            }

            if (CurrentLapAccumulable())
            {
                if (!_sectorDeltaAccum.TryGetValue(split.SectorIndex, out List<int>? deltas))
                {
                    deltas = [];
                    _sectorDeltaAccum[split.SectorIndex] = deltas;
                }

                deltas.Add(deltaMs);
            }

            var ev = new SectorEvent
            {
                T = frame.T,
                SectorIdx = split.SectorIndex,
                SectorTimeMs = split.SectorTimeMs,
                DeltaMs = deltaMs,
                OptimalDeltaMs = OptimalSectorDelta(split.SectorIndex, split.SectorTimeMs),
            };
            ev.TopLosses.AddRange(TopLosses(
                _emitLosses.Where(c => c.ApexPosition >= _prevSectorCrossPos && c.ApexPosition <= refEndPos)));
            _domain.Publish(DomainEvent.Sector(ev));
        }

        _prevSectorCrossPos = wrapped ? 0f : endPos;
    }

    private void HandleLap(CompletedLap completed, TelemetryFrame frame)
    {
        _lapCount++;
        // Fuel summary averages over coachable racing laps only — an in/out/pit or invalid lap's per-lap
        // estimate is skewed. Shares the frame-level CoachableFramePredicate with the M1 emit-gate (Q2) so
        // both agree on what a racing frame is. The completing `frame` is the next lap's start-line
        // crossing, so read fuel from the completed lap's own last frame, not from `frame`.
        if (completed.Frames.Count > 0 && completed.Frames.All(CoachableFramePredicate.IsCoachable))
        {
            _fuelPerLapAccum += completed.Frames[^1].FuelPerLapL;
            _racingLapCount++;
        }

        bool clean = completed.IsClean;
        if (clean)
        {
            _cleanLapCount++;
            _cleanLapSumMs += completed.LapTimeMs;
            _cleanLapSumSqMs += (double)completed.LapTimeMs * completed.LapTimeMs;
            AccumulateBestSectors(completed);
            _endTyreWearPct = MaxTyreWear(completed.Frames);
        }

        ResampledLap? self = ResampleSelf(completed);
        // Reference lap time is the last grid sample (~1 m short of the line); the missing final metre
        // is sub-0.1% of a lap and within coaching tolerance, so no end-point interpolation is done.
        int? deltaMs = _reference is not null
            ? completed.LapTimeMs - _reference.TMsFromLapStart[^1]
            : null;

        bool isPb = false;
        if (clean && completed.LapTimeMs < _runningBestMs)
        {
            isPb = true;
            _runningBestMs = completed.LapTimeMs;
            _pbTimeMs = completed.LapTimeMs;
            // M3: capture the best lap's deficit HERE, while deltaMs is still measured against the
            // pre-update reference. MaybeUpdate below overwrites _reference with this PB when it beats the
            // stored reference, after which (pbTime - _reference[^1]) would collapse to ~0 and defeat the
            // session-tier deficit budget in Complete(). deltaMs is null only without a reference, in
            // which case the session-tier guard degrades to inert.
            if (deltaMs is not null)
            {
                _bestLapDeficitMs = deltaMs;
            }
        }

        ThermalResult thermal = ThermalKernels.Analyze(completed.Frames);
        var lapEvent = new LapEvent
        {
            T = frame.T,
            LapNumber = completed.LapNumber,
            LapTimeMs = completed.LapTimeMs,
            DeltaMs = deltaMs ?? 0,
            IsPb = isPb,
            IsClean = clean,
            OptimalDeltaMs = OptimalLapDelta(completed.LapTimeMs),
            Thermal = new LapEvent.Types.ThermalSummary
            {
                MaxTyreTempC = thermal.MaxTyreTempC,
                MaxBrakeTempC = thermal.MaxBrakeTempC,
                TyreOverheat = thermal.TyreOverheat,
                BrakeOverheat = thermal.BrakeOverheat,
            },
        };
        // M3 Tier B: drop any lap top-loss whose magnitude cannot fit the lap's own deficit budget. The
        // lap deficit is known at completion (lapEvent.DeltaMs), so a sign-inverted or oversized corner
        // loss is filtered before it reaches either the LLM or the template phrasing path.
        foreach (CornerLoss loss in TopLosses(_lapLosses))
        {
            if (LossPlausibility.WithinDeficit(
                loss.DeltaMs, lapEvent.DeltaMs, _options.LapDeficitLossRatio, _options.LapDeficitFloorMs))
            {
                lapEvent.TopLosses.Add(loss);
            }
            else
            {
                _logger.LogDebug(
                    "M3 dropped implausible lap top-loss {DeltaMs} ms at {CornerId} vs lap deficit {Deficit} ms, session {Session}",
                    loss.DeltaMs, loss.CornerId, lapEvent.DeltaMs, _identity.SessionId);
            }
        }

        _domain.Publish(DomainEvent.Lap(lapEvent));

        // Geometry is baked and fixed for the session (ADR-0014); a clean lap only updates the reference.
        if (clean && self is not null && _referenceStore.MaybeUpdate(_triple, completed, self, _identity))
        {
            _reference = self;
        }

        // Persisting one lap must never take down the host. LapSegmenter renumbers laps to a
        // session-local monotonic sequence so a pit-return duplicate can't violate
        // UNIQUE(session_id, lap_number), but a bad row (or any future storage fault) is still caught
        // here and logged rather than thrown out of the compute loop. Losing one lap row is acceptable;
        // losing the session and the recording is not. Lap cadence means this can never log-flood.
        try
        {
            _laps.Insert(new LapRow
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = _identity.SessionId,
                LapNumber = completed.LapNumber,
                LapTimeMs = completed.LapTimeMs,
                DeltaVsReferenceMs = deltaMs,
                IsPb = isPb,
                IsClean = clean,
                S1Ms = SectorTime(completed, 0),
                S2Ms = SectorTime(completed, 1),
                S3Ms = SectorTime(completed, 2),
                RawOffsetInMcap = null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Lap row insert failed for session {Session} lap {Lap}; continuing",
                _identity.SessionId, completed.LapNumber);
        }
    }

    private ResampledLap? ResampleSelf(CompletedLap completed)
    {
        if (!_hasLength)
        {
            return null;
        }

        try
        {
            return PositionResampler.Resample(completed.Frames, _lapLengthM, completed.LapNumber);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Resample skipped for non-monotonic lap {Lap}", completed.LapNumber);
            return null;
        }
    }

    private List<CornerLoss> TopLosses(IEnumerable<CornerContribution> source) =>
        source
            .Where(c => c.DeltaMs > 0)
            .OrderByDescending(c => c.DeltaMs)
            .Take(_options.TopLossesCount)
            .Select(c => new CornerLoss { CornerId = c.CornerId, DeltaMs = c.DeltaMs, Reason = c.Reason })
            .ToList();

    private void AccumulateBestSectors(CompletedLap completed)
    {
        for (int i = 0; i < completed.SectorTimesMs.Count; i++)
        {
            int sectorMs = completed.SectorTimesMs[i];
            if (!_bestSectorMs.TryGetValue(i, out int best) || sectorMs < best)
            {
                _bestSectorMs[i] = sectorMs;
            }
        }
    }

    private static float MaxTyreWear(IReadOnlyList<TelemetryFrame> frames)
    {
        float max = 0f;
        foreach (TelemetryFrame frame in frames)
        {
            foreach (float wear in frame.TyreWearPct)
            {
                if (wear > max)
                {
                    max = wear;
                }
            }
        }

        return max;
    }

    // Population stddev of clean lap times; undefined for < 2 clean laps → 0 sentinel.
    private float ConsistencyStddevMs()
    {
        if (_cleanLapCount < 2)
        {
            return 0f;
        }

        double mean = (double)_cleanLapSumMs / _cleanLapCount;
        double variance = (_cleanLapSumSqMs / _cleanLapCount) - (mean * mean);
        return variance > 0 ? (float)Math.Sqrt(variance) : 0f;
    }

    // Best clean lap minus the sum of best clean per-sector times; 0 sentinel without a clean PB.
    // Clamped non-negative against partial sector coverage across laps.
    private int TheoreticalBestGapMs()
    {
        if (_cleanLapCount == 0 || _runningBestMs == int.MaxValue || _bestSectorMs.Count == 0)
        {
            return 0;
        }

        int bestSectorsSum = _bestSectorMs.Values.Sum();
        return Math.Max(0, _runningBestMs - bestSectorsSum);
    }

    // M46 TIME-only per-sector optimal delta at a sector crossing: this sector's time minus the persisted
    // cross-session best duration for the SAME sector, read by index from the cumulative boundaries
    // (durationsₖ = cumulativeₖ − cumulativeₖ₋₁). 0 when no optimal is persisted or the index is out of range —
    // never reads a ResampledLap or a mid-sector TimeAt.
    private int OptimalSectorDelta(int sectorIndex, int sectorTimeMs)
    {
        if (_optimalSectorMs is null || sectorIndex < 0 || sectorIndex >= _optimalSectorMs.Length)
        {
            return 0;
        }

        int optimalSectorMs = _optimalSectorMs[sectorIndex] - (sectorIndex > 0 ? _optimalSectorMs[sectorIndex - 1] : 0);
        return sectorTimeMs - optimalSectorMs;
    }

    // M46 TIME-only lap optimal delta: this lap's time minus the optimal target (Σ best sectors = the last
    // cumulative boundary). 0 when no optimal is persisted.
    private int OptimalLapDelta(int lapTimeMs) =>
        _optimalSectorMs is null ? 0 : lapTimeMs - _optimalSectorMs[^1];

    // M46 debrief headline (must-fix #2/#4): the CURRENT-session-aware gap to the cross-session own-optimal.
    // Merges the persisted per-sector optimal with THIS session's best sectors (min per sector), so a sector
    // the driver beat today folds in; gap = PB − Σ merged (clamped ≥ 0, mirroring TheoreticalBestGapMs). The
    // per-sector deficit vector (this-session-best − merged, ≥ 0) ranks where the optimal still holds time.
    // Returns null when no persisted optimal exists (first-session fallback → field 16 shows) or no clean PB
    // was set this session (no baseline lap to measure the gap against).
    private (int Gap, IReadOnlyList<int> SectorDeficits)? OptimalGap()
    {
        if (_optimalSectorMs is null || _runningBestMs == int.MaxValue || _bestSectorMs.Count == 0)
        {
            return null;
        }

        int mergedSum = 0;
        int[] deficits = new int[_optimalSectorMs.Length];
        for (int i = 0; i < _optimalSectorMs.Length; i++)
        {
            int persisted = _optimalSectorMs[i] - (i > 0 ? _optimalSectorMs[i - 1] : 0);
            if (_bestSectorMs.TryGetValue(i, out int best))
            {
                int merged = Math.Min(persisted, best);
                mergedSum += merged;
                deficits[i] = best - merged;
            }
            else
            {
                mergedSum += persisted;
                deficits[i] = 0;
            }
        }

        return (Math.Max(0, _runningBestMs - mergedSum), deficits);
    }

    // M25 (Q4): the per-sector session aggregate is the MEDIAN of the coachable-lap crossing deltas, not
    // the mean. The mean let one anomalous crossing invert the sign of a whole sector's reported loss.
    // The proto field is still named sector_avg_delta_ms (field 14) — only the estimator changed; the
    // wire contract is untouched. This is separate from _bestSectorMs (min), which drives best-sector
    // highlighting and must not be conflated with loss attribution.
    private IEnumerable<int> SectorAvgDeltas() =>
        _sectorDeltaAccum
            .OrderBy(pair => pair.Key)
            .Select(pair => SectorDeltaAggregator.Median(pair.Value));

    // M3 Tier B on the session aggregate: drop any corner whose per-occurrence average loss cannot fit
    // the best lap's deficit budget. _bestLapDeficitMs is captured pre-overwrite in HandleLap (a PB
    // overwrites the reference), so the budget survives even when self==reference by Complete(). Without a
    // captured deficit (no reference/PB) the guard degrades to inert — it never fabricates a budget.
    private IEnumerable<AggregatedLoss> PlausibleAggregatedLosses()
    {
        IReadOnlyList<AggregatedLoss> losses = _sessionLosses.Build(_options.AggregatedLossesCap);
        if (_bestLapDeficitMs is not int deficit)
        {
            return losses;
        }

        List<AggregatedLoss> kept = [];
        foreach (AggregatedLoss loss in losses)
        {
            if (LossPlausibility.WithinDeficit(
                loss.AvgLossMs, deficit, _options.LapDeficitLossRatio, _options.LapDeficitFloorMs))
            {
                kept.Add(loss);
            }
            else
            {
                _logger.LogDebug(
                    "M3 dropped implausible aggregated loss avg {AvgLossMs} ms at {CornerId} vs best-lap deficit {Deficit} ms, session {Session}",
                    loss.AvgLossMs, loss.CornerId, deficit, _identity.SessionId);
            }
        }

        return kept;
    }

    // M3 Tier B on the per-sector median deltas. A sector whose median cannot fit the deficit budget
    // (e.g. a poisoned +14799 ms S1 on a lap that gained 1381 ms) is NEUTRALISED to 0 rather than removed
    // — the list is positional (sector 0,1,2), so dropping an element would mis-index the debrief.
    // Compared only against the lap deficit, never the sector absolute time (the 14799 < 35994 trap).
    private IEnumerable<int> PlausibleSectorAvgDeltas()
    {
        IEnumerable<int> deltas = SectorAvgDeltas();
        if (_bestLapDeficitMs is not int deficit)
        {
            return deltas;
        }

        List<int> guarded = [];
        foreach (int delta in deltas)
        {
            if (LossPlausibility.WithinDeficit(delta, deficit, _options.LapDeficitLossRatio, _options.LapDeficitFloorMs))
            {
                guarded.Add(delta);
            }
            else
            {
                _logger.LogDebug(
                    "M3 neutralised implausible sector median delta {DeltaMs} ms vs best-lap deficit {Deficit} ms, session {Session}",
                    delta, deficit, _identity.SessionId);
                guarded.Add(0);
            }
        }

        return guarded;
    }

    private void RebuildCornerTrackers()
    {
        // M16: arm each tracker a fixed metric distance upstream of the corner start so the braking zone
        // is buffered. Without a lap length the metre→normalized conversion is undefined and brake diffs
        // are disabled anyway, so the window stays at the geometric start (no widening).
        float upstreamNormalized = _hasLength && _lapLengthM > 0f
            ? _options.BrakeWindowUpstreamM / _lapLengthM
            : 0f;
        _cornerTrackers = _trackModel.Corners
            .Select(corner => new CornerTracker(corner, upstreamNormalized))
            .ToList();
    }

    private void ResetForNextLap()
    {
        foreach (CornerTracker tracker in _cornerTrackers)
        {
            tracker.Reset();
        }

        _lapLosses.Clear();
        _emitLosses.Clear();
        _prevSectorCrossPos = 0f;
        _lapPoisoned = false; // re-arm the accumulation latch: a poisoned lap does not poison the session.
        _lapInPit = false;    // re-arm the emission latch: a pit lap does not silence later flying laps.
    }

    private static int? SectorTime(CompletedLap lap, int index) =>
        lap.SectorTimesMs.Count > index ? lap.SectorTimesMs[index] : null;
}
