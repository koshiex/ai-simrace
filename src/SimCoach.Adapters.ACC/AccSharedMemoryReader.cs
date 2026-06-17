using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SimCoach.Adapters.ACC.SharedMemory;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// ACC telemetry source: a dedicated thread polls the shared-memory pages (333 Hz target),
/// pushes mapped frames into a bounded drop-oldest channel, and reconnects transparently when
/// the game is not running. The snapshot→frame mapper is injected (see AccFrameMapper, plan B3).
/// One concurrent <see cref="ReadAsync"/> enumeration per instance.
/// </summary>
public sealed class AccSharedMemoryReader : ITelemetrySource
{
    public const string SimId = "acc";

    private readonly IAccPageSource _pageSource;
    private readonly Func<AccTelemetrySnapshot, TelemetryFrame> _frameMapper;
    private readonly AccReaderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccSharedMemoryReader> _logger;
    private int _activeEnumerations;

    public AccSharedMemoryReader(
        IAccPageSource pageSource,
        Func<AccTelemetrySnapshot, TelemetryFrame> frameMapper,
        AccReaderOptions options,
        TimeProvider timeProvider,
        ILogger<AccSharedMemoryReader> logger)
    {
        ArgumentNullException.ThrowIfNull(pageSource);
        ArgumentNullException.ThrowIfNull(frameMapper);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _pageSource = pageSource;
        _frameMapper = frameMapper;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Sim => SimId;

    public async IAsyncEnumerable<TelemetryFrame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // The page source and its buffers are single-threaded; a second poll thread would race it.
        if (Interlocked.CompareExchange(ref _activeEnumerations, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(AccSharedMemoryReader)} supports one concurrent {nameof(ReadAsync)} enumeration per instance.");
        }

        var channel = Channel.CreateBounded<TelemetryFrame>(
            new BoundedChannelOptions(_options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
            });

        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pollThread = new Thread(() => PollLoop(channel.Writer, pollCts.Token))
        {
            IsBackground = true,
            Name = "simcoach-acc-shm-poll",
        };
        pollThread.Start();

        try
        {
            // The poll loop completes the channel on cancellation, so the stream ends
            // gracefully instead of throwing OperationCanceledException at the consumer.
            await foreach (TelemetryFrame frame in channel.Reader.ReadAllAsync(CancellationToken.None)
                               .ConfigureAwait(false))
            {
                yield return frame;
            }
        }
        finally
        {
            pollCts.Cancel();
            pollThread.Join();
            Volatile.Write(ref _activeEnumerations, 0);
        }
    }

    private void PollLoop(ChannelWriter<TelemetryFrame> writer, CancellationToken ct)
    {
        var acquisition = new AccFrameAcquisition(_pageSource, _timeProvider, _options);
        bool isConnected = false;

        // Raise the Windows timer resolution so the sub-tick PollInterval waits aren't rounded
        // up to ~15.6 ms (which would cap the emit rate at ~64 Hz). No-op off Windows.
        using var timerResolution = new WinTimerResolution();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!isConnected)
                {
                    if (!_pageSource.TryConnect())
                    {
                        WaitUnlessCancelled(_options.ReconnectDelay, ct);
                        continue;
                    }

                    acquisition.Reset();
                    isConnected = true;
                    _logger.LogInformation("Connected to ACC shared memory");
                }

                AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);
                if (status == AccAcquisitionStatus.Disconnected)
                {
                    isConnected = false;
                    _logger.LogWarning(
                        "ACC shared memory lost; retrying every {ReconnectDelay}", _options.ReconnectDelay);

                    // Guards against hot-spinning if a source reconnects but keeps failing reads.
                    WaitUnlessCancelled(_options.ReconnectDelay, ct);
                    continue;
                }

                if (status == AccAcquisitionStatus.NewFrame && snapshot is not null)
                {
                    writer.TryWrite(_frameMapper(snapshot));
                }

                WaitUnlessCancelled(_options.PollInterval, ct);
            }

            writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACC shared-memory poll loop crashed");
            writer.TryComplete(ex);
        }
    }

    private static void WaitUnlessCancelled(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero)
        {
            Thread.Yield();
            return;
        }

        ct.WaitHandle.WaitOne(delay);
    }
}
