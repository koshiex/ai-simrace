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
}
