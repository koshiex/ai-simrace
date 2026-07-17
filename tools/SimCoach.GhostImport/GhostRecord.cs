namespace SimCoach.GhostImport;

/// <summary>
/// One decoded 130-byte ACC ghost record (little-endian), per
/// <c>docs/05-implementation/acc-ghost-format-re.md</c>. Only <see cref="WorldX"/>/<see cref="WorldZ"/>
/// are load-bearing for the beyond-PB alien LINE; <see cref="WorldY"/>/<see cref="Yaw"/>/pedals/
/// <see cref="RawTimestamp"/> are decoded but LINE-only — they never feed a TIME reference (the
/// <c>+126</c> clock is logarithmically encoded and its derived speed is untrustworthy).
/// </summary>
public readonly record struct GhostRecord(
    float WorldX,
    float WorldY,
    float WorldZ,
    float Yaw,
    float BrakeNorm,
    float ThrottleNorm,
    float RawTimestamp);
