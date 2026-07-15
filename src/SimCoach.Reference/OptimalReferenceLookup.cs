using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Reads the own-optimal (M46) target for a triple. Deliberately NOT folded into
/// <see cref="ReferenceLookup"/>: that path does <c>File.Exists</c> + parquet decode and would silently
/// null-out the row-only, file-less optimal. This reads the per-sector best DURATIONS from the optimal
/// row's <c>optimal_sector_ms</c> JSON, validates their count against the sim's sector count (fail-fast),
/// and prefix-sums them into cumulative sector-boundary times for the delta path. TIME ONLY — no control
/// channels exist for the stitched lap.
/// </summary>
public sealed class OptimalReferenceLookup
{
    private readonly ReferenceRepository _repository;
    private readonly ILogger<OptimalReferenceLookup> _logger;

    public OptimalReferenceLookup(ReferenceRepository repository, ILogger<OptimalReferenceLookup> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Cumulative sector-boundary times (ms from lap start) for the triple's optimal, or <c>null</c> when
    /// no optimal is stored. <paramref name="expectedSectorCount"/> is the sim's sector count; a stored
    /// array of any other length is a corrupt reference and throws (input-validation, fail-fast).
    /// </summary>
    public int[]? GetSectorTimes(ReferenceTriple triple, int expectedSectorCount)
    {
        if (expectedSectorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedSectorCount), expectedSectorCount, "Sector count must be positive.");
        }

        ReferenceRow? row = _repository.GetByTriple(
            triple.TrackId, triple.CarId, triple.WeatherBucket, ReferenceKind.Optimal.ToDbString());
        if (row is null)
        {
            return null;
        }

        int[] durations = ParseDurations(row, triple, expectedSectorCount);

        int[] cumulative = new int[durations.Length];
        int running = 0;
        for (int i = 0; i < durations.Length; i++)
        {
            running += durations[i];
            cumulative[i] = running;
        }

        return cumulative;
    }

    private int[] ParseDurations(ReferenceRow row, ReferenceTriple triple, int expectedSectorCount)
    {
        if (row.OptimalSectorMs is null)
        {
            _logger.LogError(
                "Optimal reference row {Id} for {Track}/{Car}/{Weather} has null optimal_sector_ms",
                row.Id, triple.TrackId, triple.CarId, triple.WeatherBucket);
            throw new InvalidOperationException($"Optimal reference row '{row.Id}' has no sector durations.");
        }

        int[]? durations;
        try
        {
            durations = JsonSerializer.Deserialize<int[]>(row.OptimalSectorMs);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex, "Optimal reference row {Id} has malformed optimal_sector_ms JSON", row.Id);
            throw new InvalidOperationException($"Optimal reference row '{row.Id}' has malformed sector JSON.", ex);
        }

        if (durations is null)
        {
            throw new InvalidOperationException($"Optimal reference row '{row.Id}' has null sector durations.");
        }

        if (durations.Length != expectedSectorCount)
        {
            _logger.LogError(
                "Optimal reference row {Id} has {Actual} sector durations, sim expects {Expected}",
                row.Id, durations.Length, expectedSectorCount);
            throw new InvalidOperationException(
                $"Optimal reference row '{row.Id}' has {durations.Length} sectors, expected {expectedSectorCount}.");
        }

        return durations;
    }
}
