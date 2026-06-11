namespace SimCoach.Storage;

/// <summary>Tuning knobs for <see cref="McapRecorderService"/>.</summary>
public sealed record RecordingOptions
{
    /// <summary>
    /// Root directory for recordings; sessions land in per-session subdirectories.
    /// Default resolves to %LOCALAPPDATA%/SimCoach/recordings on Windows and the
    /// platform equivalent elsewhere.
    /// </summary>
    public string BasePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimCoach",
        "recordings");

    /// <summary>Recording rotates to a new segment file after this duration.</summary>
    public TimeSpan SegmentDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Fails fast on unusable values. Called by consumers' constructors.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
        {
            throw new ArgumentException("A recordings base path is required.", nameof(BasePath));
        }

        if (SegmentDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SegmentDuration), SegmentDuration, "The segment duration must be positive.");
        }
    }
}
