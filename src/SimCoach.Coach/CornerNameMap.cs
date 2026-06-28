using System.Reflection;
using System.Text.Json;

namespace SimCoach.Coach;

/// <summary>
/// First-party corner names for the Phase-3 prompt layer (ADR-0010/0014). A vendored, authored map of
/// <c>corner_id → {name, short}</c>, kept OUT of compute — geometry ships nameless positional ids and the
/// prompt builder resolves names here. Corner names are public facts about the real circuits, authored
/// against our own baked ids; they are NOT sourced from any third-party dataset. An unknown id falls back to
/// positional phrasing ("поворот N"). Names are re-authored when a track is (re)baked, since they key on the
/// baked corner ids.
/// </summary>
public sealed class CornerNameMap
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, Dictionary<string, CornerNameEntry>> _byTrack;

    private CornerNameMap(Dictionary<string, Dictionary<string, CornerNameEntry>> byTrack) => _byTrack = byTrack;

    /// <summary>Loads the names embedded in this assembly.</summary>
    public static CornerNameMap Load()
    {
        Assembly assembly = typeof(CornerNameMap).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("SimCoach.Coach.Data.cornerNames.json");
        if (stream is null)
        {
            return new CornerNameMap(new Dictionary<string, Dictionary<string, CornerNameEntry>>(StringComparer.Ordinal));
        }

        Dictionary<string, Dictionary<string, CornerNameEntry>> byTrack =
            JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, CornerNameEntry>>>(stream, _jsonOptions)
            ?? new Dictionary<string, Dictionary<string, CornerNameEntry>>(StringComparer.Ordinal);
        return new CornerNameMap(byTrack);
    }

    /// <summary>
    /// Resolves the human name for a baked corner id. Returns false (and an empty name) for an unknown track
    /// or corner so the caller can fall back to positional phrasing.
    /// </summary>
    public bool TryGetName(string trackId, string cornerId, out string name)
    {
        name = TryGetEntry(trackId, cornerId)?.Name ?? string.Empty;
        return name.Length > 0;
    }

    /// <summary>The full name, or the positional fallback ("поворот N") for an unknown or empty corner id.</summary>
    public string ResolveName(string trackId, string cornerId)
    {
        // A degenerate empty/whitespace id (e.g. a proto-default "") falls back to positional phrasing rather
        // than tripping TryGetEntry's argument guard — names are a best-effort display layer, never fatal.
        if (string.IsNullOrWhiteSpace(cornerId))
        {
            return CornerNameForms.Positional(cornerId);
        }

        return TryGetName(trackId, cornerId, out string name) ? name : CornerNameForms.Positional(cornerId);
    }

    /// <summary>The authored slim display form, falling back to the full/positional name when none is authored.</summary>
    public string GetShort(string trackId, string cornerId)
    {
        CornerNameEntry? entry = TryGetEntry(trackId, cornerId);
        if (entry is not null && !string.IsNullOrWhiteSpace(entry.Short))
        {
            return entry.Short;
        }

        return ResolveName(trackId, cornerId);
    }

    /// <summary>The spoken RU form (trailing <c>(N)</c> expanded to an ordinal) for the voice path.</summary>
    public string GetSpokenRu(string trackId, string cornerId) =>
        CornerNameForms.Spoken(ResolveName(trackId, cornerId));

    private CornerNameEntry? TryGetEntry(string trackId, string cornerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cornerId);

        if (_byTrack.TryGetValue(trackId, out Dictionary<string, CornerNameEntry>? corners)
            && corners.TryGetValue(cornerId, out CornerNameEntry? entry))
        {
            return entry;
        }

        return null;
    }
}
