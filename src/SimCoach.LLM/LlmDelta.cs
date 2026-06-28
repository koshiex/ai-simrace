namespace SimCoach.LLM;

/// <summary>One streaming chunk (declared for P6; never produced in Phase 3). A non-null
/// <see cref="FinishReason"/> marks the terminal delta.</summary>
public sealed record LlmDelta(string TextChunk, string? FinishReason);
