using FluentAssertions;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class SessionRepositoryTests : RepositoryTestBase
{
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;

    public SessionRepositoryTests()
    {
        _sessions = new SessionRepository(Factory);
        _laps = new LapRepository(Factory);
    }

    [Fact]
    public void Insert_then_get_round_trips_with_null_ended_at()
    {
        // Arrange
        SessionRow row = Session("s1");

        // Act
        _sessions.Insert(row);
        SessionRow? read = _sessions.Get("s1");

        // Assert
        read.Should().BeEquivalentTo(row);
        read!.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Finalize_writes_session_end_fields()
    {
        // Arrange
        _sessions.Insert(Session("s1"));

        // Act
        _sessions.Finalize("s1", Now.AddMinutes(30), lapCount: 12, cleanLapCount: 9, pbTimeMs: 104500,
            parquetPath: "/recordings/s1/laps.parquet");
        SessionRow? read = _sessions.Get("s1");

        // Assert
        read!.EndedAtUtc.Should().Be(Now.AddMinutes(30));
        read.LapCount.Should().Be(12);
        read.CleanLapCount.Should().Be(9);
        read.PbTimeMs.Should().Be(104500);
        read.ParquetPath.Should().Be("/recordings/s1/laps.parquet");
    }

    [Fact]
    public void Delete_cascades_to_laps()
    {
        // Arrange
        _sessions.Insert(Session("s1"));
        _laps.Insert(new LapRow { Id = "l1", SessionId = "s1", LapNumber = 1, LapTimeMs = 90000 });

        // Act
        _sessions.Delete("s1");

        // Assert
        _sessions.Get("s1").Should().BeNull();
        _laps.GetBySession("s1").Should().BeEmpty();
    }

    [Fact]
    public void Get_returns_null_when_absent() => _sessions.Get("missing").Should().BeNull();
}
