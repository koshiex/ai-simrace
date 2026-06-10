namespace SimCoach.Pipeline;

/// <summary>Tuning knobs for the ingest fan-out.</summary>
public sealed record IngestOptions
{
    /// <summary>
    /// Capacity of each subscriber's channel; the oldest frame drops when a consumer lags.
    /// Sized for ~0.75 s of ACC telemetry at 333 Hz.
    /// </summary>
    public int SubscriberChannelCapacity { get; init; } = 256;

    /// <summary>Minimum interval between dropped-frame warnings.</summary>
    public TimeSpan DropLogInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Fails fast on unusable values. Called by consumers' constructors.</summary>
    public void EnsureValid()
    {
        if (SubscriberChannelCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SubscriberChannelCapacity),
                SubscriberChannelCapacity,
                "Subscriber channels need a positive capacity.");
        }

        if (DropLogInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DropLogInterval), DropLogInterval, "The drop-log interval must be non-negative.");
        }
    }
}
