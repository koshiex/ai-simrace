namespace SimCoach.Storage;

/// <summary>Tuning knobs for <see cref="McapReplaySource"/>.</summary>
public sealed record ReplayOptions
{
    /// <summary>A single .mcap segment file or a session directory holding segment-*.mcap files.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Replay speed: 1 = original inter-frame timing, 2 = twice as fast,
    /// 0 = as fast as possible (tests, batch compute). Extremes degrade gracefully:
    /// very high speeds approach the speed-0 behavior; very low speeds are bounded by
    /// <see cref="MaxFrameDelay"/> per frame.
    /// </summary>
    public double SpeedMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Cap on a single inter-frame wait — recorded pauses (pits, menus) must not stall
    /// the replay for their full real duration. Also the effective maximum per-frame
    /// slowdown regardless of how small <see cref="SpeedMultiplier"/> is.
    /// </summary>
    public TimeSpan MaxFrameDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Fails fast on unusable values. Called by consumers' constructors.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            throw new ArgumentException("A replay path is required.", nameof(Path));
        }

        if (double.IsNaN(SpeedMultiplier) || SpeedMultiplier < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpeedMultiplier), SpeedMultiplier, "The speed multiplier must be zero or positive.");
        }

        if (MaxFrameDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFrameDelay), MaxFrameDelay, "The max frame delay must be positive.");
        }
    }
}
