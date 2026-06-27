namespace SimCoach.LLM;

/// <summary>
/// Closed taxonomy of LLM call failures, mirroring <see cref="LlmResult"/>'s closed hierarchy (private
/// base ctor, nested-only) so the variant set is intent-locked. PR-F trips the circuit breaker only on
/// infra failures (<see cref="Timeout"/>/<see cref="RateLimited"/>/<see cref="ServerError"/>/
/// <see cref="Transport"/>); <see cref="SchemaViolation"/> is model-quality (Coach retry/template) and
/// carries the offending <see cref="SchemaViolation.RawText"/>; <see cref="CircuitOpen"/> is breaker-emitted.
/// </summary>
public abstract record LlmFailure
{
    private LlmFailure(string message) => Message = message;

    public string Message { get; init; }

    public sealed record Timeout(string Message) : LlmFailure(Message);

    public sealed record RateLimited(string Message, TimeSpan? RetryAfter) : LlmFailure(Message);

    public sealed record SchemaViolation(string Message, string RawText) : LlmFailure(Message);

    public sealed record Auth(string Message) : LlmFailure(Message);

    public sealed record ServerError(string Message, int StatusCode) : LlmFailure(Message);

    public sealed record Transport(string Message) : LlmFailure(Message);

    public sealed record CircuitOpen(string Message) : LlmFailure(Message);
}
