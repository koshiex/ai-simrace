using System.Text.Json;
using SimCoach.Adapters.ACC;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Reference;
using SimCoach.Storage.Mcap;

// Offline bake (ADR-0014): read a recording's MCAP, aggregate a median centerline, detect corners,
// and — only if the offline coherence gate passes — write cornerGeometry.json + an HTML review page.
// Telemetry recordings are never committed; this runs locally against %LOCALAPPDATA%/SimCoach/recordings.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: SimCoach.Bake <recording-dir> [output cornerGeometry.json]");
    return 2;
}

string recordingDir = args[0];
string? explicitOutput = args.Length >= 2 ? args[1] : null;

List<TelemetryFrame> frames = [.. McapSegmentEnumerator.Read(recordingDir)];
if (frames.Count == 0)
{
    Console.Error.WriteLine($"no telemetry frames under {recordingDir}");
    return 1;
}

TelemetryFrame? identified = frames.Find(frame => !string.IsNullOrWhiteSpace(frame.TrackId));
string trackId = identified?.TrackId ?? string.Empty;
if (string.IsNullOrWhiteSpace(trackId))
{
    Console.Error.WriteLine("no track id in telemetry");
    return 1;
}

// Per-track file by default so a bake never overwrites another track's geometry.
string outputPath = explicitOutput ?? $"cornerGeometry.{trackId}.json";

float lapLengthM = AccTrackCatalog.TryGetLapLengthM(trackId, out float catalogLength)
    ? catalogLength
    : frames.Max(frame => frame.LapDistanceM);

LapSegmenter segmenter = new();
List<IReadOnlyList<TelemetryFrame>> laps = [];
foreach (TelemetryFrame frame in frames)
{
    if (segmenter.Accept(frame) is { } lap)
    {
        laps.Add(lap.Frames);
    }
}

CoherenceReport coherence = CenterlineCoherence.Evaluate(trackId, lapLengthM, laps);
Console.WriteLine($"{trackId}: {coherence.LapCount} lap(s), median dev {coherence.MedianDeviationM:0.00} m, max {coherence.MaxDeviationM:0.0} m, GO={coherence.Go}");
foreach (string reason in coherence.Reasons)
{
    Console.WriteLine($"  - {reason}");
}

if (!coherence.Go)
{
    Console.Error.WriteLine("coherence NO-GO; refusing to bake");
    return 1;
}

MedianCenterline centerline = MedianCenterlineBuilder.Build(trackId, lapLengthM, laps);
IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline);
string sourceRecording = Path.GetFileName(Path.TrimEndingDirectorySeparator(recordingDir));
var document = CornerGeometryDocument.FromDetected(trackId, lapLengthM, coherence.LapCount, corners, sourceRecording);

JsonSerializerOptions options = new() { WriteIndented = true };
File.WriteAllText(outputPath, JsonSerializer.Serialize(document, options));
Console.WriteLine($"baked {corners.Count} corner(s) -> {outputPath}");

string reviewPath = Path.ChangeExtension(outputPath, ".html");
File.WriteAllText(reviewPath, CornerGeometryReviewPage.Render(document, centerline));
Console.WriteLine($"review page -> {reviewPath}");

return 0;
