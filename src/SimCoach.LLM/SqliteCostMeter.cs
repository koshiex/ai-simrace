using Microsoft.Extensions.Options;
using SimCoach.Storage.Repositories;

namespace SimCoach.LLM;

/// <summary>
/// Prices a call from the configured rate card (<c>LlmOptions.Providers[providerId].Rates[modelId]</c>,
/// never hard-coded) and appends an <c>llm_usage</c> row. A missing rate yields a zero-cost row rather than
/// dropping the record (rate coverage is guaranteed by ValidateOnStart #1; this is defense-in-depth so a
/// failure row still lands). <c>session_id</c> is NULL in PR-F.
/// </summary>
public sealed class SqliteCostMeter : ICostMeter
{
    private readonly LlmUsageRepository _repository;
    private readonly IOptions<LlmOptions> _options;
    private readonly TimeProvider _timeProvider;

    public SqliteCostMeter(LlmUsageRepository repository, IOptions<LlmOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _options = options;
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
            SessionId = null,
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
