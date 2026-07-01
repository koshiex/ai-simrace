using Microsoft.Extensions.Options;
using SimCoach.LLM;

namespace SimCoach.Coach;

/// <summary>
/// Prices forward estimates from the configured rate card (<c>LlmOptions.Providers[*].Rates[modelId]</c>) and
/// <see cref="RateCardOptions"/> per-cadence token assumptions. Per-lap amortises a cadence's session calls
/// over <see cref="RateCardOptions.TypicalLapsPerSession"/>; per-session sums every cadence's calls. Cached
/// tokens are not modelled (a never-used model has no cache history).
/// </summary>
public sealed class RateCardQuery : IRateCardQuery
{
    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly IOptions<RateCardOptions> _rateCardOptions;

    public RateCardQuery(IOptions<LlmOptions> llmOptions, IOptions<RateCardOptions> rateCardOptions)
    {
        ArgumentNullException.ThrowIfNull(llmOptions);
        ArgumentNullException.ThrowIfNull(rateCardOptions);
        _llmOptions = llmOptions;
        _rateCardOptions = rateCardOptions;
    }

    public Task<decimal> EstimatePerLapUsd(string modelId, CoachCadence cadence, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        RateCardOptions card = _rateCardOptions.Value;
        CadenceEstimate estimate = EstimateFor(card, cadence);
        decimal perLap = PerCall(modelId, estimate) * estimate.CallsPerSession / card.TypicalLapsPerSession;
        return Task.FromResult(perLap);
    }

    public Task<decimal> EstimatePerSessionUsd(string modelId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        decimal total = 0m;
        foreach ((CoachCadence _, CadenceEstimate estimate) in _rateCardOptions.Value.Cadences)
        {
            total += PerCall(modelId, estimate) * estimate.CallsPerSession;
        }

        return Task.FromResult(total);
    }

    private decimal PerCall(string modelId, CadenceEstimate estimate)
    {
        ModelRate rate = FindRate(modelId);
        return (estimate.TypicalInputTokens / 1_000_000m * rate.InputPerMillion)
            + (estimate.TypicalOutputTokens / 1_000_000m * rate.OutputPerMillion);
    }

    private ModelRate FindRate(string modelId)
    {
        foreach (ProviderOptions provider in _llmOptions.Value.Providers.Values)
        {
            if (provider.Rates.TryGetValue(modelId, out ModelRate? rate))
            {
                return rate;
            }
        }

        throw new InvalidOperationException($"No rate configured for model '{modelId}'.");
    }

    private static CadenceEstimate EstimateFor(RateCardOptions card, CoachCadence cadence)
        => card.Cadences.TryGetValue(cadence, out CadenceEstimate? estimate)
            ? estimate
            : throw new InvalidOperationException($"No rate-card estimate configured for cadence '{cadence}'.");
}
