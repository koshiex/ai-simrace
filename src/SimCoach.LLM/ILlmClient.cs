namespace SimCoach.LLM;

/// <summary>
/// Provider-agnostic chat-completion seam. The caller passes an opaque <see cref="LlmRequest.RouteKey"/>;
/// the router resolves it to a provider, model, and knobs. No provider- or cadence-specific type crosses
/// this boundary, so adding a provider needs only a keyed registration plus config.
/// </summary>
public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);

    /// <summary>Declared for P6 streaming; implementations throw <see cref="NotSupportedException"/> in Phase 3.</summary>
    IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct);
}
