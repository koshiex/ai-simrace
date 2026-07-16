using FluentAssertions;
using SimCoach.Coach.Actions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using Xunit;

namespace SimCoach.Coach.Tests;

/// <summary>
/// Guards that the authored <c>brake_lockup</c> action can actually fire on the MVP target (ACC GT3, ABS on).
/// <see cref="BrakeLockupKernels"/> caps any ABS-caught lock at <c>raw * AbsAttenuation</c>, so the deepest
/// lock a GT3 can report is bounded; if the registry gate is retuned above that ceiling the action ships dead.
/// This ties the kernel-produced score to the registry gate so such a retune is caught, not shipped silently.
/// </summary>
public sealed class BrakeLockupActionReachabilityTests
{
    [Fact]
    public void Overwhelmed_abs_lock_scores_above_the_registry_brake_lockup_gate()
    {
        double gate = BrakeLockupGate();

        // An ABS lock overwhelmed to full saturation (front slip past the lock band, brake hard on) — the
        // deepest reading a GT3 can produce under ABS. It MUST clear the action's gate, or the action is dead.
        TelemetryFrame[] overwhelmedAbs =
        [
            OverwhelmedAbsFrame(frontSlip: -0.1f),
            OverwhelmedAbsFrame(frontSlip: -0.5f),
        ];

        float score = BrakeLockupKernels.BrakeLockupScore(overwhelmedAbs);

        score.Should().BeGreaterThan(
            (float)gate,
            "an overwhelmed ABS lock is the primary-car-class lockup and must satisfy the action's when-clause");
    }

    private static double BrakeLockupGate()
    {
        CoachAction action = ActionRegistry.Load().Actions.Single(a => a.Id == "brake_lockup");
        WhenClause clause = action.When.Single(c => c.Field == "brake_lockup_score");
        clause.Number.Should().NotBeNull("the brake_lockup gate is a numeric threshold");
        return clause.Number!.Value;
    }

    private static TelemetryFrame OverwhelmedAbsFrame(float frontSlip)
    {
        TelemetryFrame frame = new() { BrakePct = 0.9f, AbsActive = true, Abs = 1 };
        frame.SlipRatio.AddRange([frontSlip, frontSlip, 0f, 0f]);
        return frame;
    }
}
