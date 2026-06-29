using Microsoft.Extensions.Options;
using SimCoach.Storage.Repositories;

namespace SimCoach.LLM;

/// <summary>
/// Prices a call from the configured rate card (<c>LlmOptions.Providers[providerId].Rates[modelId]</c>,
/// never hard-coded) and appends an <c>llm_usage</c> row stamped with the current session id (from
/// <see cref="ISessionIdProvider"/>). A missing rate yields a zero-cost row rather than dropping the record
/// (rate coverage is guaranteed by ValidateOnStart #1; this is defense-in-depth so a failure row still lands).
/// Takes <c>IOptions</c> (capture-once), not <c>IOptionsMonitor</c>, deliberately: the rate card is static
/// appsettings (settings writes only swap a route's model / the budget / Llm:Live, never <c>Providers[].Rates</c>),
/// and a frozen rate card mid-run is desirable — pricing must not shift under an in-flight session.
/// </summary>
public sealed class SqliteCostMeter : ICostMeter
{
    private readonly LlmUsageRepository _repository;
    private readonly IOptions<LlmOptions> _options;
    private readonly ISessionIdProvider _sessionIds;
    private readonly TimeProvider _timeProvider;

    public SqliteCostMeter(
        LlmUsageRepository repository,
        IOptions<LlmOptions> options,
        ISessionIdProvider sessionIds,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionIds);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _options = options;
        _sessionIds = sessionIds;
        _timeProvider = timeProvider;
    }

    public async Task RecordAsync(LlmCostEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        decimal cost = TryGetRate(entry.ProviderId, entry.ModelId, out ModelRate? rate)
            ? CostCalculator.Compute(rate!, entry.Usage)
            : 0m;

        var row = new LlmUsageRow
        {
            SessionId = _sessionIds.CurrentSessionId,
            TsUtc = _timeProvider.GetUtcNow(),
            ModelId = entry.ModelId,
            Provider = entry.ProviderId,
            Cadence = entry.RouteKey,
            InputTokens = entry.Usage.InputTokens,
            OutputTokens = entry.Usage.OutputTokens,
            CachedInputTokens = entry.Usage.CachedInputTokens,
            CostUsd = (double)cost,
            LatencyMs = (int)entry.Latency.TotalMilliseconds,
            Status = entry.Status,
        };

        await _repository.InsertAsync(row, ct).ConfigureAwait(false);
    }

    private bool TryGetRate(string providerId, string modelId, out ModelRate? rate)
    {
        rate = null;
        return _options.Value.Providers.TryGetValue(providerId, out ProviderOptions? provider)
            && provider.Rates.TryGetValue(modelId, out rate);
    }
}
