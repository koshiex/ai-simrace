using SimCoach.Contracts.V1;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Reads a session's rotating <c>segment-*.mcap</c> files in order as one logical
/// <see cref="TelemetryFrame"/> stream — the shared seam for end-of-session Parquet conversion
/// (ADR-0011: a session is a directory of segments, never a concatenated file). Unlike
/// <c>McapReplaySource</c> it does no pacing; it just decodes frames as fast as possible.
/// <para>
/// The segment glob/ordinal-sort here deliberately duplicates
/// <c>McapReplaySource.ResolveSegmentPaths</c>. Deduplicating the live replay source onto this
/// enumerator is deferred to a later PR (C9) — refactoring a live class does not belong in a
/// dead-until-wired change.
/// </para>
/// </summary>
public static class McapSegmentEnumerator
{
    /// <summary>
    /// Enumerates every telemetry frame across the directory's segments, ordered by segment then by
    /// message order within each segment.
    /// </summary>
    /// <exception cref="FileNotFoundException">The directory has no <c>.mcap</c> segments.</exception>
    public static IEnumerable<TelemetryFrame> Read(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        if (!Directory.Exists(sessionDirectory))
        {
            throw new FileNotFoundException($"Session directory '{sessionDirectory}' does not exist.");
        }

        string[] segments = Directory.GetFiles(sessionDirectory, "*.mcap");
        if (segments.Length == 0)
        {
            throw new FileNotFoundException($"No .mcap segments found in '{sessionDirectory}'.");
        }

        // segment-NNNN names sort chronologically up to 9999 segments (mirrors McapReplaySource).
        Array.Sort(segments, StringComparer.Ordinal);
        return ReadSegments(segments);
    }

    private static IEnumerable<TelemetryFrame> ReadSegments(string[] segments)
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
