using System.Text;

namespace SimCoach.Reference;

/// <summary>
/// The <c>(track, car, weather)</c> key that identifies one reference lap, plus its on-disk parquet
/// filename. Centralizes the filename convention so writer and reader can never disagree.
/// </summary>
public readonly record struct ReferenceTriple(string TrackId, string CarId, string WeatherBucket)
{
    /// <summary><c>&lt;track&gt;_&lt;car&gt;_&lt;weather&gt;.parquet</c> with each segment sanitized.</summary>
    public string ParquetFileName =>
        $"{Sanitize(TrackId)}_{Sanitize(CarId)}_{Sanitize(WeatherBucket)}.parquet";

    /// <summary>
    /// A versioned snapshot filename — one per PB, never overwritten (ADR-0017):
    /// <c>&lt;track&gt;_&lt;car&gt;_&lt;weather&gt;__&lt;lapMs&gt;_&lt;id&gt;.parquet</c>. The snapshot id keeps it
    /// collision-free; ordering is by the snapshot row's <c>created_at</c>, not the name.
    /// </summary>
    public string SnapshotFileName(int lapTimeMs, string snapshotId) =>
        $"{Sanitize(TrackId)}_{Sanitize(CarId)}_{Sanitize(WeatherBucket)}__{lapTimeMs}_{Sanitize(snapshotId)}.parquet";

    /// <summary>Keeps <c>[a-z0-9_-]</c>, lower-casing and replacing anything else — no path traversal.</summary>
    private static string Sanitize(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (char c in segment.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        return builder.ToString();
    }
}
