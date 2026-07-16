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
    private readonly CenterlineGeometryDataset _centerlines;
    private readonly AlienLineDataset _alienLines;
    private readonly ReferenceLookup _lookup;
    private readonly OptimalReferenceLookup _optimalLookup;
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
        CenterlineGeometryDataset centerlines,
        AlienLineDataset alienLines,
        ReferenceLookup lookup,
        OptimalReferenceLookup optimalLookup,
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
        ArgumentNullException.ThrowIfNull(centerlines);
        ArgumentNullException.ThrowIfNull(alienLines);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(optimalLookup);
        ArgumentNullException.ThrowIfNull(referenceStore);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _domainFanOut = domainFanOut;
        _sessionContext = sessionContext;
        _trackModels = trackModels;
        _centerlines = centerlines;
        _alienLines = alienLines;
        _lookup = lookup;
        _optimalLookup = optimalLookup;
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
            // The reference/lap rows compute writes are FK-bound to the sessions row, which
            // SessionManager inserts on its first frame. Wait for that insert so a fast replay can't
            // let compute drain a whole lap and upsert a reference before the row exists.
            await _sessionContext.Persisted.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _domainFanOut.Complete();
            return; // shutdown before a session ever started or was persisted
        }

        var session = new ComputeSession(
            _domainFanOut, _trackModels, _centerlines, _alienLines, _lookup, _optimalLookup, _referenceStore, _laps,
            _lengths, _options, _logger, identity);

        // Backstop: a per-frame compute fault must never bubble out of ExecuteAsync, because the host's
        // default BackgroundServiceExceptionBehavior is StopHost — one bad frame would stop the recorder
        // too. The known crash (a pit-return duplicate lap_number) is already prevented upstream
        // (monotonic renumbering) and caught at the lap write; this catch only fires on something
        // unforeseen. It is rate-limited (first at Error, then a single aggregate count) so a persistent
        // fault at ~400 Hz cannot flood the log, and it may run on partially-mutated session state.
        // OperationCanceledException from the enumerator is NOT caught here — it flows to the outer
        // handler for graceful shutdown.
        int acceptFailures = 0;
        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    session.Accept(frame);
                }
                catch (Exception ex)
                {
                    if (acceptFailures == 0)
                    {
                        _logger.LogError(
                            ex, "Compute frame failed for session {Session}; isolating and continuing",
                            identity.SessionId);
                    }

                    acceptFailures++;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — still emit the session aggregate below.
        }
        finally
        {
            if (acceptFailures > 0)
            {
                _logger.LogWarning(
                    "{Count} compute frame(s) failed and were skipped for session {Session}",
                    acceptFailures, identity.SessionId);
            }

            session.Complete();
            _logger.LogInformation("Compute stopped for session {Session}", identity.SessionId);
        }
    }
}
