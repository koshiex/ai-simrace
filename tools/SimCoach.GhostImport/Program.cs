using SimCoach.GhostImport;

// SimCoach.GhostImport — offline, ACC-specific .ghost -> beyond-PB alien LINE importer (dev-time tool).
// ACC ghost decode lives ONLY here, never the sim-agnostic runtime.
//
// Commit-19 scope: container/zlib decode + 130-byte record parse + fail-fast import guards, exposed as a
// local-file `decode` smoke command. The accreplay fetch is written (AccReplayClient) but is NOT wired
// end-to-end here and is never exercised by a test; loop-split/align/resample and persistence land in the
// following commits.
//
//   usage:
//     SimCoach.GhostImport decode <path-to.ghost>
if (args.Length < 2 || !string.Equals(args[0], "decode", StringComparison.Ordinal))
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
