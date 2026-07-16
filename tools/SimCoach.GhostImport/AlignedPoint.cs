namespace SimCoach.GhostImport;

/// <summary>
/// One ghost-path sample after nearest-point alignment onto the target centerline. <see cref="WorldX"/>/
/// <see cref="WorldZ"/> are the ghost's own driven coordinates (the alien LINE — NOT the centerline);
/// <see cref="PositionNormalized"/> is the arc-length parameter (0..1) borrowed from the nearest
/// centerline bin, because the <c>.ghost</c> carries no normalized-position channel.
/// </summary>
internal readonly record struct AlignedPoint(float PositionNormalized, float WorldX, float WorldZ);
