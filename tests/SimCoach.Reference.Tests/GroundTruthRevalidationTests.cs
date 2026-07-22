using System.Text.Json;
using FluentAssertions;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;
using Xunit;
using Xunit.Abstractions;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Ground-truth exit gate for the detection-truthfulness pack (Phase-3, TASK 7): decodes a recorded
/// Monza session, re-runs the real <see cref="ComputeSession"/> over the same frames, and asserts the
/// emitted proto numbers match an INDEPENDENT truth-oracle within coaching tolerance (Q9). It exists to
/// prove the NO-GO is lifted — the two headline lies are gone: the 3929 ms "Curva Grande" corner loss
/// and the +14799 ms S1 sector loss.
/// <para>
/// The revalidation fact is env-gated on <c>SIMCOACH_GROUNDTRUTH_FIXTURE</c> (a local session dir):
/// unset → skip, so CI stays green and the raw MCAP never enters the repo (privacy; <c>.gitignore
/// *.mcap</c>). Regenerate the fixture-side inputs with tools/SimCoach.GroundTruthDump +
/// scripts/groundtruth_oracle.py per docs/05-implementation/ground-truth-revalidation.md. The M3
/// helper fact below is hermetic and always runs, but it only checks the <see cref="LossPlausibility"/>
/// helper against the wired default thresholds — the ComputeSession wiring of that guard
/// (EmitCorner/EmitSector/HandleLap/Complete + the pre-overwrite deficit capture) is pinned hermetically
/// by the M3_* facts in <c>ComputeSessionTests</c>.
/// </para>
/// <para>
/// Reality caveat (documented in the plan): the fixture's on-disk reference was overwritten in place by
/// this same PB lap, so the gate seeds a reference from the flying lap and every reference-relative diff
/// collapses to ~0 (self==ref). The assertions are therefore: reference-relative deltas collapse toward
/// zero and, crucially, are FAR from the buggy span-time the oracle measures independently (the
/// discriminator that fails loudly if M2/M16 regress); oracle-grounded absolutes (min speed, brake
/// onset) are validated directly.
/// </para>
/// </summary>
public sealed class GroundTruthRevalidationTests
{
    private const string FixtureEnvVar = "SIMCOACH_GROUNDTRUTH_FIXTURE";
    private const string RequireEnvVar = "SIMCOACH_REQUIRE_GROUNDTRUTH";
    private const float MonzaLapLengthM = 5793f;

    // Q9 moderate tolerance bands for oracle-grounded self-only metrics.
    private const int CornerDeltaToleranceMs = 150;
    private const float MinSpeedToleranceKmh = 3f;
    private const float BrakeToleranceM = 25f;

    // The six phantom-gain corners the plan names (t03 = Curva Grande, t11 = Parabolica).
    private static readonly string[] _namedCorners =
        ["monza_t01", "monza_t02", "monza_t03", "monza_t06", "monza_t09", "monza_t11"];

    private readonly ITestOutputHelper _out;

    public GroundTruthRevalidationTests(ITestOutputHelper outputHelper) => _out = outputHelper;

    [Fact]
    public async Task Emitted_detection_matches_ground_truth_within_coaching_tolerance()
    {
        string? fixtureDir = Environment.GetEnvironmentVariable(FixtureEnvVar);
        if (string.IsNullOrWhiteSpace(fixtureDir))
        {
            // No local fixture. A PR that mutates the NO-GO-certified line/delta math (M34-populate,
            // M38-linedev) sets SIMCOACH_REQUIRE_GROUNDTRUTH so this FAILS loudly — a recorded merge
            // precondition, not a green-because-skipped no-op. Bare CI (flag unset) still skips cleanly.
            if (GroundTruthGate.IsRequired(Environment.GetEnvironmentVariable(RequireEnvVar)))
            {
                throw new InvalidOperationException(
                    $"{RequireEnvVar} is set but {FixtureEnvVar} is not — the ground-truth revalidation gate is a "
                    + "merge precondition for changes to the certified line/delta math (M34-populate, M38-linedev). "
                    + "Point it at a local session dir (tools/SimCoach.GroundTruthDump + scripts/groundtruth_oracle.py); "
                    + "see docs/05-implementation/ground-truth-revalidation.md.");
            }

            return; // Env-gated: no local fixture and not required, skip cleanly (CI path). See the run-book.
        }

        JsonElement truth = LoadTruth(fixtureDir);
        JsonElement truthCorners = truth.GetProperty("corners");

        IReadOnlyList<TelemetryFrame> frames = [.. McapSegmentEnumerator.Read(fixtureDir)];
        frames.Should().NotBeEmpty();
        string trackId = frames[0].TrackId;
        string weather = frames[0].WeatherBucket;

        var lengths = new FakeTrackLengths((trackId, MonzaLapLengthM));
        using var harness = new ComputeTestHarness(lengths, CornerGeometryDataset.Load());

        // Seed the reference from the flying lap, then evaluate the same frames against it (self==ref).
        harness.SeedReference(frames, "gt-seed");
        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, "gt-eval");

        IReadOnlyList<CornerEvent> corners = [.. events.OfType<CornerEvent>(DomainEventKind.Corner)];
        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();

        // --- Positive guard (acceptance #6): the Monza track model wired non-empty corner windows.
        corners.Should().NotBeEmpty("a mis-wired empty-corner harness must fail loudly, never pass vacuously");
        corners.Select(c => c.CornerId).Should().Contain(_namedCorners);

        // --- M2/M24 (acceptance #1): per-corner delta collapses toward zero AND is far from the buggy
        // span-time the oracle measures independently. The span-mismatch bug returned ~-(span time) even
        // self-vs-self (e.g. -3929 ms for Curva Grande), so this discriminates fixed from broken.
        foreach (string id in _namedCorners)
        {
            CornerEvent corner = corners.First(c => c.CornerId == id);
            double oracleSpanMs = truthCorners.GetProperty(id).GetProperty("self_time_at_position_ms").GetDouble();
            _out.WriteLine($"{id}: delta_ms={corner.DeltaMs}, oracle self-span={oracleSpanMs:0} ms");

            oracleSpanMs.Should().BeGreaterThan(
                CornerDeltaToleranceMs * 3, "the discriminator only has teeth when the span time dwarfs the tolerance");
            Math.Abs(corner.DeltaMs).Should().BeLessThanOrEqualTo(
                CornerDeltaToleranceMs, $"{id} delta must collapse to ~0 (self==ref), not leak the span time");
            Math.Abs(corner.DeltaMs - oracleSpanMs).Should().BeGreaterThan(
                CornerDeltaToleranceMs, $"{id} delta must NOT equal the raw span time (the span-mismatch bug)");
        }

        // Explicit headline: the 3929 ms Curva Grande lie never reappears.
        CornerEvent curvaGrande = corners.First(c => c.CornerId == "monza_t03");
        Math.Abs(curvaGrande.DeltaMs).Should().BeLessThan(3929 - CornerDeltaToleranceMs, "«3929 ms» must be gone");

        // Sum of corner deltas is ~0 self-vs-self (never the -1381 ms lap-delta figure).
        int cornerDeltaSum = corners.Sum(c => c.DeltaMs);
        Math.Abs(cornerDeltaSum).Should().BeLessThanOrEqualTo(CornerDeltaToleranceMs, "Σ corner deltas ~0, not -1381");

        // --- M27/M1/M25 (acceptance #2): only the clean flying lap contributes; the pit out-lap and the
        // partial in-lap add zero, so S1's session-average delta is ~0, never the +14799 ms lie.
        session.CleanLapCount.Should().Be(1, "the pit out-lap and partial in-lap are not clean/coachable");
        session.SectorAvgDeltaMs.Should().HaveCount(3);
        int s1AvgDelta = session.SectorAvgDeltaMs[0];
        _out.WriteLine($"sector_avg_delta_ms=[{string.Join(",", session.SectorAvgDeltaMs)}], clean_lap_count={session.CleanLapCount}");
        Math.Abs(s1AvgDelta).Should().BeLessThan(1000, "S1 session-delta collapses to ~0 (self==ref), never ~+14799");
        s1AvgDelta.Should().NotBeInRange(14799 - 1000, 14799 + 1000, "the +14799 ms S1 loss must be gone");

        // --- M24 (acceptance #3): Parabolica absolute min speed ~127.3 km/h (oracle-grounded) and the
        // reference-relative min-speed diff collapses to ~0 (the bug gave +15.1 km/h).
        JsonElement parabolica = truthCorners.GetProperty("monza_t11");
        parabolica.GetProperty("min_speed_kmh").GetDouble().Should().BeApproximately(127.3, MinSpeedToleranceKmh);
        CornerEvent parabolicaEvent = corners.First(c => c.CornerId == "monza_t11");
        Math.Abs(parabolicaEvent.MinSpeedDiffKmh).Should().BeLessThanOrEqualTo(
            MinSpeedToleranceKmh, "min_speed_diff collapses to ~0 self-vs-self");

        // --- M16 (acceptance #3): the Parabolica brake onset is found upstream of the geometric start
        // (the widened window captures the real braking zone), not collapsed to the StartPosition fallback.
        double onset = parabolica.GetProperty("brake_onset_position").GetDouble();
        double start = parabolica.GetProperty("start_position").GetDouble();
        onset.Should().BeLessThan(start, "brake onset is upstream of the corner start (M16 widened window)");
        onset.Should().BeGreaterThan(start - (300.0 / MonzaLapLengthM) - 0.001, "onset lies within the 300 m lookback");
        Math.Abs(parabolicaEvent.BrakePointDiffM).Should().BeLessThanOrEqualTo(
            BrakeToleranceM, "brake_point_diff collapses to ~0 self-vs-self");

        // --- Render-path smoke (acceptance #4): the two lies are STRINGS; assert they never render.
        AssertRenderPathIsTruthful(corners, session, trackId, weather);
    }

    /// <summary>
    /// M3 helper-level check (acceptance #5), hermetic and always-on: confirms the <see cref="LossPlausibility"/>
    /// helper rejects the two headline lies (an over-ceiling corner delta and a deficit-busting sector delta)
    /// when called with the wired <see cref="ComputeOptions"/> defaults. This is a threshold sanity check on
    /// the pure helper, NOT a test of the ComputeSession wiring — that is covered hermetically by the M3_*
    /// facts in <c>ComputeSessionTests</c>; the per-edge behaviour is in LossPlausibilityTests.
    /// </summary>
    [Fact]
    public void Plausibility_guard_drops_the_two_lies_with_the_wired_thresholds()
    {
        var options = new ComputeOptions();

        // Corner Tier A: the -3929 ms gain rendered as a loss is rejected regardless of sign.
        LossPlausibility.WithinCeiling(-3929, options.MaxPlausibleCornerLossMs).Should().BeFalse();
        LossPlausibility.WithinCeiling(3929, options.MaxPlausibleCornerLossMs).Should().BeFalse();

        // Sector Tier B: +14799 ms cannot fit the budget of a lap that actually gained 1381 ms, and the
        // comparand is the lap deficit, never the (larger) sector absolute — the 14799 < 35994 trap.
        LossPlausibility.WithinDeficit(14799, -1381, options.LapDeficitLossRatio, options.LapDeficitFloorMs)
            .Should().BeFalse();
        (14799 < 35994).Should().BeTrue("the trap: 14799 is below the sector absolute yet still dropped");

        // Sector Tier A ceiling backstops the pit-crawl out-lap sector magnitude even without a deficit.
        LossPlausibility.WithinCeiling(66538, options.MaxPlausibleSectorLossMs).Should().BeFalse();
    }

    private static JsonElement LoadTruth(string fixtureDir)
    {
        string path = Environment.GetEnvironmentVariable("SIMCOACH_GROUNDTRUTH_TRUTH")
            ?? Path.Combine(fixtureDir, "truth.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Ground-truth oracle output not found at '{path}'. Generate it with tools/SimCoach.GroundTruthDump "
                + "then scripts/groundtruth_oracle.py — see docs/05-implementation/ground-truth-revalidation.md.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private void AssertRenderPathIsTruthful(
        IReadOnlyList<CornerEvent> corners, SessionEvent session, string trackId, string weather)
    {
        var builder = new GoldArtifactBuilder(CornerNameMap.Load(), new CoachOptions());
        var ctx = new GoldSessionContext(trackId, "gt3", weather, LapNumber: 0, HasReference: true);

        string debrief = DebriefTemplate.BuildJson(builder.BuildSession(session, ctx), new CoachOptions().MaxDebriefLosses);
        _out.WriteLine($"debrief: {debrief}");
        debrief.Should().NotContain("3929", "the Curva Grande loss must never render");
        debrief.Should().NotContain("14799", "the S1 loss must never render");

        IReadOnlyList<CoachAction> cornerActions =
            [.. ActionRegistry.Load().Actions.Where(a => a.Cadence == CoachCadence.Corner)];
        cornerActions.Should().NotBeEmpty();
        foreach (CornerEvent corner in corners)
        {
            var view = new CornerGoldView(builder.BuildCorner(corner, ctx));
            foreach (CoachAction action in cornerActions)
            {
                RenderedAction rendered = PhraseRenderer.Render(action, view, new CoachOptions());
                rendered.PhraseRu.Should().NotContain("3929", $"no corner tip may voice 3929 ({corner.CornerId})");
            }
        }
    }
}
