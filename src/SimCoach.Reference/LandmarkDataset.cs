using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimCoach.Reference;

/// <summary>
/// The vendored corner-landmark dataset (CrewChiefV4, MIT — see <c>Data/LICENSE-CrewChief</c>).
/// Reads landmark distances (metres round the lap) for ACC-covered tracks and converts them to
/// normalized-position corner windows. Only the ACC-specific <c>accTrackName</c> field is consumed;
/// its <c>"&lt;Name&gt;:track config"</c> value normalizes to our <c>track_id</c> by taking the part
/// before the colon, lower-cased (extends <c>AccTrackCatalog</c>'s normalization). The dataset path
/// resolves at session start; tracks it does not cover fall back to the lap-derived model (ADR-0010).
/// </summary>
public sealed class LandmarkDataset
{
    private const string ResourceName = "SimCoach.Reference.Data.trackLandmarksData.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Normalized track_id -> landmarks, in dataset order.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RawLandmark>> _byTrackId;

    private LandmarkDataset(IReadOnlyDictionary<string, IReadOnlyList<RawLandmark>> byTrackId) =>
        _byTrackId = byTrackId;

    /// <summary>Loads the dataset from the embedded resource. Deterministic and side-effect-free.</summary>
    public static LandmarkDataset Load()
    {
        using Stream stream = typeof(LandmarkDataset).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        return LoadFrom(stream);
    }

    /// <summary>Loads the dataset from an arbitrary JSON stream (test seam).</summary>
    public static LandmarkDataset LoadFrom(Stream jsonStream)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);
        RawRoot? root = JsonSerializer.Deserialize<RawRoot>(jsonStream, _jsonOptions);
        Dictionary<string, IReadOnlyList<RawLandmark>> byTrackId = new(StringComparer.Ordinal);

        foreach (RawEntry entry in root?.TrackLandmarksData ?? [])
        {
            string? trackId = NormalizeAccTrackName(entry.AccTrackName);
            if (trackId is null || entry.TrackLandmarks is not { Count: > 0 })
            {
                continue;
            }

            // First entry wins for a given track id (the dataset has no ACC duplicates today).
            byTrackId.TryAdd(trackId, entry.TrackLandmarks);
        }

        return new LandmarkDataset(byTrackId);
    }

    /// <summary>The normalized track ids the dataset covers for ACC.</summary>
    public IReadOnlyCollection<string> CoveredTrackIds => (IReadOnlyCollection<string>)_byTrackId.Keys;

    /// <summary>
    /// Resolves dataset corners for <paramref name="trackId"/> into normalized-position windows.
    /// Returns <c>false</c> (and empty corners) when the track is not covered OR when any landmark
    /// range is insane (<c>0 ≤ start &lt; end ≤ lapLengthM</c> violated) — the whole track then drops
    /// to the derive fallback rather than emitting a misplaced corner (risk register, ADR-0010).
    /// </summary>
    public bool TryGetCorners(string trackId, float lapLengthM, out IReadOnlyList<Corner> corners)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);
        corners = [];

        if (!_byTrackId.TryGetValue(trackId, out IReadOnlyList<RawLandmark>? landmarks))
        {
            return false;
        }

        List<Corner> resolved = new(landmarks.Count);
        foreach (RawLandmark landmark in landmarks)
        {
            float startM = landmark.DistanceRoundLapStart;
            float endM = landmark.DistanceRoundLapEnd;
            if (!(startM >= 0f && startM < endM && endM <= lapLengthM))
            {
                // One bad range disqualifies the whole track — fall back to derive.
                corners = [];
                return false;
            }

            string name = landmark.LandmarkName ?? string.Empty;
            resolved.Add(new Corner
            {
                Id = $"{trackId}_{name}",
                Name = name.Length > 0 ? name : null,
                StartPosition = startM / lapLengthM,
                ApexPosition = (startM + endM) / 2f / lapLengthM,
                EndPosition = endM / lapLengthM,
            });
        }

        resolved.Sort(static (a, b) => a.StartPosition.CompareTo(b.StartPosition));
        corners = resolved;
        return true;
    }

    /// <summary>
    /// <c>"Spa:track config"</c> → <c>"spa"</c>; <c>"brands_hatch:track config"</c> →
    /// <c>"brands_hatch"</c>. <c>null</c>/blank → not an ACC track.
    /// </summary>
    private static string? NormalizeAccTrackName(string? accTrackName)
    {
        if (string.IsNullOrWhiteSpace(accTrackName))
        {
            return null;
        }

        int colon = accTrackName.IndexOf(':', StringComparison.Ordinal);
        string name = colon >= 0 ? accTrackName[..colon] : accTrackName;
        name = name.Trim();
        return name.Length > 0 ? name.ToLowerInvariant() : null;
    }

    private sealed record RawRoot
    {
        [JsonPropertyName("TrackLandmarksData")]
        public IReadOnlyList<RawEntry>? TrackLandmarksData { get; init; }
    }

    private sealed record RawEntry
    {
        public string? AccTrackName { get; init; }
        public IReadOnlyList<RawLandmark>? TrackLandmarks { get; init; }
    }

    private sealed record RawLandmark
    {
        public string? LandmarkName { get; init; }
        public float DistanceRoundLapStart { get; init; }
        public float DistanceRoundLapEnd { get; init; }
    }
}
