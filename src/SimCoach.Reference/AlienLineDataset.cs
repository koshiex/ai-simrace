using System.Reflection;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// Read-only loader for the vendored alien racing-line references (ADR-0021 / PR-B3). Mirrors
/// <see cref="CenterlineGeometryDataset"/>: loads every embedded <c>Data/alien_line.&lt;trackId&gt;.parquet</c>,
/// indexes by track id, and resolves a track's LINE-only <see cref="ResampledLap"/> (world path, no TIME).
/// One alien line per track; the full-triple stamp lives on the DB row (PR-B3 commit 21), not the asset.
/// When no alien line is embedded for a track the caller falls back to the centerline / PB line, so an
/// absent asset is never worse than the status quo.
/// <para>
/// <b>Scaffold:</b> no alien-line asset is vendored yet — the embed glob currently matches nothing and
/// <see cref="Load"/> returns an empty dataset. The real per-track parquet is produced dev-time by the
/// PR-B3 GhostImport tool and vendored later.
/// </para>
/// </summary>
public sealed class AlienLineDataset
{
    private const string ResourceMarker = ".Data.alien_line.";
    private const string ResourceExtension = ".parquet";

    private readonly IReadOnlyDictionary<string, ResampledLap> _byTrack;

    private AlienLineDataset(IReadOnlyDictionary<string, ResampledLap> byTrack) => _byTrack = byTrack;

    /// <summary>Loads the alien lines embedded in this assembly.</summary>
    public static AlienLineDataset Load() => new(LoadEmbedded());

    /// <summary>Builds a dataset from in-memory laps keyed by track id (test seam).</summary>
    internal static AlienLineDataset FromLaps(IReadOnlyDictionary<string, ResampledLap> byTrack)
    {
        ArgumentNullException.ThrowIfNull(byTrack);
        return new(new Dictionary<string, ResampledLap>(byTrack, StringComparer.Ordinal));
    }

    /// <summary>
    /// Resolves a covered track's alien LINE grid. Returns false (and null) for an unknown track, which the
    /// caller treats as "no alien line" and falls back to the centerline / PB line.
    /// </summary>
    public bool TryGetAlienLine(string trackId, out ResampledLap? alienLine) =>
        _byTrack.TryGetValue(trackId, out alienLine);

    private static IReadOnlyDictionary<string, ResampledLap> LoadEmbedded()
    {
        Assembly assembly = typeof(AlienLineDataset).Assembly;
        Dictionary<string, ResampledLap> byTrack = new(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            int markerIndex = name.IndexOf(ResourceMarker, StringComparison.Ordinal);
            if (markerIndex < 0 || !name.EndsWith(ResourceExtension, StringComparison.Ordinal))
            {
                continue;
            }

            int trackStart = markerIndex + ResourceMarker.Length;
            string trackId = name[trackStart..^ResourceExtension.Length];
            if (trackId.Length == 0)
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            byTrack[trackId] = ReadEmbeddedLap(stream);
        }

        return byTrack;
    }

    /// <summary>
    /// Materializes an embedded parquet stream to a temp file and reads it back through the shared
    /// <see cref="ReferenceParquetCodec"/> (which reads by path). Internal so the read path stays exercised
    /// while no asset is vendored.
    /// </summary>
    internal static ResampledLap ReadEmbeddedLap(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string tempPath = Path.Combine(Path.GetTempPath(), $"alien_line_{Guid.NewGuid():N}.parquet");
        try
        {
            using (FileStream file = File.Create(tempPath))
            {
                stream.CopyTo(file);
            }

            return ReferenceParquetCodec.Read(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
