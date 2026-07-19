using SimCoach.Reference;

namespace SimCoach.GhostImport;

/// <summary>
/// Outcome of a ghost-median centerline bake (B1b): the built <see cref="MedianCenterline"/> plus the
/// span-coherence verdict computed on the shared-axis-restamped laps. <see cref="Go"/> is the ghost-basis
/// gate (re-derived thresholds, not the owner-tuned 1 m / 2 m envelope); a caller vendors the centerline
/// only when it is true, otherwise skips the track (OD-B2).
/// </summary>
internal sealed record GhostCenterlineResult
{
    /// <summary>The ghost-derived median centerline (lateral g is 0 throughout — ADR-0022).</summary>
    public required MedianCenterline Centerline { get; init; }

    /// <summary>Cross-lap coherence measured on the common-axis laps (deviation numbers are metres).</summary>
    public required CoherenceReport Coherence { get; init; }

    /// <summary>Fraction of the lap length the centerline's real (sampled) bins span, 0..1.</summary>
    public required float SpanFraction { get; init; }

    /// <summary>The ghost-basis coherence ceiling (metres) the deviation was gated against.</summary>
    public required float CoherenceCeilingM { get; init; }

    /// <summary>True when the ghost centerline passes every re-derived gate (enough laps, coherent, full span).</summary>
    public required bool Go { get; init; }

    /// <summary>Human-readable reasons the gate is NO-GO; empty when <see cref="Go"/> is true.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }
}
