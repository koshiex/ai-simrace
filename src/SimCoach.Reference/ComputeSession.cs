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
    private readonly ReferenceLookup _lookup;
    private readonly ReferenceStore _referenceStore;
    private readonly LapRepository _laps;
    private readonly ITrackLengthProvider _lengths;
    private readonly ComputeOptions _options;
    private readonly ILogger _logger;
    private readonly SessionIdentity _identity;

    private readonly LapSegmenter _lapSegmenter = new();
    private readonly SectorSegmenter _sectorSegmenter = new();
    private readonly List<CornerContribution> _lapLosses = [];
    private readonly SessionLossAccumulator _sessionLosses = new();
    private readonly Dictionary<int, int> _bestSectorMs = [];                    // clean-lap per-sector minima
    private readonly Dictionary<int, (long Sum, int Count)> _sectorDeltaAccum = []; // per-sector delta avg input
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

    private int _runningBestMs = int.MaxValue;
    private int _lapCount;
    private int _cleanLapCount;
    private long _cleanLapSumMs;
    private double _cleanLapSumSqMs;
    private double _fuelPerLapAccum;
    private int _racingLapCount;
    private float _endTyreWearPct;
    private int? _pbTimeMs;
    private double _understeerAccum;
    private double _oversteerAccum;
    private int _balanceCornerCount;
    private float _prevSectorCrossPos;
    private bool _lapPoisoned;
    private TelemetryFrame? _lastFrame;

    public ComputeSession(
        DomainEventFanOut domain,
        TrackModelStore trackModels,
        ReferenceLookup lookup,
        ReferenceStore referenceStore,
        LapRepository laps,
        ITrackLengthProvider lengths,
        ComputeOptions options,
        ILogger logger,
        SessionIdentity identity)
    {
        _domain = domain;
        _trackModels = trackModels;
        _lookup = lookup;
        _referenceStore = referenceStore;
        _laps = laps;
        _lengths = lengths;
        _options = options;
        _logger = logger;
        _identity = identity;
    }

    // A lap is coachable only when it is unpoisoned AND its start-line was observed — the latter drops
    // out-lap frames before the first crossing, which have no bounded lap to attribute samples to.
    private bool CurrentLapCoachable() => !_lapPoisoned && _lapSegmenter.HasStartedLap;

    /// <summary>Processes one frame: corner-exit events, then sector crosses, then lap completion.</summary>
    public void Accept(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lastFrame = frame;
        if (!_started)
        {
            InitSession(frame);
        }

        // M1 poison latch: the first pit/invalid frame poisons the whole accumulating lap. The latch is
        // one-way and only re-armed at the next start-line crossing (ResetForNextLap), so events already
        // published earlier on a lap that later dives into the pit are not un-emitted (frame-level latch
        // limitation; a buffer-and-flush swap would localise here). Trackers still run unconditionally
        // below so their per-lap window state re-arms; only the emit calls are gated.
        if (!CoachableFramePredicate.IsCoachable(frame))
        {
            _lapPoisoned = true;
        }

        foreach (CornerTracker tracker in _cornerTrackers)
        {
            IReadOnlyList<TelemetryFrame>? window = tracker.Accept(frame);
            if (window is not null && CurrentLapCoachable())
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
            session.AggregatedLosses.AddRange(_sessionLosses.Build(_options.AggregatedLossesCap));
            session.SectorAvgDeltaMs.AddRange(SectorAvgDeltas());
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
        _logger.LogInformation(
            "Compute started for {Session}: {Track}/{Car}/{Weather}, model {Source} ({Corners} corners), reference {HasRef}",
            _identity.SessionId, _trackId, _carId, _weatherBucket, _trackModel.Source,
            _trackModel.Corners.Count, _reference is not null);
    }

    private void EmitCorner(Corner corner, IReadOnlyList<TelemetryFrame> window)
    {
        (CornerEvent ev, CornerContribution contribution) = CornerEventBuilder.Build(
            corner, window, _reference, _lapLengthM, _reference?.GridLength ?? 0);
        _domain.Publish(DomainEvent.Corner(ev));
        _lapLosses.Add(contribution);
        _sessionLosses.Accept(contribution);
        _understeerAccum += contribution.UndersteerScore;
        _oversteerAccum += contribution.OversteerScore;
        _balanceCornerCount++;
    }

    private void EmitSector(SectorSplit split, TelemetryFrame frame)
    {
        float endPos = frame.NormalizedCarPosition;
        bool wrapped = endPos < _prevSectorCrossPos;
        float refEndPos = wrapped ? 1f : endPos;

        // A poisoned (pit/invalid/out-lap) crossing must not feed the sector-delta average or emit a tip,
        // but _prevSectorCrossPos MUST keep advancing regardless so the next coachable crossing on the same
        // lap measures its delta from the correct start position (M1: gate publish + accumulation only).
        if (CurrentLapCoachable())
        {
            int deltaMs = 0;
            if (_reference is not null)
            {
                int refSectorMs = GridMetrics.TimeAt(_reference, refEndPos) - GridMetrics.TimeAt(_reference, _prevSectorCrossPos);
                deltaMs = split.SectorTimeMs - refSectorMs;
            }

            (long sum, int count) = _sectorDeltaAccum.GetValueOrDefault(split.SectorIndex);
            _sectorDeltaAccum[split.SectorIndex] = (sum + deltaMs, count + 1);

            var ev = new SectorEvent
            {
                T = frame.T,
                SectorIdx = split.SectorIndex,
                SectorTimeMs = split.SectorTimeMs,
                DeltaMs = deltaMs,
            };
            ev.TopLosses.AddRange(TopLosses(
                _lapLosses.Where(c => c.ApexPosition >= _prevSectorCrossPos && c.ApexPosition <= refEndPos)));
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
            Thermal = new LapEvent.Types.ThermalSummary
            {
                MaxTyreTempC = thermal.MaxTyreTempC,
                MaxBrakeTempC = thermal.MaxBrakeTempC,
                TyreOverheat = thermal.TyreOverheat,
                BrakeOverheat = thermal.BrakeOverheat,
            },
        };
        lapEvent.TopLosses.AddRange(TopLosses(_lapLosses));
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

    private IEnumerable<int> SectorAvgDeltas() =>
        _sectorDeltaAccum
            .OrderBy(pair => pair.Key)
            .Select(pair => (int)(pair.Value.Sum / pair.Value.Count));

    private void RebuildCornerTrackers() =>
        _cornerTrackers = _trackModel.Corners
            .Select(corner => new CornerTracker(corner))
            .ToList();

    private void ResetForNextLap()
    {
        foreach (CornerTracker tracker in _cornerTrackers)
        {
            tracker.Reset();
        }

        _lapLosses.Clear();
        _prevSectorCrossPos = 0f;
        _lapPoisoned = false; // re-arm: a poisoned lap does not poison the whole session.
    }

    private static int? SectorTime(CompletedLap lap, int index) =>
        lap.SectorTimesMs.Count > index ? lap.SectorTimesMs[index] : null;
}
