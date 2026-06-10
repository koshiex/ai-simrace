namespace SimCoach.Adapters.ACC.Tests;

/// <summary>Manually advanced <see cref="TimeProvider"/> for deterministic interval tests.</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _utcNow.Ticks;

    // GetTimestamp returns DateTimeOffset ticks, so the frequency must match (10 MHz), not the
    // base Stopwatch.Frequency — otherwise GetElapsedTime distorts durations ~100x.
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
