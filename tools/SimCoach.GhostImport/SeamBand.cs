namespace SimCoach.GhostImport;

/// <summary>
/// A closed position-normalized band <c>[Start, End]</c> whose alien-line grid bins are masked as
/// seam-invalid (OD9 full suppression). The start-finish loop-closure artifact band and the
/// track-specific end-of-lap seam are the two defaults (see <see cref="GhostImportOptions"/>).
/// </summary>
internal readonly record struct SeamBand(float Start, float End)
{
    /// <summary>True when <paramref name="positionNormalized"/> falls inside the closed band.</summary>
    internal bool Contains(float positionNormalized) =>
        positionNormalized >= Start && positionNormalized <= End;
}
