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
        IReadOnlyList<string> segmentPaths = ResolveSegmentPaths(_options.Path);
        _logger.LogInformation(
            "Replaying {SegmentCount} segment(s) from {Path} at speed {Speed}",
            segmentPaths.Count,
            _options.Path,
            _options.SpeedMultiplier);

        ulong? previousLogTimeNs = null;
        foreach (string segmentPath in segmentPaths)
        {
            McapSegment segment = ReadSegment(segmentPath);
            foreach (McapMessage message in segment.Messages)
            {
                if (ct.IsCancellationRequested)
                {
                    yield break;
                }

                TimeSpan delay = ComputeDelay(previousLogTimeNs, message.LogTimeNs);
                previousLogTimeNs = message.LogTimeNs;
                if (delay > TimeSpan.Zero && !await TryDelayAsync(delay, ct).ConfigureAwait(false))
                {
                    yield break; // cancelled mid-wait — end the stream gracefully
                }

                yield return TelemetryFrame.Parser.ParseFrom(message.Data);
            }
        }
    }

    private static IReadOnlyList<string> ResolveSegmentPaths(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        if (Directory.Exists(path))
        {
            string[] segments = Directory.GetFiles(path, "*.mcap");
            if (segments.Length == 0)
            {
                throw new FileNotFoundException($"No .mcap segments found in '{path}'.");
            }

            Array.Sort(segments, StringComparer.Ordinal); // segment-NNNN names sort chronologically
            return segments;
        }

        throw new FileNotFoundException($"Replay path '{path}' does not exist.");
    }

    private static McapSegment ReadSegment(string segmentPath)
    {
        using FileStream stream = File.OpenRead(segmentPath);
        return McapSegment.Read(stream);
    }

    private TimeSpan ComputeDelay(ulong? previousLogTimeNs, ulong currentLogTimeNs)
    {
        if (_options.SpeedMultiplier <= 0
            || previousLogTimeNs is null
            || currentLogTimeNs <= previousLogTimeNs.Value)
        {
            return TimeSpan.Zero;
        }

        double seconds = (currentLogTimeNs - previousLogTimeNs.Value) / 1e9 / _options.SpeedMultiplier;
        var delay = TimeSpan.FromSeconds(seconds);
        return delay <= _options.MaxFrameDelay ? delay : _options.MaxFrameDelay;
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
