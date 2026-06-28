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
    public async Task Insert_round_trips_all_columns()
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

        await _tips.InsertAsync(row, CancellationToken.None);

        using SqliteConnection connection = Factory.Create();
        CoachTipRow read = connection.QuerySingle<CoachTipRow>(
            "SELECT session_id, cadence, corner_id, lap_number, action_id, action_label_short, " +
            "rendered_param, priority_phase, priority_rank, severity, phrase_ru, corner_name, " +
            "source, no_pb_yet, provider_model_id, generated_at_utc FROM coach_tips");
        read.Should().BeEquivalentTo(row);
    }

    [Fact]
    public async Task Insert_persists_nullable_columns_as_null()
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

        await _tips.InsertAsync(row, CancellationToken.None);

        IReadOnlyList<CoachTipRow> read = await _tips.GetBySessionAsync("s2", CancellationToken.None);
        read.Should().ContainSingle().Which.Should().BeEquivalentTo(row);
        read[0].CornerId.Should().BeNull();
        read[0].RenderedParam.Should().BeNull();
        read[0].ProviderModelId.Should().BeNull();
        read[0].NoPbYet.Should().BeTrue();
    }

    [Fact]
    public async Task GetBySessionAsync_returns_tips_in_emission_order()
    {
        _sessions.Insert(Session("s1"));
        await _tips.InsertAsync(Tip("s1", "wider_entry"), CancellationToken.None);
        await _tips.InsertAsync(Tip("s1", "brake_later_by_meters"), CancellationToken.None);

        IReadOnlyList<CoachTipRow> read = await _tips.GetBySessionAsync("s1", CancellationToken.None);

        read.Select(t => t.ActionId).Should().Equal("wider_entry", "brake_later_by_meters");
    }

    [Fact]
    public async Task Deleting_a_session_cascades_to_its_coach_tips()
    {
        _sessions.Insert(Session("s1"));
        await _tips.InsertAsync(Tip("s1", "wider_entry"), CancellationToken.None);

        using SqliteConnection connection = Factory.Create();
        connection.Execute("DELETE FROM sessions WHERE id = 's1'");

        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM coach_tips").Should().Be(0);
    }

    [Fact]
    public async Task Insert_with_orphan_session_id_throws()
    {
        Func<Task> orphan = () => _tips.InsertAsync(Tip("missing", "wider_entry"), CancellationToken.None);
        await orphan.Should().ThrowAsync<SqliteException>();
    }

    private CoachTipRow Tip(string sessionId, string actionId) => new()
    {
        SessionId = sessionId,
        Cadence = "corner",
        ActionId = actionId,
        PriorityPhase = "Entry",
        PriorityRank = 80,
        Severity = "Medium",
        PhraseRu = "Шире вход.",
        Source = "Template",
        GeneratedAtUtc = Now,
    };
}
