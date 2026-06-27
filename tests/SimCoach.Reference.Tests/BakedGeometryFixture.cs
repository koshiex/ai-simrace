using SimCoach.Reference;
using SimCoach.TestKit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Builds an in-memory baked-geometry dataset for the compute tests, so the synthetic Spa track is
/// "covered" without committing a recording. The synthetic <c>world_pos</c> is a perfect circle (not a
/// geometry oracle), so this maps the synthetic corner windows straight into baked entries — it exercises
/// the measurement pipeline (corner trackers + kernels), not corner detection.
/// </summary>
internal static class BakedGeometryFixture
{
    /// <summary>A dataset covering the synthetic Spa track (the "covered" track in compute tests).</summary>
    public static CornerGeometryDataset Spa() => CornerGeometryDataset.FromDocuments([Document(SyntheticTracks.Spa)]);

    private static CornerGeometryDocument Document(SyntheticTrack track)
    {
        List<CornerGeometryEntry> entries = new(track.Corners.Count);
        for (int i = 0; i < track.Corners.Count; i++)
        {
            SyntheticCorner corner = track.Corners[i];
            entries.Add(new CornerGeometryEntry
            {
                Id = $"{track.TrackId}_t{i + 1:00}",
                StartPosition = corner.EntryPos,
                ApexPosition = corner.ApexPos,
                EndPosition = corner.ExitPos,
                ApexRadiusM = 50f,
                PeakLateralG = 1.2f,
                Trigger = "Both",
            });
        }

        return new CornerGeometryDocument
        {
            SchemaVersion = CornerGeometryDocument.CurrentSchemaVersion,
            TrackId = track.TrackId,
            LapLengthM = track.LapLengthM,
            LapCount = 5,
            Corners = entries,
        };
    }
}
