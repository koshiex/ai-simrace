namespace SimCoach.GhostImport;

/// <summary>
/// A single accreplay leaderboard entry, reduced to the fields the importer keeps. The driver name is
/// deliberately NOT a field: it is dropped at parse and never stored anywhere (OD1). Provenance records
/// only the source car + laptime.
/// </summary>
internal readonly record struct AccReplayLap(long LapId, string Car, int LapTimeMs);
