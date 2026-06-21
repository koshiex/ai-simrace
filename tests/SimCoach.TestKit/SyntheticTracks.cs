namespace SimCoach.TestKit;

/// <summary>
/// Ready-made <see cref="SyntheticTrack"/> instances for tests. Ships at least one dataset-covered
/// track (<see cref="Spa"/>) and one uncovered track (<see cref="TestOval"/>) so corner-model code
/// can exercise both the landmark path and the lap-derived fallback.
/// </summary>
public static class SyntheticTracks
{
    /// <summary>Spa-Francorchamps — covered by the corner-landmark dataset. 3 sectors.</summary>
    public static SyntheticTrack Spa { get; } = new()
    {
        TrackId = "spa",
        LapLengthM = 7004f,
        SectorCount = 3,
        Corners =
        [
            new SyntheticCorner { EntryPos = 0.05f, ApexPos = 0.09f, ExitPos = 0.15f, MinSpeedMps = 22f, BrakePeak = 0.9f },
            new SyntheticCorner { EntryPos = 0.40f, ApexPos = 0.44f, ExitPos = 0.50f, MinSpeedMps = 30f, BrakePeak = 0.8f },
            new SyntheticCorner { EntryPos = 0.78f, ApexPos = 0.82f, ExitPos = 0.90f, MinSpeedMps = 18f, BrakePeak = 1.0f },
        ],
    };

    /// <summary>A simple oval NOT present in any landmark dataset — forces the derive fallback. 2 sectors.</summary>
    public static SyntheticTrack TestOval { get; } = new()
    {
        TrackId = "test_oval",
        LapLengthM = 2000f,
        SectorCount = 2,
        Corners =
        [
            new SyntheticCorner { EntryPos = 0.20f, ApexPos = 0.25f, ExitPos = 0.35f, MinSpeedMps = 40f, BrakePeak = 0.6f },
            new SyntheticCorner { EntryPos = 0.70f, ApexPos = 0.75f, ExitPos = 0.85f, MinSpeedMps = 40f, BrakePeak = 0.6f },
        ],
    };
}
