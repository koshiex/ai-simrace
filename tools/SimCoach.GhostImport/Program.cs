using System.Globalization;
using SimCoach.GhostImport;
using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;

// SimCoach.GhostImport — offline, ACC-specific .ghost -> beyond-PB alien LINE importer (dev-time tool).
// ACC ghost decode lives ONLY here, never the sim-agnostic runtime.
//
//   usage:
//     SimCoach.GhostImport decode <path-to.ghost>
//     SimCoach.GhostImport import --track <t> --car <c> --weather <w> --lap-length <m> [--data-root <path>]
//
// `decode` is a local-file smoke command (container/zlib decode + record parse + guards). `import` wires the
// full dev-time pipeline: accreplay fetch (fastest GT3 lap, OD2) -> decode -> loop-closure split -> centerline
// align -> per-metre resample -> seam mask -> persist an alien_line row + LINE parquet under the OWNER triple.
// The accreplay fetch runs live and is NEVER exercised by a test; the raw .ghost is transient (never committed)
// and only the derived alien LINE is written (locally here, vendored-embedded separately — OD1/OD10).
if (args.Length >= 1 && string.Equals(args[0], "decode", StringComparison.Ordinal))
{
    return RunDecode(args);
}

if (args.Length >= 1 && string.Equals(args[0], "import", StringComparison.Ordinal))
{
    return await RunImportAsync(args).ConfigureAwait(false);
}

Console.Error.WriteLine(
    "usage:\n"
    + "  SimCoach.GhostImport decode <path-to.ghost>\n"
    + "  SimCoach.GhostImport import --track <t> --car <c> --weather <w> --lap-length <m> [--data-root <path>]");
return 2;

static int RunDecode(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: SimCoach.GhostImport decode <path-to.ghost>");
        return 2;
    }

    string ghostPath = args[1];
    if (!File.Exists(ghostPath))
    {
        Console.Error.WriteLine($"ghost file not found: {ghostPath}");
        return 2;
    }

    try
    {
        byte[] file = File.ReadAllBytes(ghostPath);
        byte[] payload = GhostContainer.Inflate(file);
        GhostPayloadHeader header = GhostPayload.ReadHeader(payload);
        ImportGuards.CheckArithmetic(header);
        IReadOnlyList<GhostRecord> records = GhostPayload.ReadRecords(payload, header);

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        foreach (GhostRecord record in records)
        {
            minX = Math.Min(minX, record.WorldX);
            maxX = Math.Max(maxX, record.WorldX);
            minZ = Math.Min(minZ, record.WorldZ);
            maxZ = Math.Max(maxZ, record.WorldZ);
        }

        Console.WriteLine($"track     : {header.TrackId}");
        Console.WriteLine($"payload   : {payload.Length} bytes");
        Console.WriteLine($"records   : {records.Count}");
        Console.WriteLine($"world X   : [{minX:0.0}, {maxX:0.0}]");
        Console.WriteLine($"world Z   : [{minZ:0.0}, {maxZ:0.0}]");
        return 0;
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine($"decode failed: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunImportAsync(string[] args)
{
    Dictionary<string, string> options = ParseOptions(args);
    if (!options.TryGetValue("track", out string? track)
        || !options.TryGetValue("car", out string? car)
        || !options.TryGetValue("weather", out string? weather)
        || !options.TryGetValue("lap-length", out string? lapLengthText)
        || !float.TryParse(lapLengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out float lapLengthM))
    {
        Console.Error.WriteLine(
            "usage: SimCoach.GhostImport import --track <t> --car <c> --weather <w> --lap-length <m> "
            + "[--data-root <path>]");
        return 2;
    }

    var importOptions = new GhostImportOptions();
    if (!CenterlineGeometryDataset.Load().TryGetCenterline(track, lapLengthM, out MedianCenterline? centerline)
        || centerline is null)
    {
        Console.Error.WriteLine(
            $"no vendored centerline for '{track}' at {lapLengthM:0.0} m — cannot align the ghost");
        return 1;
    }

    try
    {
        int accTrackId = AccReplayClient.TrackIdFor(track);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        IReadOnlyList<AccReplayLap> board = await AccReplayClient
            .FetchGt3LeaderboardAsync(http, accTrackId, CancellationToken.None)
            .ConfigureAwait(false);

        // Walk the board fastest-first to the first lap whose ghost yields a complete, centerline-tracking
        // loop. The outright-fastest entry is frequently a reconnaissance drive that never closes the loop.
        const int maxCandidates = 20;
        int considered = 0;
        foreach (AccReplayLap lap in board)
        {
            if (considered >= maxCandidates)
            {
                Console.WriteLine($"reached the {maxCandidates}-lap scan cap for '{track}' without a usable lap");
                break;
            }

            considered++;

            try
            {
                byte[] ghost = await AccReplayClient
                    .DownloadGhostAsync(http, lap.LapId, track, CancellationToken.None)
                    .ConfigureAwait(false);
                byte[] payload = GhostContainer.Inflate(ghost);
                GhostPayloadHeader header = GhostPayload.ReadHeader(payload);
                ImportGuards.CheckArithmetic(header);
                IReadOnlyList<GhostRecord> records = GhostPayload.ReadRecords(payload, header);

                IReadOnlyList<IReadOnlyList<GhostRecord>> laps = LapSplitter.Split(records, importOptions);
                if (laps.Count == 0)
                {
                    Console.WriteLine(
                        $"skip lap {lap.LapId} ({lap.Car}, {lap.LapTimeMs} ms): no complete loop");
                    continue;
                }

                float medianDeviationM = CenterlineAligner.MedianDeviationM(laps[0], centerline);
                Console.WriteLine(
                    $"lap {lap.LapId} ({lap.Car}, {lap.LapTimeMs} ms): align median {medianDeviationM:0.00} m "
                    + $"(ceiling {importOptions.AlignmentDeviationCeilingM:0.00} m)");
                IReadOnlyList<AlignedPoint> aligned =
                    CenterlineAligner.Align(laps[0], centerline, importOptions);
                ResampledLap grid =
                    LineResampler.Resample(aligned, centerline.LapLengthM, lapNumber: 1, importOptions);
                ResampledLap masked = SeamMask.Apply(grid, importOptions.SeamBands);

                var triple = new ReferenceTriple(track, car, weather);
                var provenance = GhostProvenance.FromAccReplay(lap, track);

                options.TryGetValue("data-root", out string? dataRootArg);
                string dataRoot = DataRootResolver.Resolve(dataRootArg);
                var factory = new SqliteConnectionFactory(
                    new DatabaseOptions { DbPath = DataRootResolver.DatabasePath(dataRoot) });
                new DatabaseMigrator(factory).Migrate();

                string parquetPath = AlienReferenceWriter.Persist(
                    new ReferenceRepository(factory),
                    DataRootResolver.ReferencesDirectory(dataRoot),
                    triple,
                    masked,
                    lap.LapTimeMs,
                    provenance,
                    DateTimeOffset.UtcNow);

                Console.WriteLine(
                    $"alien_line persisted for {triple.TrackId}/{triple.CarId}/{triple.WeatherBucket} "
                    + $"(source {lap.Car}, {lap.LapTimeMs} ms, median {medianDeviationM:0.00} m) -> {parquetPath}");
                return 0;
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"skip lap {lap.LapId} ({lap.Car}): {ex.Message}");
            }
        }

        Console.Error.WriteLine(
            $"no usable lap in the top {considered} GT3 laps for '{track}' (all partial or off-centerline)");
        return 1;
    }
    catch (Exception ex)
    {
        // Dev-tool top level: any failure (decode, network timeout, JSON shape drift, IO) becomes a clean
        // non-zero exit with a one-line message instead of a raw stack trace.
        Console.Error.WriteLine($"import failed: {ex.Message}");
        return 1;
    }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 1; i + 1 < args.Length; i += 2)
    {
        string key = args[i];
        if (key.StartsWith("--", StringComparison.Ordinal))
        {
            map[key[2..]] = args[i + 1];
        }
    }

    return map;
}
