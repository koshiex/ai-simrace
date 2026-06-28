namespace SimCoach.LLM;

/// <summary>
/// One LLM call. <see cref="RouteKey"/> is opaque (e.g. "corner"/"sector"/"lap"/"debrief"/"strategy");
/// the router maps it to a provider, model id, and per-call knobs — the library is cadence-blind. Per-call
/// token/timeout limits live in route config, not here. <see cref="SchemaName"/> feeds the OpenRouter
/// <c>json_schema.name</c> / the Anthropic tool name.
/// </summary>
public sealed record LlmRequest(
    string RouteKey,
    string SystemPrompt,
    string UserPrompt,
    string JsonSchema,
    string SchemaName);
