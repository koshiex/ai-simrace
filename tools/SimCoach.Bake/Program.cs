using System.Text.Json;
using SimCoach.Adapters.ACC;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Reference;
using SimCoach.Storage.Mcap;

// Offline bake (ADR-0014). Scans ALL recordings under the root, pools every CLEAN lap per track across
// all of them, and writes cornerGeometry.<trackId>.json + an HTML review page for each track that has
// >= MinLapsForTrust clean laps. More clean laps (even across sessions) => a more robust median
// centerline and fewer single-lap/line artifacts. Track-limits / off-track laps are excluded.
//   usage: SimCoach.Bake [recordings-root] [output-dir]
//   defaults: root = %LOCALAPPDATA%/SimCoach/recordings, output-dir = current directory.
string recordingsRoot = args.Length >= 1
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimCoach", "recordings");
string outputDir = args.Length >= 2 ? args[1] : ".";

if (!Directory.Exists(recordingsRoot))
{
    Console.Error.WriteLine($"recordings root not found: {recordingsRoot}");
    return 2;
}

Dictionary<string, List<IReadOnlyList<TelemetryFrame>>> cleanLapsByTrack = new(StringComparer.Ordinal);
Dictionary<string, int> totalLapsByTrack = new(StringComparer.Ordinal);
Dictionary<string, float> maxDistByTrack = new(StringComparer.Ordinal);

Console.WriteLine($"scanning {recordingsRoot}");
foreach (string recordingDir in Directory.GetDirectories(recordingsRoot).OrderBy(d => d, StringComparer.Ordinal))
{
    List<TelemetryFrame> frames;
    try
    {
        frames = [.. McapSegmentEnumerator.Read(recordingDir)];
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  {Path.GetFileName(recordingDir)}: skipped ({ex.GetType().Name})");
        continue;
    }

    TelemetryFrame? identified = frames.Find(frame => !string.IsNullOrWhiteSpace(frame.TrackId));
    string trackId = identified?.TrackId ?? string.Empty;
    if (frames.Count == 0 || string.IsNullOrWhiteSpace(trackId))
    {
        continue;
    }

    if (!cleanLapsByTrack.TryGetValue(trackId, out List<IReadOnlyList<TelemetryFrame>>? trackLaps))
    {
        trackLaps = [];
        cleanLapsByTrack[trackId] = trackLaps;
    }

    LapSegmenter segmenter = new();
    int clean = 0;
    int total = 0;
    foreach (TelemetryFrame frame in frames)
    {
        CompletedLap? completed = segmenter.Accept(frame);
        if (completed is null)
        {
            continue;
        }

        // "Clean" for GEOMETRY = the lap was never invalidated by track limits (is_valid_lap true on every
        // frame). NOT CompletedLap.IsClean / CleanLapPredicate — that also requires tyres_out == 0 on every
        // frame, which a normal kerb-riding racing lap never satisfies, so it would reject every lap here.
        // Kerb use is fine for geometry; only off-track-limit excursions bias the centerline.
        total++;
        if (completed.Frames.Count > 0 && completed.Frames.All(frame => frame.IsValidLap))
        {
            trackLaps.Add(completed.Frames);
            clean++;
        }
    }

    totalLapsByTrack[trackId] = totalLapsByTrack.GetValueOrDefault(trackId) + total;
    float recordingMaxDist = frames.Max(frame => frame.LapDistanceM);
    if (recordingMaxDist > maxDistByTrack.GetValueOrDefault(trackId))
    {
        maxDistByTrack[trackId] = recordingMaxDist;
    }

    Console.WriteLine($"  {Path.GetFileName(recordingDir)}: {trackId} (+{clean} clean of {total})");
}

if (cleanLapsByTrack.Count == 0)
{
    Console.Error.WriteLine("no track recordings with clean laps found");
    return 1;
}

Directory.CreateDirectory(outputDir);
int bakedTracks = 0;
foreach ((string trackId, List<IReadOnlyList<TelemetryFrame>> laps) in cleanLapsByTrack.OrderBy(kv => kv.Key, StringComparer.Ordinal))
{
    float lapLengthM = AccTrackCatalog.TryGetLapLengthM(trackId, out float catalogLength)
        ? catalogLength
        : maxDistByTrack[trackId];

    CoherenceReport coherence = CenterlineCoherence.Evaluate(trackId, lapLengthM, laps);
    Console.WriteLine(
        $"{trackId}: {coherence.LapCount} clean lap(s) of {totalLapsByTrack[trackId]} recorded, "
        + $"median dev {coherence.MedianDeviationM:0.00} m, max {coherence.MaxDeviationM:0.0} m, GO={coherence.Go}");
    foreach (string reason in coherence.Reasons)
    {
        Console.WriteLine($"  - {reason}");
    }

    if (!coherence.Go)
    {
        continue;
    }

    MedianCenterline centerline = MedianCenterlineBuilder.Build(trackId, lapLengthM, laps);
    // Per-lap centerlines feed the detector's cross-lap consensus split (a real chicane splits in most
    // laps; a single-lap line artifact does not).
    List<MedianCenterline> perLap = [.. laps.Select(lap => MedianCenterlineBuilder.Build(trackId, lapLengthM, [lap]))];
    IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline, perLap);
    var document = CornerGeometryDocument.FromDetected(trackId, lapLengthM, coherence.LapCount, corners);

    string jsonPath = Path.Combine(outputDir, $"cornerGeometry.{trackId}.json");
    JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, jsonOptions));
    File.WriteAllText(Path.ChangeExtension(jsonPath, ".html"), CornerGeometryReviewPage.Render(document, centerline));
    Console.WriteLine($"  baked {corners.Count} corner(s) -> {jsonPath} (+ review html)");
    bakedTracks++;
}

return bakedTracks > 0 ? 0 : 1;
