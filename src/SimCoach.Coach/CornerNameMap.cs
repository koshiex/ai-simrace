using System.Reflection;
using System.Text.Json;

namespace SimCoach.Coach;

/// <summary>
/// First-party corner names for the Phase-3 prompt layer (ADR-0010/0014). A vendored, authored map of
/// <c>corner_id → human name</c>, kept OUT of compute — geometry ships nameless positional ids and the
/// prompt builder resolves names here. Corner names are public facts about the real circuits, authored
/// against our own baked ids; they are NOT sourced from any third-party dataset. An unknown id returns
/// false so the prompt falls back to positional phrasing ("turn N"). Names are re-authored when a track
/// is (re)baked, since they key on the baked corner ids.
/// </summary>
public sealed class CornerNameMap
{
    private readonly Dictionary<string, Dictionary<string, string>> _byTrack;

    private CornerNameMap(Dictionary<string, Dictionary<string, string>> byTrack) => _byTrack = byTrack;

    /// <summary>Loads the names embedded in this assembly.</summary>
    public static CornerNameMap Load()
    {
        Assembly assembly = typeof(CornerNameMap).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("SimCoach.Coach.Data.cornerNames.json");
        if (stream is null)
        {
            return new CornerNameMap(new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
        }

        Dictionary<string, Dictionary<string, string>> byTrack =
            JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        return new CornerNameMap(byTrack);
    }

    /// <summary>
    /// Resolves the human name for a baked corner id. Returns false (and an empty name) for an unknown
    /// track or corner so the caller can fall back to positional phrasing.
    /// </summary>
    public bool TryGetName(string trackId, string cornerId, out string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cornerId);

        name = string.Empty;
        if (_byTrack.TryGetValue(trackId, out Dictionary<string, string>? corners)
            && corners.TryGetValue(cornerId, out string? found))
        {
            name = found;
            return true;
        }

        return false;
    }
}
