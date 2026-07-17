namespace SimCoach.GhostImport;

/// <summary>
/// Fail-fast import guards (<c>docs/05-implementation/acc-ghost-format-re.md</c>, OD5). The synthetic
/// decode fixture only proves decoder↔encoder self-consistency, so these guards are the real backstop
/// against a misread field order or a foreign file: a bad decode must fail loudly here, not silently
/// mislead. Every guard throws <see cref="InvalidDataException"/> so the caller can fault-isolate it.
/// </summary>
internal static class ImportGuards
{
    /// <summary>
    /// Exact record-arithmetic check: <c>RecordStart + RecordCount*130 + 11 == PayloadLength</c>. A stride
    /// or offset misread breaks this by many bytes.
    /// </summary>
    internal static void CheckArithmetic(GhostPayloadHeader header)
    {
        long expected = (long)header.RecordStart
            + ((long)header.RecordCount * GhostPayload.RecordStride)
            + GhostPayload.TrailerLength;
        if (expected != header.PayloadLength)
        {
            throw new InvalidDataException(
                $"ghost payload arithmetic mismatch: recStart {header.RecordStart} + "
                + $"{header.RecordCount}*{GhostPayload.RecordStride} + {GhostPayload.TrailerLength} = {expected}, "
                + $"but payload length is {header.PayloadLength}");
        }
    }

    /// <summary>
    /// World-XZ bbox check: every decoded record must land inside the track box. Catches a wrong-stride
    /// decode (coordinates off by hundreds of metres) or a foreign track's ghost.
    /// </summary>
    internal static void CheckWorldBbox(IReadOnlyList<GhostRecord> records, GhostBbox bbox)
    {
        for (int i = 0; i < records.Count; i++)
        {
            GhostRecord record = records[i];
            if (!bbox.Contains(record.WorldX, record.WorldZ))
            {
                throw new InvalidDataException(
                    $"ghost record {i} world XZ ({record.WorldX:0.0}, {record.WorldZ:0.0}) falls outside the "
                    + $"track box X[{bbox.MinX:0.0}, {bbox.MaxX:0.0}] Z[{bbox.MinZ:0.0}, {bbox.MaxZ:0.0}]");
            }
        }
    }
}
