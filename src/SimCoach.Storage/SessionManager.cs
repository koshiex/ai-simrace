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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        SessionContext sessionContext,
        TelemetryFanOut fanOut,
        RecordingOptions options,
        SessionRepository sessions,
        LapRepository laps,
        TimeProvider timeProvider,
        ILogger<SessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _sessionContext = sessionContext;
        _options = options;
        _sessions = sessions;
        _laps = laps;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscription = fanOut.Subscribe("session-manager");
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
            return; // shutdown before a session ever started
        }

        string sessionDirectory = Path.Combine(_options.BasePath, identity.SessionId);
        Directory.CreateDirectory(sessionDirectory);
        _logger.LogInformation(
            "Session {SessionId} directory ready at {SessionDirectory}", identity.SessionId, sessionDirectory);

        bool inserted = false;
        WeatherWindow weather = new(_weatherWindow);

        try
        {
            await foreach (TelemetryFrame frame in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                weather.Observe(frame.T.ToDateTimeOffset(), frame.WeatherBucket);
                if (!inserted)
                {
                    _sessions.Insert(NewRow(identity, frame, sessionDirectory));
                    inserted = true;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — finalize what we have below.
        }
        finally
        {
            if (inserted)
            {
                Finalize(identity.SessionId, weather.Resolve());
            }
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

    private void Finalize(string sessionId, string weatherBucket)
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

        _sessions.Finalize(
            sessionId, _timeProvider.GetUtcNow(), weatherBucket, laps.Count, cleanCount, pbTimeMs, parquetPath: null);
        _logger.LogInformation(
            "Session {SessionId} finalized: {LapCount} lap(s), {CleanCount} clean, weather {Weather}",
            sessionId, laps.Count, cleanCount, weatherBucket);
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
