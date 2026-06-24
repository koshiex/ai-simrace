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

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Directory))
        {
            throw new InvalidOperationException("ReferenceStorageOptions.Directory must be set.");
        }
    }
}
