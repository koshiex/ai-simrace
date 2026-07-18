namespace SimCoach.GhostImport;

/// <summary>
/// Tunable knobs for the alien-line import pipeline. Every threshold lives here (no magic numbers in the
/// pipeline code): the loop-closure lap split, the centerline-alignment fail-fast ceiling (OD5), the
/// per-metre resample step, and the seam-suppression bands (OD9). Defaults reproduce the ship decision;
/// a test can override any field to prove a threshold is honored.
/// </summary>
internal sealed record GhostImportOptions
{
    /// <summary>How close (metres) a world point must return to the start point to close a lap.</summary>
    public float LoopClosureRadiusM { get; init; } = 15f;

    /// <summary>How far (metres) the path must travel from the start before a return can close a lap.</summary>
    public float LoopClosureMinTravelM { get; init; } = 200f;

    /// <summary>Median nearest-point alignment deviation ceiling (metres) — import fails fast above it.</summary>
    public float AlignmentDeviationCeilingM { get; init; } = 2f;

    /// <summary>
    /// Cross-lap span-coherence ceiling (metres) for a GHOST-derived centerline bake (B1b). Deliberately
    /// looser than the owner-tuned <c>CenterlineCoherence.MaxTrustedMedianDeviationM</c> (1 m, sized to a
    /// single driver's sub-metre own-lap repeatability — Spa 0.52 m / Monza 0.33 m): here the per-bin
    /// deviation measures the spread BETWEEN different alien drivers' racing lines (turn-in / apex / exit
    /// legitimately differ by a car-width or more after the shared-axis re-stamp), not one driver's
    /// repeatability. 4 m admits that legitimate line spread while still rejecting a mis-decoded or foreign
    /// lap (tens of metres off). Informational-leaning per OD-B3 / ADR-0022 — the hard backstop is the
    /// downstream corner-layout calibration gate, not this envelope.
    /// </summary>
    public float GhostCoherenceCeilingM { get; init; } = 4f;

    /// <summary>
    /// Minimum fraction of the lap length the ghost centerline's real (sampled) bins must SPAN for the bake
    /// to be trusted (B1b full-lap span check). Guards against a decode/loop-split that only covered part of
    /// the lap being medianed into a "centerline" that silently omits whole corners. 0.90 leaves headroom
    /// for the start/finish seam while still demanding near-complete coverage.
    /// </summary>
    public float MinGhostSpanFraction { get; init; } = 0.90f;

    /// <summary>Per-metre resample step (metres) for the emitted LINE grid.</summary>
    public float ResampleStepM { get; init; } = 1f;

    /// <summary>
    /// Position-normalized bands whose bins are masked seam-invalid (OD9 full suppression). Default:
    /// the start-finish loop-closure artifact <c>[0.00, 0.02]</c> and the end-of-lap seam <c>[0.92, 1.00]</c>.
    /// </summary>
    public IReadOnlyList<SeamBand> SeamBands { get; init; } =
    [
        new SeamBand(0.00f, 0.02f),
        new SeamBand(0.92f, 1.00f),
    ];
}
