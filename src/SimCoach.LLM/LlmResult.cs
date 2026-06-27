namespace SimCoach.LLM;

/// <summary>Closed result of <see cref="ILlmClient.CompleteAsync"/>: a success payload or a structured failure.</summary>
public abstract record LlmResult
{
    private LlmResult()
    {
    }

    public sealed record Success(string Json, LlmUsage Usage, LlmCallInfo Info) : LlmResult;

    public sealed record Failure(LlmFailure Error) : LlmResult;
}
