namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// A baked corner's phase boundaries in OFFSET coordinates — forward distance (normalized-position
/// units) from the corner's start — as produced by <see cref="CornerPhaseBands.Offsets"/>.
/// <see cref="TurnInStart"/> is the Braking→Entry boundary, <see cref="ApexStart"/>/<see cref="ApexEnd"/>
/// bound the apex band. A degenerate window (length ≤ 0) yields the all-zero default.
/// </summary>
public readonly record struct CornerPhaseOffsets(double Length, double TurnInStart, double ApexStart, double ApexEnd);
