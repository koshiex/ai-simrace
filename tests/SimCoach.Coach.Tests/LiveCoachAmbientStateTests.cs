using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class LiveCoachAmbientStateTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly TelemetryFanOut _fanOut = new(new IngestOptions());

    public LiveCoachAmbientStateTests()
    {
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = _dbPath });
        new DatabaseMigrator(_factory).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string? dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Maps_the_latest_frame_to_gate_and_session_metadata()
    {
        LiveCoachAmbientState ambient = NewAmbient();
        await ambient.StartAsync(CancellationToken.None);

        _fanOut.Publish(Frame(speedMps: 50f, brake: 0.30f, steer: 0.20f, position: 0.5f, lap: 3));
        _fanOut.Complete(); // ends the read loop so the test is deterministic (no polling)
        await ambient.ExecuteTask!;

        GateSnapshot gate = ambient.LatestGate();
        gate.HasFrame.Should().BeTrue();
        gate.SpeedKmh.Should().BeApproximately(180.0, 1e-6);
        gate.Brake.Should().BeApproximately(0.30, 1e-6);
        gate.OffTrack.Should().BeFalse();
        gate.SessionState.Should().Be(SessionFlag.Green);
        gate.CornerPhase.Should().Be(GateCornerPhase.None); // stub track has no baked geometry

        GoldSessionContext meta = ambient.SessionMetadata();
        meta.TrackId.Should().Be("spa");
        meta.CarClass.Should().Be("gt3");
        meta.WeatherBucket.Should().Be("dry-warm");
        meta.HasReference.Should().BeFalse();

        ambient.Dispose();
    }

    [Fact]
    public async Task Maps_off_track_and_pit_flag()
    {
        LiveCoachAmbientState ambient = NewAmbient();
        await ambient.StartAsync(CancellationToken.None);

        TelemetryFrame frame = Frame(speedMps: 5f, brake: 0f, steer: 0f, position: 0.0f, lap: 1);
        frame.TyresOut = 2;
        frame.IsInPitLane = true;
        _fanOut.Publish(frame);
        _fanOut.Complete();
        await ambient.ExecuteTask!;

        GateSnapshot gate = ambient.LatestGate();
        gate.OffTrack.Should().BeTrue();
        gate.SessionState.Should().Be(SessionFlag.Pit);

        ambient.Dispose();
    }

    [Fact]
    public void Reports_the_no_frame_sentinel_before_any_frame()
    {
        LiveCoachAmbientState ambient = NewAmbient();

        ambient.LatestGate().HasFrame.Should().BeFalse();
        ambient.SessionMetadata().HasReference.Should().BeFalse();

        ambient.Dispose();
    }

    private LiveCoachAmbientState NewAmbient() => new(
        _fanOut,
        new StubCarClass(),
        new ReferenceRepository(_factory),
        new TrackModelStore(CornerGeometryDataset.Load(), new NoTrackLengths(), NullLogger<TrackModelStore>.Instance),
        new CornerPhaseResolver(new RuleEngineOptions()),
        NullLogger<LiveCoachAmbientState>.Instance);

    private static TelemetryFrame Frame(float speedMps, float brake, float steer, float position, int lap) => new()
    {
        T = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero)),
        TrackId = "spa",
        CarId = "ferrari_296_gt3",
        WeatherBucket = "dry-warm",
        LapNumber = lap,
        SpeedMps = speedMps,
        BrakePct = brake,
        SteerRad = steer,
        NormalizedCarPosition = position,
    };

    private sealed class StubCarClass : ICarClassProvider
    {
        public bool TryGetCarClass(string carId, out string carClass)
        {
            carClass = "gt3";
            return true;
        }
    }

    private sealed class NoTrackLengths : ITrackLengthProvider
    {
        public bool TryGetLapLengthM(string trackId, out float lengthM)
        {
            lengthM = 0f;
            return false;
        }
    }
}
