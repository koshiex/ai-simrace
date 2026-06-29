using SimCoach.LLM;
using SimCoach.Pipeline;

namespace SimCoach.App;

/// <summary>
/// Composition-edge bridge from the producer-owned <see cref="SessionContext"/> to the sim-agnostic
/// <see cref="ISessionIdProvider"/> the cost meter stamps onto <c>llm_usage</c> rows. Reads the resolved id
/// without blocking (returns <c>null</c> until identity has resolved), so the provider-agnostic LLM library
/// never reaches into the telemetry pipeline directly. Mirrors <see cref="AccTrackLengthProvider"/>.
/// </summary>
internal sealed class SessionContextSessionIdProvider : ISessionIdProvider
{
    private readonly SessionContext _sessionContext;

    public SessionContextSessionIdProvider(SessionContext sessionContext)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        _sessionContext = sessionContext;
    }

    public string? CurrentSessionId =>
        _sessionContext.Ready.IsCompletedSuccessfully ? _sessionContext.Ready.Result.SessionId : null;
}
