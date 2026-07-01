namespace SimCoach.Coach;

/// <summary>
/// <c>CoachService</c> composition knobs. <see cref="LlmLive"/> stays <c>false</c> through Phase 3 — the
/// registered <c>ILlmClient</c> routes to <c>FakeProvider</c>, so no live network call is ever made; a later
/// PR flips it behind the host's <c>Llm:Live</c> flag.
/// </summary>
public sealed class CoachServiceOptions
{
    public bool LlmLive { get; init; }
}
