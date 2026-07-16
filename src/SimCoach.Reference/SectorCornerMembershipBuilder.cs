using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Grounds each observed sector to the baked corners whose apex falls inside it (M41). Sectors are runtime
/// only (ADR-0010): the normalized-position range of each sector index is OBSERVED at sector-cross time (as
/// <c>current_sector_index</c> changes) and never persisted to the track model. At session end the observed
/// ranges are intersected with the baked corner apex positions to derive membership — one
/// <see cref="SectorCornerMembership"/> per observed sector that contains at least one baked corner apex.
/// Mutation is isolated here; <see cref="Build"/> returns an immutable snapshot.
/// </summary>
internal sealed class SectorCornerMembershipBuilder
{
    // Observed [Lo, Hi] normalized-position span per sector index, unioned across laps (min Lo, max Hi) so a
    // short first-lap partial crossing cannot clip the range. Non-wrapping: the caller feeds the wrap-folded
    // end position (1.0 for the S/F-straddling final sector), so Lo <= Hi always holds for a stored range.
    private readonly Dictionary<int, (float Lo, float Hi)> _ranges = [];

    public void Observe(int sectorIndex, float startPosition, float endPosition)
    {
        // A non-monotonic crossing (end < start) is a wrap/teleport artefact with no usable forward range —
        // the caller folds a real wrap to 1.0, so a raw end < start observation is dropped, never inverted.
        if (endPosition < startPosition)
        {
            return;
        }

        _ranges[sectorIndex] = _ranges.TryGetValue(sectorIndex, out (float Lo, float Hi) range)
            ? (Math.Min(range.Lo, startPosition), Math.Max(range.Hi, endPosition))
            : (startPosition, endPosition);
    }

    public IReadOnlyList<SectorCornerMembership> Build(IReadOnlyList<Corner> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        List<SectorCornerMembership> membership = [];
        foreach (KeyValuePair<int, (float Lo, float Hi)> observed in _ranges.OrderBy(pair => pair.Key))
        {
            var mapping = new SectorCornerMembership { SectorIndex = observed.Key };
            foreach (Corner corner in corners)
            {
                if (corner.ApexPosition >= observed.Value.Lo && corner.ApexPosition <= observed.Value.Hi)
                {
                    mapping.CornerIds.Add(corner.Id);
                }
            }

            // Honor the proto invariant that each emitted entry maps to >=1 corner: a corner-free sector
            // (unrealizable on a closed circuit, but possible in a degenerate baked model) emits nothing.
            if (mapping.CornerIds.Count > 0)
            {
                membership.Add(mapping);
            }
        }

        return membership;
    }
}
