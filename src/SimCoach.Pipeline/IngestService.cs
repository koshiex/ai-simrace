using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline;

/// <summary>
/// Pumps frames from the active <see cref="ITelemetrySource"/> into the
/// <see cref="TelemetryFanOut"/> so every consumer (recorder now, compute in Phase 2) gets the
/// full stream. Dropped-frame totals are logged at most once per
/// <see cref="IngestOptions.DropLogInterval"/>.
/// </summary>
public sealed class IngestService : BackgroundService
{
    private readonly ITelemetrySource _source;
    private readonly TelemetryFanOut _fanOut;
    private readonly IngestOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestService> _logger;
    private readonly IHostApplicationLifetime? _applicationLifetime;
    private long _lastDropLogTimestamp;
    private long _lastLoggedDropTotal;
    private bool _hasLoggedDrops;

    public IngestService(
        ITelemetrySource source,
        TelemetryFanOut fanOut,
        IngestOptions options,
        TimeProvider timeProvider,
        ILogger<IngestService> logger,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _source = source;
        _fanOut = fanOut;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry ingest started for sim {Sim}", _source.Sim);
        try
        {
            await foreach (TelemetryFrame frame in _source.ReadAsync(stoppingToken).ConfigureAwait(false))
            {
                _fanOut.Publish(frame);
                LogDropsThrottled();
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                // A finite source (replay) ended on its own — stop the host instead of idling.
                _logger.LogInformation("Telemetry source {Sim} ended; requesting application stop", _source.Sim);
                _applicationLifetime?.StopApplication();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — some sources surface cancellation as OperationCanceledException.
        }
        finally
        {
            _fanOut.Complete();
            // The final total is the number people look for after a bad session — never throttle it.
            _logger.LogInformation(
                "Telemetry ingest stopped for sim {Sim}; {DroppedTotal} frames dropped in total",
                _source.Sim,
                _fanOut.TotalDroppedFrames);
        }
    }

    private void LogDropsThrottled()
    {
        long total = _fanOut.TotalDroppedFrames;
        if (total == _lastLoggedDropTotal)
        {
            return;
        }

        if (_hasLoggedDrops && _timeProvider.GetElapsedTime(_lastDropLogTimestamp) < _options.DropLogInterval)
        {
            return;
        }

        _logger.LogWarning(
            "Slow telemetry consumers dropped {DroppedTotal} frames so far (+{DroppedDelta} since last report)",
            total,
            total - _lastLoggedDropTotal);
        _lastLoggedDropTotal = total;
        _lastDropLogTimestamp = _timeProvider.GetTimestamp();
        _hasLoggedDrops = true;
    }
}
