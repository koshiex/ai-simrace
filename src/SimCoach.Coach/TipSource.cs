namespace SimCoach.Coach;

/// <summary>Whether a tip's phrase came from the LLM or a baked template fallback.</summary>
public enum TipSource
{
    Llm,
    Template,
}
