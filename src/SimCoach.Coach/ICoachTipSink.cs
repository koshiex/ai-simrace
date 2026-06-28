namespace SimCoach.Coach;

/// <summary>
/// Where coaching tips go. P3 ships <see cref="ConsoleTipSink"/>; Voice (P4) and the overlay (P5) implement
/// the same contract, so <c>CoachService</c> never knows which sink is attached. Implementations must be
/// non-blocking — a slow sink cannot stall the coaching pipeline.
/// </summary>
public interface ICoachTipSink
{
    Task EmitTipAsync(CoachTip tip, CancellationToken ct);
}
