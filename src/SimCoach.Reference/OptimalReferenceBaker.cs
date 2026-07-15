using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Hosted service that keeps the own-optimal ("theoretical best") reference (M46) in sync with the
/// historical data. In <see cref="StartAsync"/> it runs a one-shot, IDEMPOTENT CATCH-UP bake: for every
/// stored PB reference it re-derives the per-sector optimal from that triple's clean laps and upserts the
/// row-only optimal — so existing recordings yield an optimal without a fresh drive.
///
/// It is deliberately NOT a participant in the load-bearing reversed stop-order (its <see cref="StopAsync"/>
/// is a no-op). The post-session live number is computed by the debrief itself; nothing here runs at
/// shutdown. Best-effort by design: a catch-up failure is logged, never fatal — the coach must still start.
/// </summary>
public sealed class OptimalReferenceBaker : IHostedService
{
    private readonly ReferenceRepository _references;
    private readonly LapRepository _laps;
    private readonly OptimalReferenceOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OptimalReferenceBaker> _logger;

    public OptimalReferenceBaker(
        ReferenceRepository references,
        LapRepository laps,
        OptimalReferenceOptions options,
        TimeProvider time,
        ILogger<OptimalReferenceBaker> logger)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _references = references;
        _laps = laps;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        BakeAll();
        return Task.CompletedTask;
    }

    /// <summary>No-op: the baker is not part of the reversed stop-order; nothing runs at shutdown.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void BakeAll()
    {
        IReadOnlyList<ReferenceRow> pbReferences;
        try
        {
            pbReferences = _references.GetAllByKind(ReferenceKind.Pb.ToDbString());
        }
        catch (Exception ex)
        {
            // Best-effort catch-up: a failure to enumerate must not block the host from coaching.
            _logger.LogError(ex, "Optimal catch-up bake could not enumerate PB references; skipping");
            return;
        }

        int baked = 0;
        foreach (ReferenceRow pb in pbReferences)
        {
            try
            {
                if (BakeTriple(pb))
                {
                    baked++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Optimal catch-up bake failed for {Track}/{Car}/{Weather}; continuing",
                    pb.TrackId, pb.CarId, pb.WeatherBucket);
            }
        }

        _logger.LogInformation(
            "Optimal catch-up bake complete: {Baked} of {Total} PB triples produced an optimal",
            baked, pbReferences.Count);
    }

    // Returns true when an optimal row was written (or refreshed) for the triple, false when a guard
    // blocked it (no gain over PB, no usable clean laps) or the stored optimal is already up to date.
    private bool BakeTriple(ReferenceRow pb)
    {
        IReadOnlyList<CleanLapSectors> cleanLaps =
            _laps.BestSectorsByTriple(pb.TrackId, pb.CarId, pb.WeatherBucket);

        OptimalReference? optimal = OptimalReferenceBuilder.Build(cleanLaps, pb.LapTimeMs, _options);
        if (optimal is null)
        {
            return false;
        }

        string sectorsJson = JsonSerializer.Serialize(optimal.SectorDurationsMs);

        // Idempotence: the builder is deterministic, so an unchanged input yields an unchanged optimal.
        // Skip the write when the stored durations already match — keeps created_at stable, no churn.
        ReferenceRow? existing = _references.GetByTriple(
            pb.TrackId, pb.CarId, pb.WeatherBucket, ReferenceKind.Optimal.ToDbString());
        if (existing is not null && existing.OptimalSectorMs == sectorsJson)
        {
            return false;
        }

        _references.Upsert(new ReferenceRow
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            TrackId = pb.TrackId,
            CarId = pb.CarId,
            WeatherBucket = pb.WeatherBucket,
            SourceSessionId = null,
            SourceLapNumber = null,
            LapTimeMs = optimal.TargetLapTimeMs,
            ParquetPath = null,
            Pinned = false,
            CreatedAtUtc = _time.GetUtcNow(),
            Kind = ReferenceKind.Optimal.ToDbString(),
            OptimalSectorMs = sectorsJson,
            SectorSourcesJson = JsonSerializer.Serialize(optimal.Sources),
        });

        _logger.LogInformation(
            "Optimal baked for {Track}/{Car}/{Weather}: target {TargetMs} ms vs PB {PbMs} ms",
            pb.TrackId, pb.CarId, pb.WeatherBucket, optimal.TargetLapTimeMs, pb.LapTimeMs);
        return true;
    }
}
