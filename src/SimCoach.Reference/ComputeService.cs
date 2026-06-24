using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Hosted service that turns the live telemetry stream into the four compute domain events. Subscribes
/// to the telemetry fan-out in its constructor (so opening frames are not lost), awaits session
/// identity (ADR-0011), then drives a <see cref="ComputeSession"/> frame by frame and publishes its
/// events on the <see cref="DomainEventFanOut"/>. On stream end it emits the <c>SessionEvent</c> and
/// completes the fan-out — even under cancellation. Registered before <c>IngestService</c> and after
/// the recorder so it drains and writes all lap rows before <c>SessionManager</c> finalizes.
/// </summary>
public sealed class ComputeService : BackgroundService
{
    private readonly TelemetrySubscription _subscription;
    private readonly DomainEventFanOut _domainFanOut;
    private readonly SessionContext _sessionContext;
    private readonly TrackModelStore _trackModels;
    private readonly ReferenceLookup _lookup;
    private readonly ReferenceStore _referenceStore;
    private readonly LapRepository _laps;
    private readonly ITrackLengthProvider _lengths;
    private readonly ComputeOptions _options;
    private readonly ILogger<ComputeService> _logger;

    public ComputeService(
        TelemetryFanOut fanOut,
        DomainEventFanOut domainFanOut,
        SessionContext sessionContext,
        TrackModelStore trackModels,
        ReferenceLookup lookup,
        ReferenceStore referenceStore,
        LapRepository laps,
        ITrackLengthProvider lengths,
        ComputeOptions options,
        ILogger<ComputeService> logger)
    {
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(domainFanOut);
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(trackModels);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(referenceStore);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _domainFanOut = domainFanOut;
        _sessionContext = sessionContext;
        _trackModels = trackModels;
        _lookup = lookup;
        _referenceStore = referenceStore;
        _laps = laps;
        _lengths = lengths;
        _options = options;
        _logger = logger;
        _subscription = fanOut.Subscribe("compute");
    }

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SessionIdentity identity;
        try
        {
            identity = await _sessionContext.Ready.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _domainFanOut.Complete();
            return; // shutdown before a session ever started
        }

        var session = new ComputeSession(
            _domainFanOut, _trackModels, _lookup, _referenceStore, _laps, _lengths, _options, _logger, identity);

        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                session.Accept(frame);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — still emit the session aggregate below.
        }
        finally
        {
            session.Complete();
            _logger.LogInformation("Compute stopped for session {Session}", identity.SessionId);
        }
    }
}
