using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class CoachTipRepositoryTests : RepositoryTestBase
{
    private readonly CoachTipRepository _tips;
    private readonly SessionRepository _sessions;

    public CoachTipRepositoryTests()
    {
        _tips = new CoachTipRepository(Factory);
        _sessions = new SessionRepository(Factory);
    }

    [Fact]
    public void Insert_round_trips_all_columns()
    {
        _sessions.Insert(Session("s1"));
        var row = new CoachTipRow
        {
            SessionId = "s1",
            Cadence = "corner",
            CornerId = "spa_t02",
            LapNumber = 7,
            ActionId = "brake_later_by_meters",
            ActionLabelShort = "brake_later",
            RenderedParam = "+4м",
            PriorityPhase = "Brake",
            PriorityRank = 120,
            Severity = "High",
            PhraseRu = "В Eau Rouge тормози позже на 4 м.",
            CornerName = "Eau Rouge",
            Source = "Llm",
            NoPbYet = false,
            ProviderModelId = "google/gemini-2.5-flash-lite",
            GeneratedAtUtc = new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero),
        };

        _tips.Insert(row);

        using SqliteConnection connection = Factory.Create();
        CoachTipRow read = connection.QuerySingle<CoachTipRow>("SELECT * FROM coach_tips");
        read.Should().BeEquivalentTo(row);
    }

    [Fact]
    public void Insert_persists_nullable_columns_as_null()
    {
        _sessions.Insert(Session("s2"));
        var row = new CoachTipRow
        {
            SessionId = "s2",
            Cadence = "lap",
            ActionId = "ease_understeer",
            PriorityPhase = "Apex",
            PriorityRank = 60,
            Severity = "Medium",
            PhraseRu = "Меньше газа на входе.",
            Source = "Template",
            NoPbYet = true,
            GeneratedAtUtc = Now,
        };

        _tips.Insert(row);

        using SqliteConnection connection = Factory.Create();
        CoachTipRow read = connection.QuerySingle<CoachTipRow>("SELECT * FROM coach_tips");
        read.Should().BeEquivalentTo(row);
        read.CornerId.Should().BeNull();
        read.RenderedParam.Should().BeNull();
        read.ProviderModelId.Should().BeNull();
        read.NoPbYet.Should().BeTrue();
    }

    [Fact]
    public void Insert_with_orphan_session_id_throws()
    {
        var row = new CoachTipRow
        {
            SessionId = "missing",
            Cadence = "corner",
            ActionId = "wider_entry",
            PriorityPhase = "Entry",
            PriorityRank = 80,
            Severity = "Medium",
            PhraseRu = "Шире вход.",
            Source = "Template",
            GeneratedAtUtc = Now,
        };

        Action orphan = () => _tips.Insert(row);
        orphan.Should().Throw<SqliteException>();
    }
}
