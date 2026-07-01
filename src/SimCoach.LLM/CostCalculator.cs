namespace SimCoach.LLM;

/// <summary>
/// USD cost for one call, computed in <see cref="decimal"/> throughout (rates are decimal; only the final
/// <c>cost_usd REAL</c> write narrows to double). <see cref="LlmUsage.InputTokens"/> is inclusive of
/// <see cref="LlmUsage.CachedInputTokens"/>, so cached tokens are billed at the cached rate and subtracted
/// from the full-rate input. Reasoning tokens bill at the output rate.
/// </summary>
internal static class CostCalculator
{
    public static decimal Compute(ModelRate rate, LlmUsage usage)
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(usage);

        int billedInput = Math.Max(0, usage.InputTokens - usage.CachedInputTokens);
        decimal input = billedInput / 1_000_000m * rate.InputPerMillion;
        decimal cached = usage.CachedInputTokens / 1_000_000m * rate.CachedInputPerMillion;
        decimal output = (usage.OutputTokens + usage.ReasoningTokens) / 1_000_000m * rate.OutputPerMillion;
        return input + cached + output;
    }
}
