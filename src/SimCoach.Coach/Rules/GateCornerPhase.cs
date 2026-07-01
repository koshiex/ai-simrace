namespace SimCoach.Coach.Rules;

/// <summary>The live corner phase the gate snapshot marks, derived from the active corner window (M7).</summary>
public enum GateCornerPhase
{
    None,
    Braking,
    Entry,
    Apex,
    Exit,
}
