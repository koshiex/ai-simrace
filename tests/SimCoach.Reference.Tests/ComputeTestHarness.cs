using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Wires a real compute dependency graph (temp SQLite + temp data dirs) and drives a
/// <see cref="ComputeSession"/> over a frame list, collecting the emitted domain events. Lets the
/// compute tests assert on the full event stream and the persisted lap rows without a host.
/// </summary>
internal sealed class ComputeTestHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-compute-" + Guid.NewGuid().ToString("N"));

    public ComputeTestHarness(ITrackLengthProvider? trackLengths = null, CornerGeometryDataset? geometry = null)
    {
        Directory.CreateDirectory(_root);
        var dbOptions = new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") };
        Factory = new SqliteConnectionFactory(dbOptions);
        new DatabaseMigrator(Factory).Migrate();

        ITrackLengthProvider lengths = trackLengths ?? FakeTrackLengths.Spa();
        Laps = new LapRepository(Factory);
        References = new ReferenceRepository(Factory);
        Snapshots = new ReferenceSnapshotRepository(Factory);
        Sessions = new SessionRepository(Factory);
        DomainFanOut = new DomainEventFanOut();

        // Default to the synthetic-Spa baked fixture; the ground-truth gate injects the real vendored
        // geometry (CornerGeometryDataset.Load) + a real track length so Monza frames build non-empty
        // corner windows instead of vacuously passing against zero corners.
        TrackModels = new TrackModelStore(
            geometry ?? BakedGeometryFixture.Spa(),
            lengths,
            NullLogger<TrackModelStore>.Instance);
        Lookup = new ReferenceLookup(References, NullLogger<ReferenceLookup>.Instance);
        OptimalLookup = new OptimalReferenceLookup(References, NullLogger<OptimalReferenceLookup>.Instance);
        ReferenceStore = new ReferenceStore(
            References,
            Snapshots,
            new ReferenceStorageOptions { Directory = Path.Combine(_root, "references") },
            TimeProvider.System,
            NullLogger<ReferenceStore>.Instance);
        _lengths = lengths;
    }

    private readonly ITrackLengthProvider _lengths;

    public SqliteConnectionFactory Factory { get; }

    public LapRepository Laps { get; }

    public ReferenceRepository References { get; }

    public ReferenceSnapshotRepository Snapshots { get; }

    public SessionRepository Sessions { get; }

    public DomainEventFanOut DomainFanOut { get; }

    public TrackModelStore TrackModels { get; }

    public CenterlineGeometryDataset Centerlines { get; } = CenterlineGeometryDataset.Load();

    public ReferenceLookup Lookup { get; }

    public OptimalReferenceLookup OptimalLookup { get; }

    public ReferenceStore ReferenceStore { get; }

    public string ReferencesDirectory => Path.Combine(_root, "references");

    /// <summary>Inserts a minimal sessions row so a reference's <c>source_session_id</c> FK is satisfied.</summary>
    public void SeedSession(string sessionId, ReferenceTriple triple) => Sessions.Insert(new SessionRow
    {
        Id = sessionId,
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        Sim = "acc",
        TrackId = triple.TrackId,
        CarId = triple.CarId,
        WeatherBucket = triple.WeatherBucket,
        McapPath = "unused",
    });

    /// <summary>Feeds the frames through a fresh <see cref="ComputeSession"/> and returns its events.</summary>
    public async Task<IReadOnlyList<DomainEvent>> RunAsync(
        IReadOnlyList<TelemetryFrame> frames, string sessionId = "20260601-120000-000", ComputeOptions? options = null)
    {
        DomainEventSubscription subscription = DomainFanOut.Subscribe("test");
        var identity = new SessionIdentity(sessionId, new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        // The session row is owned by SessionManager in production; insert it here so the lap FK holds.
        Sessions.Insert(NewSessionRow(identity, frames[0]));

        var session = new ComputeSession(
            DomainFanOut, TrackModels, Centerlines, Lookup, OptimalLookup, ReferenceStore, Laps, _lengths,
            options ?? new ComputeOptions(), NullLogger.Instance, identity);
        foreach (TelemetryFrame frame in frames)
        {
            session.Accept(frame);
        }

        session.Complete();

        List<DomainEvent> events = [];
        await foreach (DomainEvent domainEvent in subscription.ReadAllAsync())
        {
            events.Add(domainEvent);
        }

        return events;
    }

    /// <summary>
    /// Drives one <see cref="ComputeSession"/> purely to populate the shared reference store, discarding
    /// its events (its own fan-out has no subscribers). The ground-truth gate calls this first so the
    /// evaluation run compares the flying lap against a reference built from that same lap — mirroring the
    /// production reality that the on-disk reference was overwritten in place by this PB lap (self==ref).
    /// </summary>
    public void SeedReference(
        IReadOnlyList<TelemetryFrame> frames, string sessionId, ComputeOptions? options = null)
    {
        var identity = new SessionIdentity(sessionId, new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        Sessions.Insert(NewSessionRow(identity, frames[0]));
        var session = new ComputeSession(
            new DomainEventFanOut(), TrackModels, Centerlines, Lookup, OptimalLookup, ReferenceStore, Laps, _lengths,
            options ?? new ComputeOptions(), NullLogger.Instance, identity);
        foreach (TelemetryFrame frame in frames)
        {
            session.Accept(frame);
        }

        session.Complete();
    }

    private static SessionRow NewSessionRow(SessionIdentity identity, TelemetryFrame frame) => new()
    {
        Id = identity.SessionId,
        StartedAtUtc = identity.StartedAtUtc,
        Sim = frame.Sim,
        TrackId = frame.TrackId,
        CarId = frame.CarId,
        WeatherBucket = frame.WeatherBucket,
        McapPath = "unused",
    };

    public void Dispose()
    {
        // Release pooled SQLite handles so the temp db file is unlocked before delete (Windows).
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
