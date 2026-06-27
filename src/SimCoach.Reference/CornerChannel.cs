namespace SimCoach.Reference;

/// <summary>Which sign-stable channel caused a corner to be detected (ADR-0014).</summary>
public enum CornerChannel
{
    /// <summary>Detected by centerline curvature (R below threshold).</summary>
    Curvature,

    /// <summary>Detected by sustained lateral load only — the flat/large-radius case (e.g. Curva Grande).</summary>
    LateralG,

    /// <summary>Detected by both channels.</summary>
    Both,
}
