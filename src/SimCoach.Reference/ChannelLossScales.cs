namespace SimCoach.Reference;

/// <summary>
/// The M36 cross-unit ms-per-unit ranking scales, passed by value from <see cref="ComputeOptions"/> into
/// <see cref="SessionLossAccumulator"/>. They bring the three SIGNED diagnostic diffs (brake-point metres,
/// throttle-resume metres, min-speed km/h) onto one comparable millisecond axis so a corner's
/// <c>dominant_channel</c> is chosen by argmax, not by unit magnitude. The unsigned RMS line-deviation has
/// no scale here — it is excluded from the argmax domain by design (ADR-0020, MF-2). Each product is a
/// ranking heuristic only, never an additive time.
/// </summary>
internal readonly record struct ChannelLossScales(
    float MsPerMetreBrakePoint,
    float MsPerMetreThrottleResume,
    float MsPerKmhMinSpeed);
