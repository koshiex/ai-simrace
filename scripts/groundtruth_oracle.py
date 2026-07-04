#!/usr/bin/env python3
"""Independent ground-truth oracle for the detection-truthfulness exit gate (Phase-3, TASK 7).

Reads the per-frame CSV produced by tools/SimCoach.GroundTruthDump and computes corner/sector truth
straight from the raw telemetry, with NO dependency on the C# pipeline code. The revalidation xUnit
(tests/SimCoach.Reference.Tests/GroundTruthRevalidationTests.cs) asserts the pipeline's emitted proto
numbers against this file.

Independence is the whole point (plan Q-EG3): the Monza corner [Start, End] landmarks are inlined here
as constants rather than imported from the vendored geometry, so a geometry error in the pipeline
cannot hide behind a matching error in the oracle.

  usage: python3 groundtruth_oracle.py <frames-csv> <truth-json>
"""
import json
import sys

import numpy as np
import pandas as pd

LAP_LENGTH_M = 5793.0
# Upstream brake-window lookback (ComputeOptions.BrakeWindowUpstreamM default), as a normalized fraction.
BRAKE_UPSTREAM_M = 300.0
BRAKE_ONSET_THRESHOLD = 0.15

# Inlined Monza landmark [Start, End] normalized positions (independent of the vendored geometry, Q-EG3).
# monza_t03 = Curva Grande (the 3929 ms headline); monza_t11 = Curva Parabolica.
CORNERS = {
    "monza_t01": (0.15570517, 0.16485414),
    "monza_t02": (0.16502675, 0.17383048),
    "monza_t03": (0.22906956, 0.27015364),
    "monza_t06": (0.42810288, 0.45572242),
    "monza_t09": (0.69100636, 0.70930433),
    "monza_t11": (0.88278955, 0.92249270),
}


def segment_passes(dd):
    """Split into lap-crossings by the normalized-position wrap (0.9x -> 0.0x), never by ACC lap_number
    (garbage in this fixture: nearly every frame reports lap 1)."""
    pos = dd["normalized_car_position"].to_numpy()
    wraps = list(np.where((pos[:-1] > 0.9) & (pos[1:] < 0.1))[0])
    bounds = [0] + [w + 1 for w in wraps] + [len(dd)]
    return [dd.iloc[bounds[i]:bounds[i + 1]].reset_index(drop=True) for i in range(len(bounds) - 1)]


def time_at_position_ms(seg, start, end):
    """Self time over [start, end] by linear interpolation of t_ms at the exact positions. This is the
    absolute span time the span-mismatch bug leaked into delta_ms as ~3929 ms for Curva Grande."""
    p = seg["normalized_car_position"].to_numpy()
    t = seg["t_ms"].to_numpy()

    def interp(x):
        i = int(np.searchsorted(p, x))
        if i <= 0 or i >= len(p):
            return None
        return t[i - 1] + (t[i] - t[i - 1]) * (x - p[i - 1]) / (p[i] - p[i - 1])

    a, b = interp(start), interp(end)
    return None if a is None or b is None else float(b - a)


def min_speed_kmh(seg, start, end):
    s = seg[(seg["normalized_car_position"] >= start) & (seg["normalized_car_position"] <= end)]
    return float(s["speed_kmh"].min()) if len(s) else None


def brake_onset_position(seg, start, end):
    """First position with brake >= threshold in the upstream-widened window [start - upstream, end].
    Non-null and < start proves the real braking zone starts before the geometric corner (M16)."""
    upstream = BRAKE_UPSTREAM_M / LAP_LENGTH_M
    w = seg[(seg["normalized_car_position"] >= start - upstream) & (seg["normalized_car_position"] <= end)]
    braking = w[w["brake"] >= BRAKE_ONSET_THRESHOLD]
    return float(braking["normalized_car_position"].iloc[0]) if len(braking) else None


def sector_times_ms(seg):
    """Sector times from the sim's current_sector_index transitions (0 -> 1 -> 2), which is robust to the
    sector index briefly re-reading 0 just before the position wrap (min/max over the index is not)."""
    si = seg["current_sector_index"].to_numpy()
    t = seg["t_ms"].to_numpy()
    starts = {0: t[0]}
    for k in (1, 2):
        idx = np.where(si == k)[0]
        if len(idx):
            starts[k] = t[idx[0]]
    out = {}
    out["0"] = int(starts.get(1, t[-1]) - starts[0])
    if 1 in starts:
        out["1"] = int(starts.get(2, t[-1]) - starts[1])
    if 2 in starts:
        out["2"] = int(t[-1] - starts[2])
    return out


def main():
    if len(sys.argv) < 3:
        print("usage: python3 groundtruth_oracle.py <frames-csv> <truth-json>", file=sys.stderr)
        return 2

    csv_path, out_path = sys.argv[1], sys.argv[2]
    df = pd.read_csv(csv_path)
    total_frames = len(df)
    pit_frames = int(df["is_in_pit_lane"].sum())

    # Dedup on t_ms (SHM over-poll duplicates); keep the first frame per distinct millisecond.
    dd = df.drop_duplicates(subset="t_ms", keep="first").reset_index(drop=True)
    passes = segment_passes(dd)

    # The out-lap carries the pit-lane frames; the flying lap is the fully-bounded pass with none.
    out_lap = max(passes, key=lambda s: int(s["is_in_pit_lane"].sum()))
    bounded = [s for s in passes if int(s["is_in_pit_lane"].sum()) == 0 and len(s) > 1000]
    flying = max(bounded, key=len) if bounded else max(passes, key=len)

    corners = {}
    for cid, (start, end) in CORNERS.items():
        corners[cid] = {
            "start_position": start,
            "end_position": end,
            "self_time_at_position_ms": time_at_position_ms(flying, start, end),
            "min_speed_kmh": min_speed_kmh(flying, start, end),
            "brake_onset_position": brake_onset_position(flying, start, end),
        }

    truth = {
        "lap_length_m": LAP_LENGTH_M,
        "total_frames": total_frames,
        "deduped_frames": len(dd),
        "pit_lane_frames": pit_frames,
        "pass_count": len(passes),
        "corners": corners,
        "flying_sectors_ms": sector_times_ms(flying),
        "out_lap_sectors_ms": sector_times_ms(out_lap),
    }

    with open(out_path, "w", encoding="ascii") as f:
        json.dump(truth, f, indent=2)
    print(f"wrote {out_path}: {total_frames} frames, {pit_frames} pit, {len(passes)} passes")
    print(f"  Curva Grande (monza_t03) self time-at-position: {corners['monza_t03']['self_time_at_position_ms']:.0f} ms")
    print(f"  Parabolica (monza_t11) min speed: {corners['monza_t11']['min_speed_kmh']:.1f} km/h")
    print(f"  flying S1: {truth['flying_sectors_ms'].get('0')} ms, out-lap S1: {truth['out_lap_sectors_ms'].get('0')} ms")
    return 0


if __name__ == "__main__":
    sys.exit(main())
