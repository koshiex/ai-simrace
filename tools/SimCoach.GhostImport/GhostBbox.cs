namespace SimCoach.GhostImport;

/// <summary>
/// An axis-aligned world-XZ bounding box for a track, used by <see cref="ImportGuards"/> to reject a
/// decoded ghost whose path falls outside the racing surface (a wrong stride or a foreign file produces
/// coordinates off by hundreds of metres). Bounds are inclusive and expressed in the same world frame
/// as the SHM <c>carCoordinates</c> (e.g. Monza X <c>[-398, 858]</c>, Z <c>[-1126, 1045]</c>).
/// </summary>
internal readonly record struct GhostBbox(float MinX, float MaxX, float MinZ, float MaxZ)
{
    internal bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}
