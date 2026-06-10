namespace SimCoach.Pipeline.Tests;

/// <summary>
/// Manually advanced <see cref="TimeProvider"/> for deterministic interval tests.
/// Thread-safe: tests advance it while a service thread reads timestamps.
/// </summary>
internal sealed class FakeClock : TimeProvider
{
    private long _utcNowTicks = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _utcNowTicks), TimeSpan.Zero);

    public override long GetTimestamp() => Volatile.Read(ref _utcNowTicks);

    // GetTimestamp returns DateTimeOffset ticks, so the frequency must match (10 MHz).
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcNowTicks, delta.Ticks);
}
