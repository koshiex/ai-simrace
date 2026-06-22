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

/// <summary>Row of the <c>references</c> table (one PB per <c>(track, car, weather)</c> triple).</summary>
public sealed record ReferenceRow
{
    public required string Id { get; init; }
    public required string TrackId { get; init; }
    public required string CarId { get; init; }
    public required string WeatherBucket { get; init; }
    public string? SourceSessionId { get; init; }
    public int? SourceLapNumber { get; init; }
    public required int LapTimeMs { get; init; }
    public required string ParquetPath { get; init; }
    public bool Pinned { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
