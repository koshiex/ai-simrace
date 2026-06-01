namespace SimCoach.LLM;

/// <summary>
/// Abstracts a chat-completion LLM provider behind a strict, schema-enforced contract.
/// Implementations: OpenRouter (default), local fakes for tests.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Send a Gold-tier coaching artifact and return a structured response that conforms to the supplied JSON schema.
    /// Schema violations and timeouts return <see cref="LlmResult.Failure"/>; the caller is expected to fall back to template.
    /// </summary>
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    string ModelId,
    string SystemPrompt,
    string UserPrompt,
    string JsonSchema,
    int MaxOutputTokens,
    TimeSpan Timeout);

public abstract record LlmResult
{
    public sealed record Success(string Json, int InputTokens, int OutputTokens, TimeSpan Latency) : LlmResult;
    public sealed record Failure(string Reason) : LlmResult;
}
