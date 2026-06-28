namespace SimCoach.Storage;

/// <summary>
/// Maps a normalized <c>car_id</c> to its coarse, privacy-safe competition class (e.g. <c>gt3</c>) for the
/// Gold session context. Mirrors <see cref="ITrackLengthProvider"/>: it lives in Storage (the lowest project
/// the Coach layer references) so the seam stays sim-agnostic — the ACC adapter implements it at the
/// composition edge, and other sims plug in their own catalog without any project depending on a sim adapter.
/// </summary>
public interface ICarClassProvider
{
    /// <summary>The coarse class for a normalized car id; <c>false</c> for cars outside the catalog.</summary>
    bool TryGetCarClass(string carId, out string carClass);
}
