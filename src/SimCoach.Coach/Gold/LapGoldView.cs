using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Gold;

/// <summary>
/// The typed <see cref="IGoldView"/> over a lap Gold artifact. Field switch matches
/// <c>GoldFieldNames.For(Lap)</c> exactly; the nested <see cref="GoldThermalSummary"/> is flattened to the flat
/// <c>max_tyre_temp_c</c>/<c>tyre_overheat</c>/… keys the registry references.
/// </summary>
public sealed class LapGoldView : IGoldView
{
    private readonly GoldArtifact<GoldLapEvent> _artifact;

    public LapGoldView(GoldArtifact<GoldLapEvent> artifact) => _artifact = artifact;

    public CoachCadence Cadence => CoachCadence.Lap;

    public bool HasReference => _artifact.Session.HasReference;

    public bool TryGetNumber(string field, out double value)
    {
        GoldLapEvent e = _artifact.Event;
        switch (field)
        {
            case "lap_number": return GoldScalar.Num(e.LapNumber, out value);
            case "delta_ms": return GoldScalar.Num(e.DeltaMs, out value);
            case "max_tyre_temp_c": return GoldScalar.Num(e.Thermal.MaxTyreTempC, out value);
            case "max_brake_temp_c": return GoldScalar.Num(e.Thermal.MaxBrakeTempC, out value);
            default: value = 0d; return false;
        }
    }

    public bool TryGetBool(string field, out bool value)
    {
        GoldLapEvent e = _artifact.Event;
        switch (field)
        {
            case "is_pb": value = e.IsPb; return true;
            case "is_clean": value = e.IsClean; return true;
            case "tyre_overheat": value = e.Thermal.TyreOverheat; return true;
            case "brake_overheat": value = e.Thermal.BrakeOverheat; return true;
            case "has_reference": value = _artifact.Session.HasReference; return true;
            default: value = false; return false;
        }
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
