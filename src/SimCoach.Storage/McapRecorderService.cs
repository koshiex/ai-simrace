using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage.Mcap;

namespace SimCoach.Storage;

/// <summary>
/// Records the telemetry stream to rotating MCAP segments:
/// <c>&lt;BasePath&gt;/&lt;sessionId&gt;/segment-NNN.mcap</c>. Each segment is self-contained
/// (own Schema/Channel records) so any segment can be read or replayed in isolation.
/// Subscribes to the fan-out in the constructor so frames published before this service's
/// ExecuteAsync runs are not lost (hosted services start sequentially).
/// </summary>
public sealed class McapRecorderService : BackgroundService
{
    private const string Topic = "telemetry";
    private const string ProtobufEncoding = "protobuf";

    private readonly TelemetrySubscription _subscription;
    private readonly RecordingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McapRecorderService> _logger;

    public McapRecorderService(
        TelemetryFanOut fanOut,
        RecordingOptions options,
        TimeProvider timeProvider,
        ILogger<McapRecorderService> logger)
    {
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscription = fanOut.Subscribe("recorder");
    }

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Millisecond suffix: a crash + restart within one second must not reuse (and truncate)
        // the previous session's directory.
        string sessionId = _timeProvider.GetUtcNow()
            .ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string sessionDirectory = Path.Combine(_options.BasePath, sessionId);
        byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);

        McapWriter? writer = null;
        ushort channelId = 0;
        uint sequence = 0;
        int segmentIndex = 0;
        long segmentStartedAt = 0;

        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                bool needsRotation = writer is null
                    || _timeProvider.GetElapsedTime(segmentStartedAt) >= _options.SegmentDuration;
                if (needsRotation)
                {
                    if (writer is not null)
                    {
                        LogSegmentFinished(segmentIndex - 1, sequence, segmentStartedAt);
                        writer.Dispose();
                    }

                    (writer, channelId) = StartSegment(sessionDirectory, segmentIndex, schemaData);
                    sequence = 0;
                    segmentStartedAt = _timeProvider.GetTimestamp();
                    segmentIndex++;
                }

                ulong logTimeNs = ToUnixNanos(frame.T);
                writer!.WriteMessage(channelId, sequence++, logTimeNs, logTimeNs, frame.ToByteArray());
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            if (writer is not null)
            {
                LogSegmentFinished(segmentIndex - 1, sequence, segmentStartedAt);
                writer.Dispose();
            }

            _logger.LogInformation(
                "Recorder stopped: {SegmentCount} segment(s) in {SessionDirectory}",
                segmentIndex,
                sessionDirectory);
        }
    }

    /// <summary>The effective rate in this line is the manual live-ACC verification signal (~333 Hz).</summary>
    private void LogSegmentFinished(int segmentIndex, uint frameCount, long segmentStartedAt)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(segmentStartedAt);
        double effectiveHz = elapsed > TimeSpan.Zero ? frameCount / elapsed.TotalSeconds : 0;
        _logger.LogInformation(
            "Segment {SegmentIndex} finished: {FrameCount} frames in {ElapsedSeconds:F1} s (~{EffectiveHz:F0} Hz)",
            segmentIndex,
            frameCount,
            elapsed.TotalSeconds,
            effectiveHz);
    }

    private (McapWriter Writer, ushort ChannelId) StartSegment(
        string sessionDirectory, int segmentIndex, byte[] schemaData)
    {
        Directory.CreateDirectory(sessionDirectory);
        string segmentPath = Path.Combine(
            sessionDirectory,
            string.Create(CultureInfo.InvariantCulture, $"segment-{segmentIndex:0000}.mcap"));
        FileStream stream = File.Create(segmentPath);
        try
        {
            McapWriter writer = new(stream);
            ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, ProtobufEncoding, schemaData);
            ushort channelId = writer.AddChannel(schemaId, Topic, ProtobufEncoding);
            _logger.LogInformation("Recording telemetry to {SegmentPath}", segmentPath);
            return (writer, channelId);
        }
        catch
        {
            // Disk-full etc. mid-initialization: release the file handle instead of leaking it.
            stream.Dispose();
            throw;
        }
    }

    private static ulong ToUnixNanos(Timestamp? timestamp)
    {
        if (timestamp is null || timestamp.Seconds < 0 || timestamp.Nanos < 0)
        {
            return 0UL; // pre-epoch or malformed timestamps clamp to 0 instead of wrapping huge
        }

        return (ulong)timestamp.Seconds * 1_000_000_000UL + (ulong)timestamp.Nanos;
    }
}
