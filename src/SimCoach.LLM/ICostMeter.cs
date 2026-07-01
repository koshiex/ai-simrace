namespace SimCoach.LLM;

/// <summary>One billable (or failed) LLM call, ready for the cost ledger. <c>SessionId</c> is intentionally
/// absent — PR-F persists it as NULL; Coach supplies the session correlation in a later phase.</summary>
public sealed record LlmCostEntry(
    string ProviderId,
    string ModelId,
    string RouteKey,
    LlmUsage Usage,
    TimeSpan Latency,
    string Status);

/// <summary>Persists per-call token usage and computed USD cost (FR-036/FR-072).</summary>
public interface ICostMeter
{
    Task RecordAsync(LlmCostEntry entry, CancellationToken ct);
}
