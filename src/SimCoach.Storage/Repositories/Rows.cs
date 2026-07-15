namespace SimCoach.Storage.Repositories;

/// <summary>Row of the <c>sessions</c> table. Nullability mirrors the schema in data-model.md.</summary>
public sealed record SessionRow
{
    public required string Id { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public required string Sim { get; init; }
    public required string TrackId { get; init; }
    public required string CarId { get; init; }
    public required string WeatherBucket { get; init; }
    public int LapCount { get; init; }
    public int CleanLapCount { get; init; }
    public int? PbTimeMs { get; init; }
    public required string McapPath { get; init; }
    public string? ParquetPath { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Row of the <c>laps</c> table.</summary>
public sealed record LapRow
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required int LapNumber { get; init; }
    public required int LapTimeMs { get; init; }
    public int? DeltaVsReferenceMs { get; init; }
    public bool IsPb { get; init; }
    public bool IsClean { get; init; }
    public int? S1Ms { get; init; }
    public int? S2Ms { get; init; }
    public int? S3Ms { get; init; }
    public long? RawOffsetInMcap { get; init; }
}

/// <summary>Row of the <c>llm_usage</c> cost ledger. <c>Provider</c> and <c>CachedInputTokens</c> arrive in
/// migration 002 (PR-F); <c>ReasoningTokens</c> in migration 005 (M28); <c>SessionId</c> is nullable (NULL in
/// PR-F — Coach supplies it in a later phase).</summary>
public sealed record LlmUsageRow
{
    public string? SessionId { get; init; }
    public required DateTimeOffset TsUtc { get; init; }
    public required string ModelId { get; init; }
    public string? Provider { get; init; }
    public required string Cadence { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CachedInputTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public required double CostUsd { get; init; }
    public int LatencyMs { get; init; }
    public required string Status { get; init; }
}

/// <summary>Row of the <c>coach_tips</c> log (one per emitted tip, PR-G / D8). The <c>CoachTip</c> DTO's short
/// and spoken corner-name forms are voice-layer-only and intentionally not persisted here.</summary>
public sealed record CoachTipRow
{
    public required string SessionId { get; init; }
    public required string Cadence { get; init; }
    public string? CornerId { get; init; }
    public int? LapNumber { get; init; }
    public required string ActionId { get; init; }
    public string? ActionLabelShort { get; init; }
    public string? RenderedParam { get; init; }
    public required string PriorityPhase { get; init; }
    public required int PriorityRank { get; init; }
    public required string Severity { get; init; }
    public required string PhraseRu { get; init; }
    public string? CornerName { get; init; }
    public required string Source { get; init; }
    public bool NoPbYet { get; init; }
    public string? ProviderModelId { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    // Structured debrief payload (Session cadence only; null otherwise). The remaining 004 columns
    // (debrief_prose, checklist_json, per_sector_deltas_json, balance_verdict, audio_artifact_ref) stay
    // reserved for the P6 debrief-delivery path.
    public string? TopLossesJson { get; init; }
    public string? SetupHint { get; init; }
}

/// <summary>Row of the <c>references</c> table (one active reference per <c>(track, car, weather, kind)</c>,
/// ADR-0021). Each kind is read through a non-overlapping facet.</summary>
public sealed record ReferenceRow
{
    public required string Id { get; init; }
    public required string TrackId { get; init; }
    public required string CarId { get; init; }
    public required string WeatherBucket { get; init; }
    public string? SourceSessionId { get; init; }
    public int? SourceLapNumber { get; init; }
    public required int LapTimeMs { get; init; }

    /// <summary>Path to the resampled-lap parquet. Null iff <see cref="Kind"/> is <c>"optimal"</c> (the
    /// row-only own-optimal kind has no parquet); non-null for <c>"pb"</c>/<c>"alien_line"</c>.</summary>
    public string? ParquetPath { get; init; }

    public bool Pinned { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>The DB <c>kind</c> discriminator string (<c>"pb"</c> / <c>"optimal"</c>). The
    /// <c>ReferenceKind</c> enum + mapping live in <c>SimCoach.Reference</c>; storage stays string-typed
    /// so it takes no dependency on that assembly.</summary>
    public required string Kind { get; init; }

    /// <summary>JSON array of N per-sector best durations (ms). Null unless <see cref="Kind"/> is
    /// <c>"optimal"</c>.</summary>
    public string? OptimalSectorMs { get; init; }

    /// <summary>JSON describing which session/lap each optimal sector best came from. Null unless
    /// <see cref="Kind"/> is <c>"optimal"</c>.</summary>
    public string? SectorSourcesJson { get; init; }
}

/// <summary>Row of the <c>reference_snapshots</c> history table — one per PB parquet ever written for a
/// triple (many per triple; the active pointer is <see cref="ReferenceRow"/>). ADR-0017.</summary>
public sealed record ReferenceSnapshotRow
{
    public required string Id { get; init; }
    public required string TrackId { get; init; }
    public required string CarId { get; init; }
    public required string WeatherBucket { get; init; }
    public string? SourceSessionId { get; init; }
    public int? SourceLapNumber { get; init; }
    public required int LapTimeMs { get; init; }
    public required string ParquetPath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
