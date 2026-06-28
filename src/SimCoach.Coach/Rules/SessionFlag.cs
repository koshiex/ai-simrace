namespace SimCoach.Coach.Rules;

/// <summary>Track/session state for the session-not-green gate. Only <see cref="Green"/> permits tips.</summary>
public enum SessionFlag
{
    Green,
    Pit,
    SafetyCar,
    Yellow,
    Paused,
}
