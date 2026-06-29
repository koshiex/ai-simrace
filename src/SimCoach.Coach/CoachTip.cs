using SimCoach.Coach.Actions;

namespace SimCoach.Coach;

/// <summary>
/// One coaching tip, carrying everything a sink renders or persists — the console/log sink (P3), Voice (P4),
/// and the overlay card (P5) all consume the same record. <see cref="ActionLabelShort"/> is the authored chip
/// label (not a trimmed id); <see cref="RenderedParam"/> is the quantitative chip value (e.g. <c>+4м</c>).
/// <see cref="Priority"/> is the total-order sort key; <see cref="Severity"/> is its deterministic display
/// band. The full / short / spoken corner-name forms serve the debrief/log, the slim overlay, and the voice
/// path respectively. <see cref="NoPbYet"/> flags a tip generated with no reference (FR-014).
/// <see cref="TopLossesJson"/> / <see cref="SetupHint"/> carry the structured debrief payload (Session cadence
/// only; null on real-time tips) so the loss attribution is persisted now — emitted on both the validated LLM
/// debrief and the deterministic template fallback, so every debrief row is self-renderable. The post-session
/// window that renders it (plus the prose/checklist/balance/audio columns) lands in P6.
/// </summary>
public sealed record CoachTip(
    string SessionId,
    CoachCadence Cadence,
    string? CornerId,
    int? LapNumber,
    string ActionId,
    string? ActionLabelShort,
    string? RenderedParam,
    CoachPriority Priority,
    CoachSeverity Severity,
    string PhraseRu,
    string? CornerName,
    string? CornerNameShort,
    string? CornerNameSpokenRu,
    TipSource Source,
    bool NoPbYet,
    string? ProviderModelId,
    DateTimeOffset GeneratedAtUtc,
    string? TopLossesJson = null,
    string? SetupHint = null);
