namespace SimCoach.LLM;

/// <summary>
/// Provider/runtime metadata for one call. <see cref="ProviderId"/> is the keyed registration id — the
/// circuit-breaker isolation key (e.g. "openrouter-google"/"openrouter-anthropic"), NOT a shared gateway
/// brand. <see cref="ProviderModelId"/> is the resolved upstream model id the cost meter prices against,
/// so it must equal the resolved route's model id.
/// </summary>
public sealed record LlmCallInfo(
    string ProviderId,
    string ProviderModelId,
    TimeSpan Latency,
    string? FinishReason);
