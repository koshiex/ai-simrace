using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class LlmUsageRepositoryTests : RepositoryTestBase
{
    private readonly LlmUsageRepository _usage;

    public LlmUsageRepositoryTests() => _usage = new LlmUsageRepository(Factory);

    [Fact]
    public async Task Insert_round_trips_all_columns_including_provider_and_cached()
    {
        var row = new LlmUsageRow
        {
            SessionId = null,
            TsUtc = new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero),
            ModelId = "google/gemini-2.5-flash-lite",
            Provider = "openrouter-google",
            Cadence = "corner",
            InputTokens = 100,
            OutputTokens = 20,
            CachedInputTokens = 10,
            CostUsd = 0.00029,
            LatencyMs = 250,
            Status = "success",
        };

        await _usage.InsertAsync(row, CancellationToken.None);

        using SqliteConnection connection = Factory.Create();
        LlmUsageRow read = connection.QuerySingle<LlmUsageRow>("SELECT * FROM llm_usage");
        read.Should().BeEquivalentTo(row);
    }
}
