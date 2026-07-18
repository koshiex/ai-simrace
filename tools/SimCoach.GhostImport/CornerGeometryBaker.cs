using SimCoach.Reference;

namespace SimCoach.GhostImport;

/// <summary>
/// Bakes a <c>cornerGeometry.&lt;track&gt;.json</c> document from a ghost-derived median centerline
/// (blueprint B3 / ADR-0022). A ghost aggregate is a single line with no per-lap variants and no lateral g,
/// so the detector runs curvature+sustained with the line as its own sole per-lap centerline (G=0), matching
/// SimCoach.Bake's single-lap fallback. The apex is the geometric centre of each corner's extent; on
/// ghost maps every corner triggers by curvature and reports <c>PeakLateralG=0</c> — the documented
/// degenerate fields that distinguish ghost-derived geometry from owner-baked Monza/Spa.
/// </summary>
internal static class CornerGeometryBaker
{
    /// <summary>
    /// Detects corners on <paramref name="centerline"/> and wraps them into a length-pinned document whose
    /// corner ids are the detection-order <c>&lt;trackId&gt;_tNN</c>.
    /// </summary>
    internal static CornerGeometryDocument Bake(MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);

        // A ghost aggregate has no separate clean laps, so it feeds the per-lap-consensus split rule as its
        // own single per-lap centerline (tools/SimCoach.Bake does the same for a single-lap track).
        IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline, [centerline]);
        return CornerGeometryDocument.FromDetected(
            centerline.TrackId, centerline.LapLengthM, centerline.LapCount, corners);
    }
}
