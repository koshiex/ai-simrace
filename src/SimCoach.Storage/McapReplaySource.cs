using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage.Mcap;

namespace SimCoach.Storage;

/// <summary>
/// Replays recorded MCAP segments through the <see cref="ITelemetrySource"/> contract — the
/// macOS dev loop and the test harness for compute work: everything downstream of the ACC
/// reader runs identically against a recording. Honors original inter-frame timing scaled by
/// <see cref="ReplayOptions.SpeedMultiplier"/> (0 = as fast as possible); waits run on the
/// injected <see cref="TimeProvider"/>. The stream ends when the segments end or the token
/// cancels — gracefully, like the live ACC source.
/// </summary>
public sealed class McapReplaySource : ITelemetrySource
{
    public const string SimId = "replay";

    private readonly ReplayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McapReplaySource> _logger;

    public McapReplaySource(ReplayOptions options, TimeProvider timeProvider, ILogger<McapReplaySource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Sim => SimId;

    public async IAsyncEnumerable<TelemetryFrame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        IReadOnlyList<string> segmentPaths = McapSegmentEnumerator.ResolveSegmentPaths(_options.Path);
        _logger.LogInformation(
            "Replaying {SegmentCount} segment(s) from {Path} at speed {Speed}",
            segmentPaths.Count,
            _options.Path,
            _options.SpeedMultiplier);

        // Pacing runs against an absolute schedule (start timestamp + accumulated target),
        // not per-frame relative delays — timer overshoot must not accumulate into drift
        // (3 ms gaps at 333 Hz vs ~16 ms Windows timer granularity would replay ~5x slow).
        ulong? previousLogTimeNs = null;
        long replayStartTimestamp = 0;
        TimeSpan targetElapsed = TimeSpan.Zero;
        foreach (string segmentPath in segmentPaths)
        {
            McapSegment segment = ReadSegment(segmentPath);
            foreach (McapMessage message in segment.Messages)
            {
                if (ct.IsCancellationRequested)
                {
                    yield break;
                }

                if (previousLogTimeNs is null)
                {
                    replayStartTimestamp = _timeProvider.GetTimestamp();
                }
                else
                {
                    targetElapsed += ScaledGap(previousLogTimeNs.Value, message.LogTimeNs);
                    TimeSpan remaining = targetElapsed - _timeProvider.GetElapsedTime(replayStartTimestamp);
                    if (remaining > TimeSpan.Zero && !await TryDelayAsync(remaining, ct).ConfigureAwait(false))
                    {
                        yield break; // cancelled mid-wait — end the stream gracefully
                    }
                }

                previousLogTimeNs = message.LogTimeNs;
                yield return TelemetryFrame.Parser.ParseFrom(message.Data);
            }
        }
    }

    private static McapSegment ReadSegment(string segmentPath)
    {
        using FileStream stream = File.OpenRead(segmentPath);
        return McapSegment.Read(stream);
    }

    private const double NanosPerSecond = 1e9;

    /// <summary>The capped, speed-scaled recorded gap between two consecutive frames.</summary>
    private TimeSpan ScaledGap(ulong previousLogTimeNs, ulong currentLogTimeNs)
    {
        if (_options.SpeedMultiplier <= 0 || currentLogTimeNs <= previousLogTimeNs)
        {
            return TimeSpan.Zero;
        }

        double seconds = (currentLogTimeNs - previousLogTimeNs) / NanosPerSecond / _options.SpeedMultiplier;
        var gap = TimeSpan.FromSeconds(seconds);
        return gap <= _options.MaxFrameDelay ? gap : _options.MaxFrameDelay;
    }

    private async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
