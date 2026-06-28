using System.Collections.Frozen;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// Per-car steering lock (full lock-to-lock rotation, degrees) for the complete ACC 1.10 roster.
/// Shared memory does not expose the lock, so steer conversion needs this table.
/// Keys are exact <c>static.carModel</c> shared-memory ids (already lowercase snake_case).
/// Sources: Kunos shared-memory doc V1.8.12 Appendix 5, cross-checked against the tables shipped
/// by Race Element and the acc-steering-lock SimHub plugin. Where they disagree the community
/// telemetry value wins: bmw_m4_gt3 is 516 (doc says 540), honda_nsx_gt3_evo is 436 (doc's 620
/// predates the 1.9 update).
/// </summary>
public static class AccCarCatalog
{
    /// <summary>Lock used for unknown cars (matches Race Element's fallback).</summary>
    public const float FallbackSteerLockDeg = 360f;

    private static readonly FrozenDictionary<string, float> _steerLockDeg = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        // GT3
        ["amr_v12_vantage_gt3"] = 640f,
        ["amr_v8_vantage_gt3"] = 640f,
        ["audi_r8_lms"] = 720f,
        ["audi_r8_lms_evo"] = 720f,
        ["audi_r8_lms_evo_ii"] = 720f,
        ["bentley_continental_gt3_2016"] = 640f,
        ["bentley_continental_gt3_2018"] = 640f,
        ["bmw_m4_gt3"] = 516f,
        ["bmw_m6_gt3"] = 566f,
        ["ferrari_296_gt3"] = 800f,
        ["ferrari_488_gt3"] = 480f,
        ["ferrari_488_gt3_evo"] = 480f,
        ["ford_mustang_gt3"] = 516f,
        ["honda_nsx_gt3"] = 620f,
        ["honda_nsx_gt3_evo"] = 436f,
        ["jaguar_g3"] = 720f,
        ["lamborghini_gallardo_rex"] = 720f,
        ["lamborghini_huracan_gt3"] = 620f,
        ["lamborghini_huracan_gt3_evo"] = 620f,
        ["lamborghini_huracan_gt3_evo2"] = 620f,
        ["lexus_rc_f_gt3"] = 640f,
        ["mclaren_650s_gt3"] = 480f,
        ["mclaren_720s_gt3"] = 480f,
        ["mclaren_720s_gt3_evo"] = 480f,
        ["mercedes_amg_gt3"] = 640f,
        ["mercedes_amg_gt3_evo"] = 640f,
        ["nissan_gt_r_gt3_2017"] = 640f,
        ["nissan_gt_r_gt3_2018"] = 640f,
        ["porsche_991_gt3_r"] = 800f,
        ["porsche_991ii_gt3_r"] = 800f,
        ["porsche_992_gt3_r"] = 800f,

        // GT4
        ["alpine_a110_gt4"] = 720f,
        ["amr_v8_vantage_gt4"] = 640f,
        ["audi_r8_gt4"] = 720f,
        ["bmw_m4_gt4"] = 492f,
        ["chevrolet_camaro_gt4r"] = 720f,
        ["ginetta_g55_gt4"] = 720f,
        ["ktm_xbow_gt4"] = 582f,
        ["maserati_mc_gt4"] = 900f,
        ["mclaren_570s_gt4"] = 480f,
        ["mercedes_amg_gt4"] = 492f,
        ["porsche_718_cayman_gt4_mr"] = 800f,

        // GT2
        ["audi_r8_lms_gt2"] = 720f,
        ["ktm_xbow_gt2"] = 582f,
        ["maserati_mc20_gt2"] = 480f,
        ["mercedes_amg_gt2"] = 492f,
        ["porsche_935"] = 720f,
        ["porsche_991_gt2_rs_mr"] = 720f,

        // Cup / single-make / TCX
        ["porsche_991ii_gt3_cup"] = 800f,
        ["porsche_992_gt3_cup"] = 540f,
        ["lamborghini_huracan_st"] = 620f,
        ["lamborghini_huracan_st_evo2"] = 620f,
        ["ferrari_488_challenge_evo"] = 480f,
        ["bmw_m2_cs_racing"] = 360f,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static int KnownCarCount => _steerLockDeg.Count;

    /// <summary>Full lock-to-lock rotation in degrees; fallback for cars outside the catalog.</summary>
    public static float GetSteerLockDeg(string carId) =>
        _steerLockDeg.TryGetValue(carId, out float lockDeg) ? lockDeg : FallbackSteerLockDeg;

    /// <summary>
    /// Coarse ACC competition class per <c>static.carModel</c> id. An explicit map (not a suffix parse)
    /// because the ids are irregular: <c>porsche_991ii_gt3_cup</c> contains "gt3" yet is a Cup car,
    /// <c>chevrolet_camaro_gt4r</c> drops the underscore, <c>jaguar_g3</c>/<c>porsche_935</c> encode no class.
    /// Classes: <c>gt3</c>, <c>gt4</c>, <c>gt2</c>, <c>gtc</c> (Challenger/Cup single-makes), <c>tcx</c>.
    /// </summary>
    private static readonly FrozenDictionary<string, string> _carClass = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // GT3
        ["amr_v12_vantage_gt3"] = "gt3",
        ["amr_v8_vantage_gt3"] = "gt3",
        ["audi_r8_lms"] = "gt3",
        ["audi_r8_lms_evo"] = "gt3",
        ["audi_r8_lms_evo_ii"] = "gt3",
        ["bentley_continental_gt3_2016"] = "gt3",
        ["bentley_continental_gt3_2018"] = "gt3",
        ["bmw_m4_gt3"] = "gt3",
        ["bmw_m6_gt3"] = "gt3",
        ["ferrari_296_gt3"] = "gt3",
        ["ferrari_488_gt3"] = "gt3",
        ["ferrari_488_gt3_evo"] = "gt3",
        ["ford_mustang_gt3"] = "gt3",
        ["honda_nsx_gt3"] = "gt3",
        ["honda_nsx_gt3_evo"] = "gt3",
        ["jaguar_g3"] = "gt3",
        ["lamborghini_gallardo_rex"] = "gt3",
        ["lamborghini_huracan_gt3"] = "gt3",
        ["lamborghini_huracan_gt3_evo"] = "gt3",
        ["lamborghini_huracan_gt3_evo2"] = "gt3",
        ["lexus_rc_f_gt3"] = "gt3",
        ["mclaren_650s_gt3"] = "gt3",
        ["mclaren_720s_gt3"] = "gt3",
        ["mclaren_720s_gt3_evo"] = "gt3",
        ["mercedes_amg_gt3"] = "gt3",
        ["mercedes_amg_gt3_evo"] = "gt3",
        ["nissan_gt_r_gt3_2017"] = "gt3",
        ["nissan_gt_r_gt3_2018"] = "gt3",
        ["porsche_991_gt3_r"] = "gt3",
        ["porsche_991ii_gt3_r"] = "gt3",
        ["porsche_992_gt3_r"] = "gt3",

        // GT4
        ["alpine_a110_gt4"] = "gt4",
        ["amr_v8_vantage_gt4"] = "gt4",
        ["audi_r8_gt4"] = "gt4",
        ["bmw_m4_gt4"] = "gt4",
        ["chevrolet_camaro_gt4r"] = "gt4",
        ["ginetta_g55_gt4"] = "gt4",
        ["ktm_xbow_gt4"] = "gt4",
        ["maserati_mc_gt4"] = "gt4",
        ["mclaren_570s_gt4"] = "gt4",
        ["mercedes_amg_gt4"] = "gt4",
        ["porsche_718_cayman_gt4_mr"] = "gt4",

        // GT2
        ["audi_r8_lms_gt2"] = "gt2",
        ["ktm_xbow_gt2"] = "gt2",
        ["maserati_mc20_gt2"] = "gt2",
        ["mercedes_amg_gt2"] = "gt2",
        ["porsche_935"] = "gt2",
        ["porsche_991_gt2_rs_mr"] = "gt2",

        // GTC (Challenger / Cup single-makes)
        ["porsche_991ii_gt3_cup"] = "gtc",
        ["porsche_992_gt3_cup"] = "gtc",
        ["lamborghini_huracan_st"] = "gtc",
        ["lamborghini_huracan_st_evo2"] = "gtc",
        ["ferrari_488_challenge_evo"] = "gtc",

        // TCX
        ["bmw_m2_cs_racing"] = "tcx",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The coarse competition class for a car id, or <c>false</c> for cars outside the catalog.</summary>
    public static bool TryGetCarClass(string carId, out string carClass) =>
        _carClass.TryGetValue(carId, out carClass!);
}
