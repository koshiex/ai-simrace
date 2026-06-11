namespace SimCoach.Storage.Mcap;

/// <summary>Tuning knobs for <see cref="McapWriter"/>.</summary>
public sealed record McapWriterOptions
{
    /// <summary>MCAP header profile string; spec reserves bare names, custom profiles use x-.</summary>
    public string Profile { get; init; } = "x-simcoach";

    /// <summary>MCAP header library string (writer identification).</summary>
    public string Library { get; init; } = "simcoach-mcap 0.1";

    /// <summary>The current chunk flushes once its buffered records reach this size.</summary>
    public int ChunkThresholdBytes { get; init; } = 1024 * 1024;

    /// <summary>Keep the underlying stream open after the writer is disposed.</summary>
    public bool LeaveOpen { get; init; }

    /// <summary>Fails fast on unusable values.</summary>
    public void EnsureValid()
    {
        if (ChunkThresholdBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ChunkThresholdBytes), ChunkThresholdBytes, "The chunk threshold must be positive.");
        }

        ArgumentNullException.ThrowIfNull(Profile, nameof(Profile));
        ArgumentNullException.ThrowIfNull(Library, nameof(Library));
    }
}
