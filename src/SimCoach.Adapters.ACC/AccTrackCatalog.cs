using System.Collections.Frozen;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// Per-track lap length in meters for the complete ACC 1.10 track list. ACC does not populate
/// <c>static.trackSPlineLength</c>, so lap distance derivation needs this table.
/// Keys are NORMALIZED track ids (lowercase): shared memory reports mixed case ("Spa",
/// "Paul_Ricard", "brands_hatch"), while server configs and results JSON are all-lowercase —
/// normalized keys serve both namespaces. Lengths are the values community telemetry tools
/// (Race Element) and the in-game UI use; the internal spline may deviate by &lt;1%.
/// </summary>
public static class AccTrackCatalog
{
    private static readonly FrozenDictionary<string, float> _lapLengthM = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["barcelona"] = 4655f,
        ["brands_hatch"] = 3908f,
        ["cota"] = 5513f,
        ["donington"] = 4020f,
        ["hungaroring"] = 4381f,
        ["imola"] = 4959f,
        ["indianapolis"] = 4167f,
        ["kyalami"] = 4522f,
        ["laguna_seca"] = 3602f,
        ["misano"] = 4226f,
        ["monza"] = 5793f,
        ["mount_panorama"] = 6213f,
        ["nurburgring"] = 5137f,
        ["nurburgring_24h"] = 25378f,
        ["oulton_park"] = 4307f,
        ["paul_ricard"] = 5770f,
        ["red_bull_ring"] = 4318f,
        ["silverstone"] = 5891f,
        ["snetterton"] = 4779f,
        ["spa"] = 7004f,
        ["suzuka"] = 5807f,
        ["valencia"] = 4005f,
        ["watkins_glen"] = 5552f,
        ["zandvoort"] = 4252f,
        ["zolder"] = 4011f,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static int KnownTrackCount => _lapLengthM.Count;

    /// <summary>Lap length for a NORMALIZED (lowercase) track id; false for unknown tracks.</summary>
    public static bool TryGetLapLengthM(string trackId, out float lengthM) =>
        _lapLengthM.TryGetValue(trackId, out lengthM);
}
