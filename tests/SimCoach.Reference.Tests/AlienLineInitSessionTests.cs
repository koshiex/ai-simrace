using FluentAssertions;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// PR-B3 commit 22 runtime integration of the alien_line LINE reference into <see cref="ComputeSession"/>:
/// fault isolation of a corrupt import (M3), the LINE-only invariant that the alien line never feeds
/// <c>_reference</c>/TIME, and the M7 weather-mismatch diagnostic. Synthetic frames only — no ghost, no network.
/// </summary>
public sealed class AlienLineInitSessionTests
{
    private const string SessionId = "20260601-120000-000";
    private static readonly DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    // The synthetic Spa stream's identity (SyntheticSessionBuilder: acc / synthetic_gt3 / dry-warm).
    private static ReferenceTriple SyntheticTriple => new("spa", "synthetic_gt3", "dry-warm");

    [Fact]
    public async Task A_corrupt_alien_parquet_is_fault_isolated_and_the_session_still_completes()
    {
        using var harness = new ComputeTestHarness();
        // A multi-row-group parquet: ReferenceParquetCodec.Read throws InvalidDataException, which
        // TryLoadAlienLine must catch and fall through to centerline/PB — never exit InitSession.
        string corrupt = Path.Combine(harness.ReferencesDirectory, "corrupt.parquet");
        Directory.CreateDirectory(harness.ReferencesDirectory);
        CorruptReferenceParquet.WriteMultiRowGroup(corrupt);
        harness.References.Upsert(AlienRow(corrupt, "dry-warm"));

        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        events.OfType<SessionEvent>(DomainEventKind.Session).Should().ContainSingle(
            "a corrupt alien import must degrade to the line fallback, not poison the session");
        events.OfType<LapEvent>(DomainEventKind.Lap).Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_alien_line_never_shifts_the_time_delta_it_is_line_only()
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<int> baseline;
        using (var harness = new ComputeTestHarness())
        {
            baseline = LapDeltas(await harness.RunAsync(frames, SessionId));
        }

        IReadOnlyList<int> withAlien;
        using (var harness = new ComputeTestHarness())
        {
            string parquet = Path.Combine(harness.ReferencesDirectory, "alien.parquet");
            ReferenceParquetCodec.Write(LineOnlyLap.Circle(200, 100f), parquet);
            harness.References.Upsert(AlienRow(parquet, "dry-warm"));
            harness.Lookup.Get(SyntheticTriple, ReferenceKind.AlienLine).Should().NotBeNull(
                "precondition: the alien line resolves so the run actually exercises it as _lineReference");

            withAlien = LapDeltas(await harness.RunAsync(frames, SessionId));
        }

        withAlien.Should().Equal(
            baseline, "the alien line is LINE-only — it changes line cues, never the TIME delta (_reference untouched)");
    }

    [Fact]
    public void A_wet_session_gates_off_the_embedded_line_and_logs_a_mismatched_import_as_inactive()
    {
        using var harness = new ComputeTestHarness();
        // Wet session: the dry-baked embedded alien line is gated OFF, and the only import is stamped dry-cool →
        // it never resolves (OD6). The session runs on the centerline/PB line; M7 surfaces the inactive import.
        harness.References.Upsert(AlienRow("unused.parquet", "dry-cool"));

        var logger = new CollectingLogger();
        ComputeSession session = NewSpaSession(harness, logger);

        // A single partial lap: InitSession runs on the first frame; no lap completes, so no FK/persistence.
        foreach (TelemetryFrame frame in
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1, weatherBucket: "wet"))
        {
            session.Accept(frame);
        }

        IReadOnlyList<(LogLevel Level, string Message)> snapshot = logger.Snapshot();
        snapshot.Should().Contain(
            e => e.Level == LogLevel.Information
                && e.Message.Contains("alien_line present", StringComparison.Ordinal)
                && e.Message.Contains("dry-cool", StringComparison.Ordinal),
            "a bucket-mismatched alien_line must be surfaced as present-but-inactive");
        snapshot.Should().NotContain(
            e => e.Message.Contains("line alien_line", StringComparison.Ordinal),
            "the dry-baked embedded alien line must NOT activate in the wet");
    }

    [Fact]
    public void A_dry_session_activates_the_vendored_embedded_alien_line()
    {
        using var harness = new ComputeTestHarness();
        // Dry session, no import row: the vendored embedded Spa alien line takes over as the LINE reference
        // (OD10, works out-of-box). Dry buckets share the dry-baked line (dry-cool ≈ dry-warm).
        var logger = new CollectingLogger();
        ComputeSession session = NewSpaSession(harness, logger);

        foreach (TelemetryFrame frame in
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1, weatherBucket: "dry-warm"))
        {
            session.Accept(frame);
        }

        logger.Snapshot().Should().Contain(
            e => e.Message.Contains("line alien_line", StringComparison.Ordinal),
            "in the dry the vendored embedded alien line activates as the LINE reference");
    }

    private static ComputeSession NewSpaSession(ComputeTestHarness harness, CollectingLogger logger) => new(
        harness.DomainFanOut, harness.TrackModels, harness.Centerlines, harness.AlienLines, harness.Lookup,
        harness.OptimalLookup, harness.ReferenceStore, harness.Laps, FakeTrackLengths.Spa(), new ComputeOptions(),
        logger, new SessionIdentity("m7", DateTimeOffset.UnixEpoch));

    private static ReferenceRow AlienRow(string parquetPath, string weather) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = weather,
        LapTimeMs = 100030,
        ParquetPath = parquetPath,
        CreatedAtUtc = _now,
        Kind = "alien_line",
        SectorSourcesJson = "{\"source_car\":\"synthetic_gt3\"}",
    };

    private static IReadOnlyList<int> LapDeltas(IReadOnlyList<DomainEvent> events) =>
        [.. events.OfType<LapEvent>(DomainEventKind.Lap).Select(l => l.DeltaMs)];
}
