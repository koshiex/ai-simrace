using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class SessionManagerTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset _startedAt = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName());

    private readonly SqliteConnectionFactory _factory;
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;
    private readonly TelemetryFanOut _fanOut = new(new IngestOptions());

    public SessionManagerTests()
    {
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(_factory).Migrate();
        _sessions = new SessionRepository(_factory);
        _laps = new LapRepository(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Creates_directory_and_inserts_row_on_first_frame()
    {
        // Arrange
        SessionContext context = ResolvedContext("20260610-120000-000");
        SessionManager manager = CreateManager(context);
        await manager.StartAsync(CancellationToken.None);

        // Act
        _fanOut.Publish(Frame(weatherBucket: "dry-warm"));
        _fanOut.Complete();
        await manager.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        string expectedDir = Path.Combine(_root, "recordings", "20260610-120000-000");
        Directory.Exists(expectedDir).Should().BeTrue();
        SessionRow? row = _sessions.Get("20260610-120000-000");
        row.Should().NotBeNull();
        row!.McapPath.Should().Be(expectedDir);
        row.StartedAtUtc.Should().Be(_startedAt);
        row.Sim.Should().Be("acc");
        row.TrackId.Should().Be("spa");
        row.CarId.Should().Be("synthetic_gt3");
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task No_frames_means_no_row()
    {
        // Arrange
        SessionManager manager = CreateManager(ResolvedContext("20260610-120000-000"));
        await manager.StartAsync(CancellationToken.None);

        // Act — stream ends before any frame arrives
        _fanOut.Complete();
        await manager.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert — directory is created on Ready, but no row without an identified first frame
        _sessions.Get("20260610-120000-000").Should().BeNull();
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Weather_bucket_finalized_to_settled_value_after_zero_temp_start()
    {
        // Arrange — early frames mis-bucket to "dry-warm" during the temp warm-up, then settle to "wet"
        SessionContext context = ResolvedContext("20260610-120000-000");
        SessionManager manager = CreateManager(context);
        await manager.StartAsync(CancellationToken.None);

        // Act — warm-up frames at t0..t0+20s are "dry-warm"; settled frames near the end are "wet"
        _fanOut.Publish(Frame("dry-warm", _startedAt));
        _fanOut.Publish(Frame("dry-warm", _startedAt.AddSeconds(10)));
        _fanOut.Publish(Frame("wet", _startedAt.AddSeconds(300)));
        _fanOut.Publish(Frame("wet", _startedAt.AddSeconds(305)));
        _fanOut.Publish(Frame("wet", _startedAt.AddSeconds(310)));
        _fanOut.Complete();
        await manager.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert — provisional insert kept "dry-warm"; finalize wrote the settled "wet"
        _sessions.Get("20260610-120000-000")!.WeatherBucket.Should().Be("wet");
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Finalize_counts_and_pb_come_from_persisted_laps()
    {
        // Arrange — laps already persisted (as ComputeService would in PR-E) before the stream ends
        SessionContext context = ResolvedContext("20260610-120000-000");
        SessionManager manager = CreateManager(context);
        await manager.StartAsync(CancellationToken.None);
        _fanOut.Publish(Frame("dry-warm"));
        await WaitForAsync(() => _sessions.Get("20260610-120000-000") is not null);
        _laps.Insert(Lap("l1", lapNumber: 1, lapTimeMs: 105000, isClean: true));
        _laps.Insert(Lap("l2", lapNumber: 2, lapTimeMs: 103000, isClean: true));
        _laps.Insert(Lap("l3", lapNumber: 3, lapTimeMs: 999000, isClean: false));

        // Act
        _fanOut.Complete();
        await manager.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        SessionRow row = _sessions.Get("20260610-120000-000")!;
        row.EndedAtUtc.Should().NotBeNull();
        row.LapCount.Should().Be(3);
        row.CleanLapCount.Should().Be(2);
        row.PbTimeMs.Should().Be(103000, "PB is the fastest clean lap");
        await manager.StopAsync(CancellationToken.None);
    }

    private SessionManager CreateManager(SessionContext context) =>
        new(
            context,
            _fanOut,
            new RecordingOptions { BasePath = Path.Combine(_root, "recordings") },
            _sessions,
            _laps,
            TimeProvider.System,
            NullLogger<SessionManager>.Instance);

    private static SessionContext ResolvedContext(string sessionId)
    {
        SessionContext context = new();
        context.Resolve(sessionId, _startedAt);
        return context;
    }

    private static TelemetryFrame Frame(string weatherBucket, DateTimeOffset? t = null) => new()
    {
        T = Timestamp.FromDateTimeOffset(t ?? _startedAt),
        Sim = "acc",
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = weatherBucket,
        LapNumber = 1,
    };

    private static LapRow Lap(string id, int lapNumber, int lapTimeMs, bool isClean) => new()
    {
        Id = id,
        SessionId = "20260610-120000-000",
        LapNumber = lapNumber,
        LapTimeMs = lapTimeMs,
        IsClean = isClean,
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(_waitTimeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, CancellationToken.None);
        }
    }
}
