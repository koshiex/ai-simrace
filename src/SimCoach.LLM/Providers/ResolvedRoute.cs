namespace SimCoach.LLM.Providers;

/// <summary>The resolved knobs the router hands a provider for one call. Carries <see cref="ProviderId"/>
/// (the breaker-isolation key) but not router-only concerns like the fallback route.</summary>
internal readonly record struct ResolvedRoute(
    string ProviderId,
    string ModelId,
    int MaxOutputTokens,
    TimeSpan Timeout,
    ReasoningEffort Reasoning,
    bool Stream,
    double? Temperature = null,
    double? TopP = null,
    bool CacheSystemPrompt = false);
