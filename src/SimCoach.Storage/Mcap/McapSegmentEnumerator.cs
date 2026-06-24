using SimCoach.Contracts.V1;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Reads a session's rotating <c>segment-*.mcap</c> files in order as one logical
/// <see cref="TelemetryFrame"/> stream — the shared seam for end-of-session Parquet conversion
/// (ADR-0011: a session is a directory of segments, never a concatenated file). Unlike
/// <c>McapReplaySource</c> it does no pacing; it just decodes frames as fast as possible.
/// <para>
/// <see cref="ResolveSegmentPaths"/> is the single segment-glob/ordinal-sort used by both this
/// enumerator and <c>McapReplaySource</c> (which keeps only its pacing loop). It accepts either a
/// single <c>.mcap</c> file or a directory of segments.
/// </para>
/// </summary>
public static class McapSegmentEnumerator
{
    /// <summary>
    /// Resolves a replay/session path to its ordered segment list: a single <c>.mcap</c> file maps to
    /// itself; a directory maps to its <c>*.mcap</c> files sorted ordinally (segment-NNNN names sort
    /// chronologically up to 9999 segments). The shared seam so the glob lives in exactly one place.
    /// </summary>
    /// <exception cref="FileNotFoundException">The path is missing or a directory has no <c>.mcap</c> segments.</exception>
    public static IReadOnlyList<string> ResolveSegmentPaths(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            return [path];
        }

        if (Directory.Exists(path))
        {
            string[] segments = Directory.GetFiles(path, "*.mcap");
            if (segments.Length == 0)
            {
                throw new FileNotFoundException($"No .mcap segments found in '{path}'.");
            }

            Array.Sort(segments, StringComparer.Ordinal);
            return segments;
        }

        throw new FileNotFoundException($"Segment path '{path}' does not exist.");
    }

    /// <summary>
    /// Enumerates every telemetry frame across the directory's segments, ordered by segment then by
    /// message order within each segment.
    /// </summary>
    /// <exception cref="FileNotFoundException">The directory has no <c>.mcap</c> segments.</exception>
    public static IEnumerable<TelemetryFrame> Read(string sessionDirectory)
    {
        IReadOnlyList<string> segments = ResolveSegmentPaths(sessionDirectory);
        return ReadSegments(segments);
    }

    private static IEnumerable<TelemetryFrame> ReadSegments(IReadOnlyList<string> segments)
    {
        foreach (string segmentPath in segments)
        {
            McapSegment segment = ReadSegment(segmentPath);
            foreach (McapMessage message in segment.Messages)
            {
                yield return TelemetryFrame.Parser.ParseFrom(message.Data);
            }
        }
    }

    private static McapSegment ReadSegment(string segmentPath)
    {
        using FileStream stream = File.OpenRead(segmentPath);
        return McapSegment.Read(stream);
    }
}
