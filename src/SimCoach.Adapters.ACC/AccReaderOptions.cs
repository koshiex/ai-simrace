namespace SimCoach.Adapters.ACC;

/// <summary>Tuning knobs for <see cref="AccSharedMemoryReader"/>.</summary>
public sealed record AccReaderOptions
{
    /// <summary>
    /// Pause between poll ticks. ACC's physics page updates at 333 Hz (~3 ms); 1 ms keeps us
    /// ahead of it. Windows would otherwise round a 1 ms wait up to the ~15.6 ms scheduler tick,
    /// capping the emit rate at ~64 Hz — B7 verification on real hardware confirmed this, so the
    /// poll loop raises the timer resolution to 1 ms (<see cref="WinTimerResolution"/>) for the
    /// 1 ms wait to hold. Zero means yield-only busy polling.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(1);

    /// <summary>Delay between connection attempts while ACC is not running.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the (rarely changing) static page is re-read.</summary>
    public TimeSpan StaticRefreshInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Capacity of the internal frame channel; oldest frames drop when full.</summary>
    public int ChannelCapacity { get; init; } = 256;

    /// <summary>Max page-copy attempts per tick when the seqlock detects torn reads.</summary>
    public int MaxSeqlockRetries { get; init; } = 4;

    /// <summary>
    /// Fails fast on unusable values (e.g. zero seqlock retries would silently never produce
    /// a frame). Called by consumers' constructors.
    /// </summary>
    public void EnsureValid()
    {
        if (MaxSeqlockRetries < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSeqlockRetries), MaxSeqlockRetries, "At least one seqlock copy attempt is required.");
        }

        if (ChannelCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ChannelCapacity), ChannelCapacity, "The frame channel needs a positive capacity.");
        }

        EnsureNonNegative(PollInterval, nameof(PollInterval));
        EnsureNonNegative(ReconnectDelay, nameof(ReconnectDelay));
        EnsureNonNegative(StaticRefreshInterval, nameof(StaticRefreshInterval));
    }

    private static void EnsureNonNegative(TimeSpan interval, string name)
    {
        if (interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, interval, "Intervals must be non-negative.");
        }
    }
}
