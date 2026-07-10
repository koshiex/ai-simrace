namespace SimCoach.Reference;

/// <summary>
/// Where reference (PB) parquet files live — <c>&lt;DataRoot&gt;/references</c>, one file per
/// <c>(track, car, weather)</c> triple. Shared by <see cref="ReferenceStore"/> (writer) and
/// <see cref="ReferenceLookup"/> (reader) so both agree on the path.
/// </summary>
public sealed class ReferenceStorageOptions
{
    /// <summary>Absolute directory holding <c>&lt;track&gt;_&lt;car&gt;_&lt;weather&gt;.parquet</c> files.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Max reference snapshots kept per triple before the oldest are pruned (ADR-0017). <c>null</c> =
    /// keep all (the default — safe for pre-alpha; bounds disk when set). Must be positive when set; the
    /// newest snapshot (the active pointer) is always retained.
    /// </summary>
    public int? MaxSnapshotsPerTriple { get; init; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Directory))
        {
            throw new InvalidOperationException("ReferenceStorageOptions.Directory must be set.");
        }

        if (MaxSnapshotsPerTriple is <= 0)
        {
            throw new InvalidOperationException(
                "ReferenceStorageOptions.MaxSnapshotsPerTriple must be positive when set (or null for keep-all).");
        }
    }
}
