namespace SimCoach.Voice;

/// <summary>
/// TTS backend abstraction. Implementations stream PCM chunks for low-latency playback;
/// callers cancel via the supplied <see cref="CancellationToken"/> when a tip becomes stale.
/// </summary>
public interface ITtsBackend
{
    /// <summary>
    /// Synthesize <paramref name="text"/> and stream 16-bit PCM frames at <see cref="SampleRateHz"/>.
    /// First frame must arrive within ~200 ms p50 for the contract to be met (NFR-001 for voice).
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> StreamAsync(string text, CancellationToken ct);

    int SampleRateHz { get; }
    int Channels { get; }
}
