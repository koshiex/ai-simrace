# ACC `.ghost` (GhostCars) format — reverse-engineering log

Living record of what we know about ACC's hotlap **GhostCars** file, evaluated as a source of an external
beyond-PB racing **LINE** (the one thing MoTeC `.ld` and the shared-memory replay path could not give us —
see `ideal-line-reference-research.md` §7 and `ai/knowledge-base/docs/reference/acc-shared-memory-layout.md`).

Firsthand-decoded and verified against our own recorded Monza line (median 1.15 m point-to-line distance).

## Where these files come from (sourcing)

- ACC writes a ghost to `Documents/Assetto Corsa Competizione/GhostCars/Offline/<track>/<Dry_Car_Name>.ghost`.
- **You can harvest a ghost by opening a downloaded REPLAY** (e.g. a hotlap-server replay): playing it makes
  ACC materialise a ghost of the **followed car**.
- **Critical caveat:** the ghost car = the car the replay is following, which is **NOT necessarily the fastest
  car on the server**. To get an alien-pace line you must open the replay **focused on the fastest car**
  before it writes the ghost. The two specimens we have (`Dry_Ferrari_296_GT3.ghost`,
  `Dry_Ferrari_296_GT3(1).ghost`) are slow reconnaissance drives, not hotlaps — usable to prove the decoder,
  not as a beyond-PB reference.
- **One car per ghost file** (multi-car is a `.rpy` replay, not a ghost). But **a ghost can hold MULTIPLE
  LAPS** — the record stream is one continuous monotonic-time drive whose world path simply retraces the loop
  N times. Our two specimens are ~1 lap each (path 4696 m ≈ 0.81 lap; 5746 m ≈ 0.99 lap), but the importer
  must lap-split by world-position loop closure (there is no normalized-position channel in the file).

## Container

UE4 compressed-chunk archive. One or more chunks, each a `0x30`-byte header then a zlib stream:
- `+0x00` u64 magic `0x9E2A83C1` (bytes `c1 83 2a 9e …`)
- `+0x08` u64 block size `0x20000` (131072)
- `+0x10`, `+0x20` u64 compressed/uncompressed size pairs
- zlib stream begins at chunk `+0x30`; concatenate every chunk's inflate output → the payload.

Example (`Dry_Ferrari_296_GT3.ghost`): 3 chunks, zlib `78783→131072`, `81616→131072`, `2830→4658`,
concatenated payload = 266802 bytes. The `(1)` specimen: payload 198032 bytes.

## Payload layout

Header (offsets into the decompressed payload):
- `+0` u32 = bytes following this field (payload length − 4, exact)
- `+4` u32 = 4 (version?)
- `+8` u32 — unknown (≈ a lap-time-ms-looking value in specimen 0, but inconsistent — unresolved)
- `+12` u32 = 800 — unknown (equals ACC physics page size; likely coincidence)
- `+16` u8 = 0
- `+17` u32 = string length incl NUL, then the track id string (`"monza\0"`) at `+21`
- `+21+len` u32 = **record count**
- records start immediately after; an 11-byte trailer (`u32 3` then zeros) closes the payload.
  Arithmetic checks exact: `recStart + count*130 + 11 == payloadLen`.

**Record = 130 bytes** (found by byte autocorrelation; peaks at lag 130/260). Fields (little-endian):
- `+0`  f32 world **X** (m)   — Monza range matches our SHM `carCoordinates` X `[-398,858]`
- `+4`  f32 world **Y** (m)   — near-flat `[-13.6,-0.7]`
- `+8`  f32 world **Z** (m)   — matches `[-1126,1045]`
- `+12` f32 **yaw** (rad), convention `yaw = atan2(-dx, dz)` (0.009 rad fit residual)
- `+16` f32 pitch (rad) · `+20` f32 roll (rad)
- `+24` u8 **BRAKE** (value/255 = brake fraction) · `+25` u8 **THROTTLE** (value/255) — DECODED (see §Pedals).
- `+26` u8 ≈always 0 (clutch/pad; ~0 for GT3 auto-clutch) · `+27` u8 nine discrete levels (×32) — NOT gear,
  NOT steer, uncorrelated with everything · `+28..+29` smooth high-entropy channel, NOT steer, NOT yaw-rate
  (likely wheel-visual rotation/phase). **Gear + steer are NOT recoverable.**
- `+30..+125` four 24-byte wheel blocks (per-wheel thermal/load; each f32 tyre-temp-like; no gear/rpm/wheel-speed).
- `+126` f32 **timestamp**, strictly monotonic, **logarithmically encoded** (see §Clock). Speed is derived,
  not stored — and **not trustworthy at high speed** even after the clock is solved.

## Confirmation (why we trust the line)

Re-derived from raw bytes, then tested against ground truth the decode never used (our own
`references/monza_bmw_m4_gt3_dry-warm.parquet` world line): nearest-neighbour distance median **1.15 m**,
p95 3.16 m, 99.3% within 8 m; **0 backtracks** over 2051 steps mapping to strictly non-decreasing
`position_normalized`; loop closes within official track length; peak derived speed occurs on a
verified-straight section. Random/misaligned data would be off by hundreds of metres.

## Open debts (before this is a usable reference)

1. **~~Clock~~ SOLVED (form), speed still untrustworthy** — see §Clock below. The `+126` timestamp is a
   universal **logarithmic** encoding; laptime reproduces exactly for all 9 cars. But the log compresses
   time-resolution on fast sectors, so derived *vmax* is 400–570 km/h (true ~285) — **speed is not usable**.
   Only laptime (exact), median speed (~195 km/h) and brake-zone/chicane *shape* survive. Irrelevant for a
   LINE-only ship (positions unaffected).
2. **~~Pedals~~ brake/throttle DECODED** (`+24`/`+25`), see §Pedals. Gear/steer/clutch remain undecoded and
   are not recoverable from the record. Pedals can't feed TIME anyway → still LINE-only.
3. **Lap-splitting** must be done by world-position loop closure (no normalized-position channel).
4. **Single-specimen field order** — record field order verified on 2 files / 1 car (Ferrari 296 + BMW M4) /
   Monza. Import must **fail-fast** on the arithmetic + bbox + loop-closure + deviation-ceiling guards; a new
   car/track needs re-validation before it is trusted.

## Validated on ALIEN laps (accreplay.com) — beyond-PB LINE confirmed

Source found: **accreplay.com** serves ghosts publicly, no login. Angular SPA backed by a REST API —
leaderboard `GET /api/leaderboards/laps?trackId=3&group=GT3` (Monza = trackId 3), download
`GET /api/laps/{lapId}/download-ghost` (returns a ZIP containing the inner `.ghost`). Downloaded 9 alien GT3
laps (01:45–01:47, top of board) to `C:\Users\koba9\Desktop\ghosts\`. Filenames encode car + laptime + driver,
so **every ghost has a KNOWN laptime** — the clock-calibration anchor we lacked.

**Verified accreplay `trackId` map** (read off the `download-ghost` ZIP path `GhostCars/Offline/<track>/…` of
each board's top lap; the ZIP path is the ground-truth track slug, not the leaderboard label):

| id | track | id | track | id | track |
|----|-------|----|-------|----|-------|
| 1 | brands_hatch | 6 | silverstone | 11 | zandvoort |
| 2 | spa | 7 | hungaroring | 12 | kyalami |
| 3 | monza | 8 | nurburgring | 13 | mount_panorama |
| 4 | misano | 9 | barcelona | 14 | laguna_seca |
| 5 | paul_ricard | 10 | zolder | | |

Only tracks with a **vendored centerline** (alignment target for the ~2 m guard + pn mapping) can be imported
today — Monza + Spa. `SimCoach.GhostImport`'s in-tool `_trackIds` map carries just that importable subset; add
an id here (and to the tool) once its centerline is baked.

**Decisive test — alien BMW M4 GT3 (01:46.037 = 106.037 s, ~7 s faster than our 113.000 s PB, same car/track)
vs OUR OWN recorded BMW line** (`references/monza_bmw_m4_gt3_dry-warm.parquet`, nearest-point):
- deviation **median 0.98 m, mean 1.43 m, p95 4.0 m, max 5.95 m**; 49% of points >1 m, 24% >2 m, 12% >3 m.
- concentrated at specific corners: **pn 0.55–0.60 ≈ 2.4 m, pn 0.90–1.00 ≈ 3.5 m** — the alien runs a
  materially different line there.

This is exactly what the M38 self-median could NOT deliver (a consistent driver deviates ~0 from their own
median): a **real, non-zero, faster line** with per-corner "take a different line here" signal, decoded in our
own world frame. **The ghost path is validated as a beyond-PB LINE source.** (median 0.98 m also re-confirms
the decode: the alien line sits ON the Monza racing surface.)

## Clock: SOLVED as a logarithmic law (speed still not trustworthy)

Both earlier hypotheses (wrong stride, shifted timestamp offset) were **falsified**: stride is 130 and the
timestamp is the f32 at `+126` for **all 9** cars (`recStart + count*130 + 11 == payloadLen` holds exactly;
`+126` is the only strictly-monotone f32 column). The real cause of the bogus "per-car scale" (9.3 / 19.3 /
68 s per raw unit): **the `+126` timestamp is logarithmically encoded.**

```
real_elapsed_time = A · exp(B · raw_ts)     B ≈ 1.41 (≈ √2), UNIVERSAL
A = knownLaptime / (exp(B·ts_last) − exp(B·ts_first))   # per-car amplitude, from the laptime anchor
```

Evidence: `corr(raw_ts, log(elapsed)) = 0.99977`; the ground-truth clock derivative `d(real)/d(raw_ts)` rises
smoothly 1 → 137 across a lap, tracking `A·B·exp(B·raw_ts)`. This reproduces all 9 laptimes. The "7× outliers"
(Ferrari 0.81 lap, McLaren 0.84 lap) are **partial** ghosts that start mid-lap at high `raw_ts` where the log
is compressed — consistent under the exp law, absurd under a linear one.

**But derived SPEED is still not trustworthy.** The log encoding loses time-resolution on fast sectors (at
high `raw_ts` the clock-derivative IQR ≈ its median), so even after uniform-arc resample + smoothing, vmax
reads 400–570 km/h (true ~285) and ~16 % of a full lap reads >300 km/h. **Reliable:** laptime (exact), median
speed (~195 km/h, physical), brake-zone/chicane *shape* (corr ~0.83 to our reference). **Unreliable:**
absolute high-speed values. The two partial-lap cars are only approximately A-calibrated (full laptime over a
partial path). `B ≈ 1.41` is empirical; the exact constant is unconfirmed. → **Ship LINE-only; ghost TIME/speed
never feeds `delta_ms` / `min-speed`.**

## Pedals: brake/throttle DECODED

In the 6 bytes at `+24`: **`+24` = BRAKE (u8, /255), `+25` = THROTTLE (u8, /255).** Verified against our BMW
reference (nearest-position map): `corr(+24, brake_pct) = +0.93`, `corr(+25, throttle_pct) = +0.87`. The ACC
signature is self-evident even without a reference — full-throttle records are `[+24=0, +25=255]`, braking
records `[+24=255, +25=0]` (mutually exclusive). Physical cross-check: `+24` (brake) correlates with `g_long`
at **−0.90** (braking → deceleration). Byte assignment confirmed firsthand on 1 car (BMW); the anti-correlated
`[0,255]`/`[255,0]` pattern and the g_long check are car-independent. **Gear, steer, clutch: not recoverable.**
Pedals can't feed a TIME reference, so this stays a LINE-only feature — but a future *line-anchored,
clock-free* positional brake-point cue is now conceivable (deferred).

## All-9 alien consensus line (Monza GT3) — where the alien line really differs

Decoded world XZ for **all 9** alien Monza GT3 ghosts, nearest-point onto our BMW reference, signed lateral
deviation binned into 100 `position_normalized` bins, aggregated across cars. **Baseline: global |deviation|
median = 0.99 m ≈ car-width** — the aliens are on OUR line almost everywhere; per-car full-lap |dev| medians
cluster at 0.89–1.12 m. The *null is "same line."* Only a contiguous mid-lap band shows a real, shared
difference (sign = side of our line; std = spread across the 9 cars):

| pn range | corner (approx) | median dev | cross-car std | agreement | confidence |
|---|---|---|---|---|---|
| 0.00–0.02 | start/finish + Rettifilo | −2.1 m | 3.5 m | 71 % | **ARTIFACT** (loop-closure seam) |
| 0.31–0.40 | slow corner ~68 km/h | −1.2…−2.0 m | 0.22 | 9/9 | solid |
| **0.45–0.59** | **Lesmo→Serraglio 180–250 km/h** | **−2.1 m** (peaks −3.7 @0.45, −3.2 @0.56) | **0.24** | 9/9 | **STRONGEST** |
| 0.67–0.68 | — | +1.4 m | 0.18 | 9/9 | solid |
| **0.73–0.78** | **Variante Ascari ~200 km/h** | **+3.1 m** (peak +4.3 @0.74), opposite side | 0.57 | 9/9 | **strong** |
| 0.83–0.88 | fast pre-Parabolica | −1.25 m | 0.33 | 9/9 | solid |
| 0.92–1.00 | Parabolica onto straight | +3.9 m | 2.1 | 89 % | **LOW** (seam + car-specific) |

**It is a COMMON alien racing line, not car-specific:** in every strong cluster the 9 different GT3 chassis
(incl. the alien BMW M4 = our car) converge to within 0.2–0.6 m of *each other* while sitting 1.2–4.3 m off
*our* line — cross-car spread ≪ deviation-from-us. Dropping the 2 partial-lap ghosts did not move the
full-lap medians (−2.13 vs −2.14 at cluster-B). **Car-specific/noisy behaviour appears only at the two lap
seams** (0.00–0.02 and 0.92–1.00). → The two headline "take a different line here" corners are **pn 0.45–0.59**
and **pn 0.73–0.78**; **discard the two seam bins** — they are the least trustworthy despite Parabolica being
a real corner.

## Verdict for the feature

The `.ghost` decode gives a real, grid-alignable, beyond-PB world **LINE** (in OUR frame, unlike MoTeC `.ld`)
— **validated** against a 7-s-faster same-car alien and confirmed as a 9-car *shared* line. The two historical
blockers are now moot **for a LINE-only ship**: the clock is solved in form (laptime exact) but speed stays
untrustworthy, and pedals are decoded but single-car-verified and can't feed TIME anyway. So the feature ships
**LINE-ONLY** — see the reviewed implementation plan `beyond-pb-pr-plan.md` (PR-B3). Kept OFF the M46 critical
path: M46 (own-optimal, own data) ships first as PR-B1 and introduces the reference-`kind` mechanism the ghost
line reuses. **New hard constraint from review:** the two seam bins (pn 0.00–0.02, 0.92–1.00) must be
*masked*, not zeroed — zeroing makes `InterpWorldXZ` interpolate toward the origin and fabricate a
multi-hundred-metre Parabolica deviation cue.
