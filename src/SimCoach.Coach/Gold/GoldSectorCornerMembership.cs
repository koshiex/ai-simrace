namespace SimCoach.Coach.Gold;

/// <summary>
/// Grounded sector→corner membership for one observed sector (M41, proto <c>SectorCornerMembership</c>): the
/// baked corners whose apex falls within that sector's distance range. <see cref="Corners"/> are the resolved
/// human names (compute carries only <c>corner_ids</c>; names stay out of compute per ADR-0010). Non-scalar
/// member of <see cref="GoldSessionPayload"/>, excluded from the reflected <c>GoldFieldNames._session</c> guard.
/// </summary>
public sealed record GoldSectorCornerMembership(int SectorIndex, IReadOnlyList<string> Corners);
