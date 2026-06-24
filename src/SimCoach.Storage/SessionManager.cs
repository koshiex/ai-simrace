using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage.Repositories;

namespace SimCoach.Storage;

/// <summary>
/// Single owner of the <c>sessions</c> row and the session directory (ADR-0011). Resolves identity
/// from the shared <see cref="SessionContext"/> (allocated by the producer before frame #1), creates
/// <c>&lt;BasePath&gt;/&lt;SessionId&gt;</c>, inserts the row on the first frame, and finalizes it
/// (ended-at, authoritative weather bucket, counts/PB from persisted laps) when the stream ends.
/// Subscribes to the fan-out in the constructor so no opening frames are missed.
/// </summary>
public sealed class SessionManager : BackgroundService
{
    /// <summary>
    /// Window (relative to the latest frame) used to pick the authoritative weather bucket. ACC
    /// temps read 0 for ~21 s after going LIVE (ADR-0008); sampling the tail of the session skips
    /// that warm-up so the bucket that keys the references triple is the settled one.
    /// </summary>
    private static readonly TimeSpan _weatherWindow = TimeSpan.FromSeconds(30);

    private readonly SessionContext _sessionContext;
    private readonly TelemetrySubscription _subscription;
    private readonly RecordingOptions _options;
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;
    private readonly ITrackLengthProvider _trackLengths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        SessionContext sessionContext,
        TelemetryFanOut fanOut,
        RecordingOptions options,
        SessionRepository sessions,
        LapRepository laps,
        ITrackLengthProvider trackLengths,
        TimeProvider timeProvider,
        ILogger<SessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(trackLengths);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _sessionContext = sessionContext;
        _options = options;
        _sessions = sessions;
        _laps = laps;
        _trackLengths = trackLengths;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscription = fanOut.Subscribe("session-manager");
    }

    private readonly WeatherWindow _weather = new(_weatherWindow);
    private SessionIdentity? _identity;
    private string _sessionDirectory = string.Empty;
    private string _trackId = string.Empty;
    private bool _inserted;
    private bool _finalized;

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Finalizes the row here rather than at stream end. The host stops services in reverse
    /// registration order and awaits each; SessionManager is registered first so it stops LAST — after
    /// ComputeService has fully drained and written its lap rows. Finalizing in the ExecuteAsync finally
    /// would race compute, since every subscriber's loop ends together when the fan-out completes.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_inserted && !_finalized && _identity is not null)
        {
            _finalized = true;
            Finalize(_identity.SessionId, _weather.Resolve(), _trackId, _sessionDirectory);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _identity = await _sessionContext.Ready.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return; // shutdown before a session ever started
        }

        _sessionDirectory = Path.Combine(_options.BasePath, _identity.SessionId);
        Directory.CreateDirectory(_sessionDirectory);
        _logger.LogInformation(
            "Session {SessionId} directory ready at {SessionDirectory}", _identity.SessionId, _sessionDirectory);

        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _weather.Observe(frame.T.ToDateTimeOffset(), frame.WeatherBucket);
                if (!_inserted)
                {
                    _trackId = frame.TrackId;
                    _sessions.Insert(NewRow(_identity, frame, _sessionDirectory));
                    _inserted = true;
                    // The session row now exists — release FK-dependent writers (compute) waiting on it.
                    _sessionContext.MarkPersisted();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — finalization happens in StopAsync, after compute has drained.
        }
    }

    private static SessionRow NewRow(SessionIdentity identity, TelemetryFrame frame, string sessionDirectory) => new()
    {
        Id = identity.SessionId,
        StartedAtUtc = identity.StartedAtUtc,
        Sim = frame.Sim,
        TrackId = frame.TrackId,
        CarId = frame.CarId,
        WeatherBucket = frame.WeatherBucket, // provisional; finalized off the temp warm-up window
        McapPath = sessionDirectory,
    };

    private void Finalize(string sessionId, string weatherBucket, string trackId, string sessionDirectory)
    {
        IReadOnlyList<LapRow> laps = _laps.GetBySession(sessionId);
        int cleanCount = 0;
        int? pbTimeMs = null;
        foreach (LapRow lap in laps)
        {
            if (!lap.IsClean)
            {
                continue;
            }

            cleanCount++;
            if (pbTimeMs is null || lap.LapTimeMs < pbTimeMs)
            {
                pbTimeMs = lap.LapTimeMs;
            }
        }

        string? parquetPath = ConvertLapsParquet(trackId, sessionDirectory);
        _sessions.Finalize(
            sessionId, _timeProvider.GetUtcNow(), weatherBucket, laps.Count, cleanCount, pbTimeMs, parquetPath);
        _logger.LogInformation(
            "Session {SessionId} finalized: {LapCount} lap(s), {CleanCount} clean, weather {Weather}",
            sessionId, laps.Count, cleanCount, weatherBucket);
    }

    /// <summary>
    /// Converts the session's flushed MCAP segments to <c>laps.parquet</c> (off the compute hot path,
    /// and after the recorder has stopped — guaranteed by hosted-service stop order). Returns the path
    /// on success, or <c>null</c> when the track length is unknown or conversion fails (logged, never
    /// fatal — finalize must still record counts/PB).
    /// </summary>
    private string? ConvertLapsParquet(string trackId, string sessionDirectory)
    {
        if (!_trackLengths.TryGetLapLengthM(trackId, out float lapLengthM))
        {
            _logger.LogWarning(
                "No lap length for track {Track}; skipping laps.parquet conversion", trackId);
            return null;
        }

        string parquetPath = Path.Combine(sessionDirectory, "laps.parquet");
        try
        {
            int skipped = LapParquetWriter.Write(sessionDirectory, lapLengthM, parquetPath);
            if (skipped > 0)
            {
                _logger.LogInformation(
                    "{Skipped} degenerate lap(s) skipped in laps.parquet for {SessionDirectory}",
                    skipped, sessionDirectory);
            }

            return parquetPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "laps.parquet conversion failed for {SessionDirectory}", sessionDirectory);
            return null;
        }
    }

    /// <summary>
    /// Tracks weather buckets over a trailing time window and resolves the most-common one. Frames
    /// older than the window (relative to the latest frame) are evicted, so resolution is O(window).
    /// </summary>
    private sealed class WeatherWindow(TimeSpan window)
    {
        private readonly Queue<(DateTimeOffset T, string Bucket)> _recent = new();
        private DateTimeOffset _latest;

        public void Observe(DateTimeOffset t, string bucket)
        {
            _latest = t;
            _recent.Enqueue((t, bucket));
            while (_recent.Count > 0 && _latest - _recent.Peek().T > window)
            {
                _recent.Dequeue();
            }
        }

        /// <summary>Most-common bucket in the trailing window; ties broken toward the most recent.</summary>
        public string Resolve()
        {
            Dictionary<string, int> counts = new(StringComparer.Ordinal);
            string best = string.Empty;
            int bestCount = 0;
            foreach ((_, string bucket) in _recent)
            {
                int next = counts.TryGetValue(bucket, out int c) ? c + 1 : 1;
                counts[bucket] = next;
                // >= so a later-seen bucket with an equal count wins the tie (most recent).
                if (next >= bestCount)
                {
                    best = bucket;
                    bestCount = next;
                }
            }

            return best;
        }
    }
}
