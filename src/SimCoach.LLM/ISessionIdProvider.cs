namespace SimCoach.LLM;

/// <summary>
/// Supplies the current session id to the cost meter so <c>llm_usage</c> rows are attributable per session,
/// without the provider-agnostic LLM library reaching into the telemetry pipeline's session context. The App
/// bridges it over the producer-owned <c>SessionContext</c> at the composition edge (mirrors
/// <c>ITrackLengthProvider</c>). Returns <c>null</c> before a session has resolved.
/// </summary>
public interface ISessionIdProvider
{
    string? CurrentSessionId { get; }
}
