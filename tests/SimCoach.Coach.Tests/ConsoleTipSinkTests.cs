using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SimCoach.Coach.Actions;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ConsoleTipSinkTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");

    private readonly SqliteConnectionFactory _factory;

    public ConsoleTipSinkTests()
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
    public async Task EmitTipAsync_logs_and_persists_mapped_row()
    {
        new SessionRepository(_factory).Insert(NewSession("s1"));
        var logger = new CapturingLogger<ConsoleTipSink>();
        var sink = new ConsoleTipSink(new CoachTipRepository(_factory), logger);
        var tip = new CoachTip(
            SessionId: "s1",
            Cadence: CoachCadence.Corner,
            CornerId: "spa_t02",
            LapNumber: 7,
            ActionId: "brake_later_by_meters",
            ActionLabelShort: "brake_later",
            RenderedParam: "+4м",
            Priority: new CoachPriority(CoachPhase.Brake, 120),
            Severity: CoachSeverity.High,
            PhraseRu: "В Eau Rouge тормози позже на 4 м.",
            CornerName: "Eau Rouge",
            CornerNameShort: "О-Руж",
            CornerNameSpokenRu: "Эст-Руж",
            Source: TipSource.Llm,
            NoPbYet: false,
            ProviderModelId: "google/gemini-2.5-flash-lite",
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero));

        await sink.EmitTipAsync(tip, CancellationToken.None);

        using SqliteConnection connection = _factory.Create();
        CoachTipRow read = connection.QuerySingle<CoachTipRow>("SELECT * FROM coach_tips");
        read.Should().BeEquivalentTo(new CoachTipRow
        {
            SessionId = "s1",
            Cadence = "Corner",
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
        });
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information && e.Message.Contains("brake_later_by_meters"));
    }

    private static SessionRow NewSession(string id) => new()
    {
        Id = id,
        StartedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        Sim = "acc",
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = "dry-warm",
        McapPath = $"/recordings/{id}",
    };

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
