namespace SimCoach.GhostImport;

/// <summary>
/// The parsed fixed header of a decompressed ghost payload: the track id string, the declared
/// <see cref="RecordCount"/>, and <see cref="RecordStart"/> — the byte offset where the fixed-stride
/// 130-byte records begin. <see cref="PayloadLength"/> is carried through so the arithmetic guard can
/// verify <c>RecordStart + RecordCount*130 + 11 == PayloadLength</c> without re-reading the buffer.
/// </summary>
internal readonly record struct GhostPayloadHeader(
    string TrackId,
    int RecordCount,
    int RecordStart,
    int PayloadLength);
