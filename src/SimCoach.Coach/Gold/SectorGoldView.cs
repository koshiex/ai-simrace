using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Gold;

/// <summary>
/// The typed <see cref="IGoldView"/> over a sector Gold artifact. Field switch matches
/// <c>GoldFieldNames.For(Sector)</c> exactly.
/// </summary>
public sealed class SectorGoldView : IGoldView
{
    private readonly GoldArtifact<GoldSectorEvent> _artifact;

    public SectorGoldView(GoldArtifact<GoldSectorEvent> artifact) => _artifact = artifact;

    public CoachCadence Cadence => CoachCadence.Sector;

    public bool HasReference => _artifact.Session.HasReference;

    public bool TryGetNumber(string field, out double value)
    {
        GoldSectorEvent e = _artifact.Event;
        switch (field)
        {
            case "sector_idx": return GoldScalar.Num(e.SectorIdx, out value);
            case "delta_ms": return GoldScalar.Num(e.DeltaMs, out value);
            default: value = 0d; return false;
        }
    }

    public bool TryGetBool(string field, out bool value)
    {
        if (field == "has_reference")
        {
            value = _artifact.Session.HasReference;
            return true;
        }

        value = false;
        return false;
    }

    public bool TryGetString(string field, out string value)
    {
        if (field == "top_corner")
        {
            return GoldScalar.Str(_artifact.Event.TopCorner, out value);
        }

        value = string.Empty;
        return false;
    }
}
