namespace SimCoach.Storage.Repositories;

/// <summary>
/// Read side over the <c>references</c> table for the reference-library UI (Screen 05). Declared in P3 so
/// the contract is stable; the implementation lands in a later phase (P6/P7) when the screen is built.
/// </summary>
public interface IReferenceQueryRepository
{
    Task<IReadOnlyList<ReferenceLap>> ListAsync(string? trackId, string? carId, string? weatherBucket, CancellationToken ct);

    Task SetPinnedAsync(string referenceId, bool pinned, CancellationToken ct);
}

/// <summary>UI projection of a stored PB reference (one per <c>(track, car, weather)</c> triple).</summary>
public sealed record ReferenceLap(
    string ReferenceId,
    string TrackId,
    string CarId,
    string WeatherBucket,
    int LapTimeMs,
    bool Pinned,
    DateTimeOffset CreatedAtUtc);
