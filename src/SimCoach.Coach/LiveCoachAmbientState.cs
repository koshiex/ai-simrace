using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.Coach;

/// <summary>
/// The real <see cref="ICoachAmbientState"/> for the live host: a gate-only <see cref="TelemetryFanOut"/>
/// subscriber (M7) that maintains the latest-frame gate snapshot and the session metadata
/// (<see cref="GoldSessionContext"/>) the corner/sector/lap events do not carry. The latest snapshot is
/// published as an immutable reference swapped with <see cref="Volatile.Write{T}"/> so the reader
/// (<c>CoachService</c>, a different thread) never sees a torn multi-field struct. Per-session lookups
/// (car class, has-reference, track geometry) are cached and recomputed only when the <c>(track, car,
/// weather)</c> triple changes, so the 333 Hz frame loop does no per-frame I/O. Before the first frame it
/// reports the no-frame sentinel, so frame-dependent gates fail open exactly like the placeholder did.
/// </summary>
public sealed class LiveCoachAmbientState : BackgroundService, ICoachAmbientState
{
    private const int YellowFlagBit = 1 << 1; // flags_active bit 1 = yellow (see telemetry.proto)

    private readonly TelemetrySubscription _subscription;
    private readonly ICarClassProvider _carClasses;
    private readonly ReferenceRepository _references;
    private readonly TrackModelStore _trackModels;
    private readonly CornerPhaseResolver _cornerPhase;
    private readonly ILogger<LiveCoachAmbientState> _logger;

    private Snapshot _snapshot = Snapshot.Initial;

    public LiveCoachAmbientState(
        TelemetryFanOut fanOut,
        ICarClassProvider carClasses,
        ReferenceRepository references,
        TrackModelStore trackModels,
        CornerPhaseResolver cornerPhase,
        ILogger<LiveCoachAmbientState> logger)
    {
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(carClasses);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(trackModels);
        ArgumentNullException.ThrowIfNull(cornerPhase);
        ArgumentNullException.ThrowIfNull(logger);
        _carClasses = carClasses;
        _references = references;
        _trackModels = trackModels;
        _cornerPhase = cornerPhase;
        _logger = logger;
        _subscription = fanOut.Subscribe("coach-gate");
    }

    public GoldSessionContext SessionMetadata() => Volatile.Read(ref _snapshot).Metadata;

    public GateSnapshot LatestGate() => Volatile.Read(ref _snapshot).Gate;

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Per-triple cache: the frame loop is single-consumer, so these stay loop-local and only the
        // immutable Snapshot is published. Recomputed when the (track, car, weather) triple changes.
        string? cachedTriple = null;
        string carClass = "unknown";
        bool hasReference = false;
        IReadOnlyList<Corner> corners = [];

        double previousSteer = 0;
        DateTimeOffset? previousAt = null;

        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                string triple = $"{frame.TrackId}|{frame.CarId}|{frame.WeatherBucket}";
                if (triple != cachedTriple)
                {
                    cachedTriple = triple;
                    carClass = _carClasses.TryGetCarClass(frame.CarId, out string resolved) ? resolved : "unknown";
                    hasReference = _references.GetByTriple(frame.TrackId, frame.CarId, frame.WeatherBucket) is not null;
                    corners = _trackModels.Get(frame.TrackId).Corners;
                }

                var now = frame.T.ToDateTimeOffset();
                double steer = frame.SteerRad;
                double steerRate = previousAt is { } prev && now > prev
                    ? (steer - previousSteer) / (now - prev).TotalSeconds
                    : 0.0;
                previousSteer = steer;
                previousAt = now;

                var gate = new GateSnapshot(
                    Brake: frame.BrakePct,
                    Steer: steer,
                    SteerRate: steerRate,
                    SpeedKmh: frame.SpeedMps * 3.6,
                    OffTrack: frame.TyresOut > 0,
                    Contact: false, // no per-frame contact signal in the telemetry contract yet
                    NormalizedCarPosition: frame.NormalizedCarPosition,
                    CornerPhase: _cornerPhase.Resolve(frame.NormalizedCarPosition, corners),
                    SessionState: MapSessionState(frame),
                    HasFrame: true);

                var metadata = new GoldSessionContext(
                    TrackId: frame.TrackId,
                    CarClass: carClass,
                    WeatherBucket: frame.WeatherBucket,
                    LapNumber: frame.LapNumber,
                    HasReference: hasReference);

                Volatile.Write(ref _snapshot, new Snapshot(metadata, gate));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: frames stop at IngestService (which stops first); a stale gate is harmless.
        }
        finally
        {
            _logger.LogInformation("Coach ambient gate feed stopped");
        }
    }

    private static SessionFlag MapSessionState(TelemetryFrame frame)
    {
        if (frame.IsInPitLane || frame.IsInPit)
        {
            return SessionFlag.Pit;
        }

        return (frame.FlagsActive & YellowFlagBit) != 0 ? SessionFlag.Yellow : SessionFlag.Green;
    }

    /// <summary>The atomically-swapped latest view; both members move together so reads stay consistent.</summary>
    private sealed record Snapshot(GoldSessionContext Metadata, GateSnapshot Gate)
    {
        // Pre-frame sentinel: no live frame (gates fail open), no reference, until the first frame arrives.
        public static Snapshot Initial { get; } = new(
            new GoldSessionContext("unknown", "unknown", "unknown", 0, false),
            GateSnapshot.Unknown);
    }
}
