using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class LapRepositoryTests : RepositoryTestBase
{
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;

    public LapRepositoryTests()
    {
        _sessions = new SessionRepository(Factory);
        _laps = new LapRepository(Factory);
        _sessions.Insert(Session("s1"));
    }

    [Fact]
    public void Insert_then_get_by_session_round_trips_ordered()
    {
        // Arrange
        LapRow lap2 = new() { Id = "l2", SessionId = "s1", LapNumber = 2, LapTimeMs = 89000, IsClean = true, S1Ms = 30000 };
        LapRow lap1 = new() { Id = "l1", SessionId = "s1", LapNumber = 1, LapTimeMs = 90000 };

        // Act
        _laps.Insert(lap2);
        _laps.Insert(lap1);
        IReadOnlyList<LapRow> read = _laps.GetBySession("s1");

        // Assert
        read.Should().HaveCount(2);
        read[0].Should().BeEquivalentTo(lap1); // ordered by lap_number
        read[1].Should().BeEquivalentTo(lap2);
    }

    [Fact]
    public void Duplicate_lap_number_in_session_throws()
    {
        // Arrange
        _laps.Insert(new LapRow { Id = "l1", SessionId = "s1", LapNumber = 1, LapTimeMs = 90000 });

        // Act
        Action duplicate = () =>
            _laps.Insert(new LapRow { Id = "l2", SessionId = "s1", LapNumber = 1, LapTimeMs = 91000 });

        // Assert
        duplicate.Should().Throw<SqliteException>();
    }
}
