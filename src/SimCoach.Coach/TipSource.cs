namespace SimCoach.Coach;

/// <summary>Where a tip's phrase came from: the LLM, a baked template fallback, or a budget-cap downgrade.</summary>
public enum TipSource
{
    Llm,

    /// <summary>Baked template fallback after an LLM failure / validation miss.</summary>
    Template,

    /// <summary>Template emitted because the session or monthly budget cap was hit (no LLM call made).</summary>
    TemplateBudget,
}
