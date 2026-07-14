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
- `+24` 6 bytes **UNDECODED** (candidate for packed inputs/gear — not clean f32/f16/u16)
- `+30..+125` four 24-byte wheel-visual blocks (rotation/suspension-like; semantics undecoded)
- `+126` f32 **timestamp**, strictly monotonic, **variable/adaptive** sample interval (keyframes denser in
  corners: step size anti-correlates with curvature, r≈−0.40). **No throttle/brake/steer/gear as clean
  channels.** Speed is not stored — derive as Δpos/Δt.

## Confirmation (why we trust the line)

Re-derived from raw bytes, then tested against ground truth the decode never used (our own
`references/monza_bmw_m4_gt3_dry-warm.parquet` world line): nearest-neighbour distance median **1.15 m**,
p95 3.16 m, 99.3% within 8 m; **0 backtracks** over 2051 steps mapping to strictly non-decreasing
`position_normalized`; loop closes within official track length; peak derived speed occurs on a
verified-straight section. Random/misaligned data would be off by hundreds of metres.

## Open debts (before this is a usable reference)

1. **Clock not universally calibrated.** A ×128 scale reproduced specimen 0's 282 km/h anchor, but specimen
   `(1)` has timestamp range `[-3.14, 7.81]` (×128 ⇒ absurd 1400 s), so the timestamp unit is **not a fixed
   ×128** — semantics still open. Positions (the LINE) are unaffected; **derived speed is not trustworthy**
   until the clock is pinned (e.g. against a self-recorded ghost of known wall-clock duration).
2. **Pedals/gear undecoded** — the 6 bytes at `+24` and the per-wheel fast slots. Without them the ghost is
   **line + rough speed only, no inputs** (can't coach brake/throttle points off it).
3. **Lap-splitting** must be done by world-position loop closure (no normalized-position channel).
4. **Single-specimen field order** — layout verified on 2 files, both Ferrari 296 / Monza; treat field order
   as provisional until a different car/track confirms it.

## Validated on ALIEN laps (accreplay.com) — beyond-PB LINE confirmed

Source found: **accreplay.com** serves ghosts publicly, no login. Angular SPA backed by a REST API —
leaderboard `GET /api/leaderboards/laps?trackId=3&group=GT3` (Monza = trackId 3), download
`GET /api/laps/{lapId}/download-ghost` (returns a ZIP containing the inner `.ghost`). Downloaded 9 alien GT3
laps (01:45–01:47, top of board) to `C:\Users\koba9\Desktop\ghosts\`. Filenames encode car + laptime + driver,
so **every ghost has a KNOWN laptime** — the clock-calibration anchor we lacked.

**Decisive test — alien BMW M4 GT3 (01:46.037 = 106.037 s, ~7 s faster than our 113.000 s PB, same car/track)
vs OUR OWN recorded BMW line** (`references/monza_bmw_m4_gt3_dry-warm.parquet`, nearest-point):
- deviation **median 0.98 m, mean 1.43 m, p95 4.0 m, max 5.95 m**; 49% of points >1 m, 24% >2 m, 12% >3 m.
- concentrated at specific corners: **pn 0.55–0.60 ≈ 2.4 m, pn 0.90–1.00 ≈ 3.5 m** — the alien runs a
  materially different line there.

This is exactly what the M38 self-median could NOT deliver (a consistent driver deviates ~0 from their own
median): a **real, non-zero, faster line** with per-corner "take a different line here" signal, decoded in our
own world frame. **The ghost path is validated as a beyond-PB LINE source.** (median 0.98 m also re-confirms
the decode: the alien line sits ON the Monza racing surface.)

## Clock: partially pinned (per-car offset issue)

Per-file `scale = knownLaptime / timestamp_span` is **consistent (~10 s per raw unit) for 6 of 9 cars**
(Aston/Bentley/Ford/Lambo/Nissan/Porsche, all full-lap ~5748 m paths). But **BMW (scale 19.3 ≈ 2×) and
Ferrari/McLaren (~68 ≈ 7×) are outliers**, and BMW's derived speed blew up (an 18.8 s dt gap) — i.e. the
`+126` timestamp offset is NOT universal; the record layout (wheel/aux block sizes) likely shifts the clock
field per car. So: the LINE (positions) is solid for all 9; a trustworthy SPEED profile needs the per-car
timestamp offset re-found (tractable RE — anchor each car's clock against its known laptime). Pedals/gear
(the 6 bytes at +24) remain undecoded.

## Verdict for the feature

The `.ghost` decode gives a real, grid-alignable, beyond-PB world **LINE** (in OUR frame, unlike MoTeC `.ld`)
— **validated** against a 7-s-faster same-car alien. Open before it ships: (1) finish the per-car timestamp
offset for a trustworthy speed profile, (2) decode pedals (or accept line+speed only). Kept OFF the M46
critical path — M46 (own-optimal, own data) ships first; the alien-LINE is the complementary next feature.
