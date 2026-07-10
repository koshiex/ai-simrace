using System.Reflection;
using System.Text.Json;

namespace SimCoach.Reference;

/// <summary>
/// Read-only loader for the vendored first-party <c>cornerGeometry.json</c> documents (ADR-0014).
/// Loads every embedded <c>Data/cornerGeometry*.json</c>, indexes by track id, and resolves a track's
/// corners only when the schema version, lap length, and per-corner ranges all check out — one bad
/// range disqualifies the whole track (the ADR-0010 guard: never emit a misplaced corner). Replaces
/// the vendored CrewChief <c>LandmarkDataset</c>.
/// </summary>
public sealed class CornerGeometryDataset
{
    /// <summary>Tolerance (metres) for the document lap length vs the catalog lap length.</summary>
    public const float LapLengthToleranceM = 1f;

    private readonly IReadOnlyDictionary<string, CornerGeometryDocument> _byTrack;

    private CornerGeometryDataset(IReadOnlyDictionary<string, CornerGeometryDocument> byTrack) => _byTrack = byTrack;

    /// <summary>Loads the geometry embedded in this assembly.</summary>
    public static CornerGeometryDataset Load() => new(LoadEmbedded());

    /// <summary>Builds a dataset from in-memory documents (test seam).</summary>
    internal static CornerGeometryDataset FromDocuments(IEnumerable<CornerGeometryDocument> documents) =>
        new(Index(documents));

    /// <summary>
    /// Resolves a covered track's corners. Returns false (and an empty list) for an unknown track, a
    /// schema/lap-length mismatch, or any out-of-range corner.
    /// </summary>
    public bool TryGetCorners(string trackId, float lapLengthM, out IReadOnlyList<Corner> corners)
    {
        corners = [];
        if (!_byTrack.TryGetValue(trackId, out CornerGeometryDocument? document))
        {
            return false;
        }

        if (document.SchemaVersion != CornerGeometryDocument.CurrentSchemaVersion)
        {
            return false;
        }

        if (MathF.Abs(document.LapLengthM - lapLengthM) > LapLengthToleranceM)
        {
            return false;
        }

        List<Corner> resolved = new(document.Corners.Count);
        foreach (CornerGeometryEntry entry in document.Corners)
        {
            bool inRange = entry.StartPosition >= 0f
                && entry.StartPosition < entry.EndPosition
                && entry.EndPosition <= 1f
                && entry.StartPosition <= entry.ApexPosition
                && entry.ApexPosition <= entry.EndPosition;
            if (!inRange)
            {
                return false;
            }

            resolved.Add(new Corner
            {
                Id = entry.Id,
                Name = null,
                StartPosition = entry.StartPosition,
                ApexPosition = entry.ApexPosition,
                EndPosition = entry.EndPosition,
                ApexRadiusM = entry.ApexRadiusM,
                Trigger = entry.Trigger,
            });
        }

        corners = resolved;
        return true;
    }

    private static IReadOnlyDictionary<string, CornerGeometryDocument> LoadEmbedded()
    {
        Assembly assembly = typeof(CornerGeometryDataset).Assembly;
        Dictionary<string, CornerGeometryDocument> byTrack = new(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.Contains(".Data.cornerGeometry", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            CornerGeometryDocument? document = JsonSerializer.Deserialize<CornerGeometryDocument>(stream);
            if (document is not null)
            {
                byTrack[document.TrackId] = document;
            }
        }

        return byTrack;
    }

    private static IReadOnlyDictionary<string, CornerGeometryDocument> Index(IEnumerable<CornerGeometryDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Dictionary<string, CornerGeometryDocument> byTrack = new(StringComparer.Ordinal);
        foreach (CornerGeometryDocument document in documents)
        {
            byTrack[document.TrackId] = document;
        }

        return byTrack;
    }
}
