using System.Reflection;
using System.Text.Json;

namespace SimCoach.Reference;

/// <summary>
/// Read-only loader for the vendored first-party <c>centerline.json</c> documents (ADR-0014 / ADR-0019).
/// Loads every embedded <c>Data/centerline*.json</c>, indexes by track id, and resolves a track's median
/// centerline only when the schema version and lap length check out and the aggregate is trustworthy — the
/// runtime LINE reference (M38). Mirrors <see cref="CornerGeometryDataset"/>. When no centerline is embedded
/// for a track the caller falls back to the PB world path (ADR-0019), so an absent asset is never worse than
/// the status quo.
/// </summary>
public sealed class CenterlineGeometryDataset
{
    /// <summary>Tolerance (metres) for the document lap length vs the catalog lap length.</summary>
    public const float LapLengthToleranceM = 1f;

    private readonly IReadOnlyDictionary<string, CenterlineGeometryDocument> _byTrack;

    private CenterlineGeometryDataset(IReadOnlyDictionary<string, CenterlineGeometryDocument> byTrack) =>
        _byTrack = byTrack;

    /// <summary>Loads the centerlines embedded in this assembly.</summary>
    public static CenterlineGeometryDataset Load() => new(LoadEmbedded());

    /// <summary>Builds a dataset from in-memory documents (test seam).</summary>
    internal static CenterlineGeometryDataset FromDocuments(IEnumerable<CenterlineGeometryDocument> documents) =>
        new(Index(documents));

    /// <summary>
    /// Resolves a covered track's median centerline. Returns false (and null) for an unknown track, a
    /// schema/lap-length mismatch, or an untrustworthy (too-few-laps / empty) centerline — each of which the
    /// caller treats as "no centerline" and falls back to the PB line (ADR-0019).
    /// </summary>
    public bool TryGetCenterline(string trackId, float lapLengthM, out MedianCenterline? centerline)
    {
        centerline = null;
        if (!_byTrack.TryGetValue(trackId, out CenterlineGeometryDocument? document))
        {
            return false;
        }

        if (document.SchemaVersion != CenterlineGeometryDocument.CurrentSchemaVersion)
        {
            return false;
        }

        if (MathF.Abs(document.LapLengthM - lapLengthM) > LapLengthToleranceM)
        {
            return false;
        }

        if (document.LapCount < MedianCenterlineBuilder.MinLapsForTrust || document.Bins.Count == 0)
        {
            return false;
        }

        centerline = document.ToCenterline();
        return true;
    }

    private static IReadOnlyDictionary<string, CenterlineGeometryDocument> LoadEmbedded()
    {
        Assembly assembly = typeof(CenterlineGeometryDataset).Assembly;
        Dictionary<string, CenterlineGeometryDocument> byTrack = new(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.Contains(".Data.centerline", StringComparison.Ordinal)
                || !name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            CenterlineGeometryDocument? document = JsonSerializer.Deserialize<CenterlineGeometryDocument>(stream);
            if (document is not null)
            {
                byTrack[document.TrackId] = document;
            }
        }

        return byTrack;
    }

    private static IReadOnlyDictionary<string, CenterlineGeometryDocument> Index(
        IEnumerable<CenterlineGeometryDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Dictionary<string, CenterlineGeometryDocument> byTrack = new(StringComparer.Ordinal);
        foreach (CenterlineGeometryDocument document in documents)
        {
            byTrack[document.TrackId] = document;
        }

        return byTrack;
    }
}
