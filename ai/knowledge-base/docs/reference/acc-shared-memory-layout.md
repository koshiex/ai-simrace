# ACC shared memory — layout provenance and known pitfalls

C# mirrors live in `src/SimCoach.Adapters.ACC/SharedMemory/`; every field offset is pinned by
golden tests in `tests/SimCoach.Adapters.ACC.Tests/`. This card records what is NOT visible
from the code: where the layout came from and which traps exist in third-party sources.

## Authoritative layout

- Official Kunos doc: "ACC Shared Memory Documentation" V1.8.12 (assettocorsa.net forum thread
  59965). **Last layout revision** — ACC 1.9.x/1.10 changed nothing; layout only ever grows by
  appending fields, never reordering.
- Packing: `#pragma pack(4)`, byte-identical to default MSVC packing for these structs.
- Golden sizes: physics **800**, graphics **1588**, static **820** bytes.
- Wheel arrays are always ordered [FL, FR, RL, RR]. Native gear encoding: 0=R, 1=N, 2=first.

## Pitfalls in third-party implementations (do not copy from them blindly)

- **`trackConfiguration` is `wchar_t[33]`, NOT `[15]`.** The `[15]` bug originated in
  mdjarv/assettocorsasharedmemory and propagated to heikowulf/ACCsharedmemory,
  rsys-dev/accsharedmemory and rrennoir/Jackal — it shifts every static-page field after
  `trackSPlineLength` by 36 bytes. Correct `[33]` confirmed by the Kunos C++ header
  (IkoRein/ACC_ShmemUDP_Relay), PyAccSharedMemory reads, and the Dekadee/accshm Go port.
- **PyAccSharedMemory maps only 784 bytes for the static page** (inherited from the same bug
  family) while its reads sum to 820 — its `wetTyresName` is silently truncated. Use 820.
- **IkoRein's header appends `wchar_t reserved[20]` to each struct** — his own forward-compat
  padding, not part of the game layout.
- Type discrepancies that don't affect offsets (4 bytes either way): `currentMaxRpm` int (header)
  vs float (PDF); `aidAllowTyreBlankets` int (header) vs float (PDF). We follow the header.
- Penalty enum: documented 0–21, but 22 (disqualified wrong way) observed in the wild — never
  validate penalty values against a closed range.

## Padding rules (the only 7 pad spots)

Pack=4 inserts 2 bytes when a `wchar_t[N]` array **ends at an offset not divisible by 4** and
the next field needs 4-byte alignment (int/float). An array ending 4-aligned gets no pad even if
odd-length: the four consecutive `wchar_t[15]` at the top of graphics end at 132, and
`acVersion[15]` in static ends at 60 — no padding there. Pad spots: graphics after
`tyreCompound[33]` (242→244), `deltaLapTime[15]` (1358→1360), `estimatedLapTime[15]`
(1394→1396), `trackStatus[33]` (1482→1484); static after `playerNick[33]` (398→400),
`trackConfiguration[33]` (590→592), `carSkin[33]` (670→672).

## Useful runtime facts

- `smVersion` reports the shared-memory layout version ("1.8"), not the game version.
- `static.trackSPlineLength` is not populated by ACC (returns 0) — lap distance comes from
  `AccTrackCatalog`; `physics.clutch` is engagement (0 = pedal pressed, 1 = released).
- `physics.steerAngle` is a NORMALIZED steering input [-1..1] of full lock (Kunos doc: "Steering
  input value"), not radians/degrees. Wheel angle = steerAngle × lock-to-lock/2 — per-car locks
  live in `AccCarCatalog`. Two cars where the official doc is wrong: bmw_m4_gt3 is 516° (doc 540),
  honda_nsx_gt3_evo is 436° after the 1.9 update (doc 620). Source of truth: Race Element /
  acc-steering-lock plugin tables.
- `static.track` ids are MIXED case ("Spa", "Paul_Ricard", "brands_hatch"); ACC server configs
  and results JSON use all-lowercase ids — different namespaces, normalize case-insensitively.
- **`graphics.PlayerCarId` is a car id VALUE, not a slot index into `CarCoordinates`/`CarId`.**
  It holds one of the values stored in the `CarId[60]` array (in practice 1001-based, not 0), so
  the player's coordinates are `CarCoordinates[slot*3 + {0,1,2}]` where
  `slot = Array.IndexOf(CarId, PlayerCarId)`. Indexing `CarCoordinates[PlayerCarId*3]` directly
  reads far out of bounds (e.g. 1001×3 = 3003 vs a 180-length array) → on a defensive
  bounds-guard it silently yields zero world coords on every live frame. Pinned by
  `AccGraphicsPageLayoutTests` (`CarId[0]=PlayerCarId=1001`, `CarCoordinates[3]`=second car).
- `graphics.surfaceGrip` always returns 0 in ACC.
- `physics.suspensionDamage` works (PyAcc's "not used" comment is wrong).
- Static page has no `packetId` — seqlock applies to physics/graphics only
  (`AccPageMarshaller.ReadPacketId`).
- The struct ports marshal more than `AccFrameMapper` currently surfaces into `TelemetryFrame`.
  Already-marshalled-but-unmapped (no struct change needed to use them): `graphics.CarCoordinates`
  (flattened `float[60][3]`; player car = `CarCoordinates[slot*3 + {0,1,2}]` where
  `slot = Array.IndexOf(CarId, PlayerCarId)` — see the PlayerCarId pitfall above, world XYZ in m),
  `graphics.CurrentSectorIndex` (0-based), `static.SectorCount`, `physics.NumberOfTyresOut`
  (**"Not used in ACC" → always 0 live**, honest passthrough only), `graphics.IsValidLap`
  (ACC `int`, `!= 0 → true`). Phase 2 maps these to `world_pos` / `current_sector_index` /
  `sector_count` / `tyres_out` / `is_valid_lap` — mapper-only, no marshalling work.

## SHM validity by AC_STATUS — telemetry is only real when LIVE (replay yields nothing usable)

Measured with `tools/SimCoach.ShmProbe` (reads the raw pages, bypassing the new-frame gate). Three
distinct regimes — **replay capture from shared memory is not possible**:

| Context | AC_STATUS | physics packetId | physics channels | graphics `CarCoordinates` | Static (Track/CarModel) |
|---|---|---|---|---|---|
| **Live driving** | `2 LIVE` | advances ~333 Hz | real (speed/gas/brake/gear/AccG) | real, moving (all active cars) | populated |
| **Menu-loaded `.rpy`** (standalone viewer) | `0 OFF` | **frozen** | all zero | all zero | **empty** — no session |
| **In-session replay** (ESC→replay in a live session) | `1 REPLAY` | **advances** (heartbeat) | **all zero** | **frozen** at entry position | populated |

A menu replay leaves the SHM entirely dormant (the `Local\acpmf_*` map exists — `TryConnect` succeeds — but
every page is zeroed; the empty Static page is the tell). An in-session replay is more deceptive: it keeps
the packetId **ticking** and reports `AC_STATUS=1`, so `AccFrameMapper.IsRecordable(…, allowReplay: true)`
admits the frames and `AccFrameAcquisition` sees "new frames" — but the physics page is **blanked to zero**
(not the last live value — actively zeroed) and the graphics car coordinates are **frozen** at the position
where the replay was entered. Recording it yields a stream of zero-speed, fixed-position frames.
**Conclusion: the `AccReaderOptions.AllowReplayCapture` gate is mechanically correct but has no usable data
behind it — ACC does not expose telemetry via shared memory during replay.** An external fast lap must come
from a non-SHM source (MoTeC `.ld` export) or be reconstructed from a **live** race.

Corollary (a live-only opportunity): during **live** driving the graphics page holds every active car's world
position (`CarCoordinates`, indexed by `CarId`, count = `ActiveCars`), updated in real time — so a faster
opponent's *line* (world XZ → speed via Δpos/Δt) is capturable **live** from a race, though their pedals/g are
not (physics page is player-only).

## MoTeC `.ld`/`.ldx` export — rich channels, but NO world position (can't anchor a line to our grid)

ACC's file-based telemetry export (evaluated as an external reference-lap source) is **off by default** —
enable in-session via car setup → **ELECTRONICS → TELEMETRY LAPS → N**; it then writes the last N laps
per session to `Documents/Assetto Corsa Competizione/MoTeC/` (paired `.ld` channel data + `.ldx` XML lap
markers, `Time` in µs), with no invalid-lap filtering. Format: little-endian, magic `0x40`, channels are a
doubly-linked list of contiguous per-channel blocks (`datatype_a`+`datatype` codes, `scale/mul/shift`).
Portable references: `gotzl/ldparser` (Python, **GPL — use as layout spec only, do not port the code**),
`t-babin/ACC-Telemetry-Tracker` (C#, MIT — structural starting point).

Channels present: `Ground Speed`, `Throttle Pos`, `Brake Pos`, `Steering Angle`, `Gear`, `Engine RPM`,
`CG Accel Lateral/Longitudinal` (gLat/gLon), wheel speeds, suspension, tyres. **NOT present: world XYZ, a
lap-distance channel, or normalized/spline position.** MoTeC i2 dead-reckons the track map — distance
`s=Σ(v·dt)`, heading `θ=Σ(a_lat/v·dt)`, `x=Σ v·cosθ·dt`, `y=Σ v·sinθ·dt`, then a loop-closure correction —
so the map is an **arbitrary, drift-corrected local frame, not world coordinates**. Consequence: a `.ld`
**cannot anchor a reference LINE** to our `carCoordinates` grid; only **distance-axis** channels align (via
`Σspeed` ↔ `normalizedCarPosition`). Grid-anchored lines must come from live SHM `carCoordinates`, not `.ld`.
And a foreign "alien" `.ld` is not a shippable beyond-PB source: no redistributable public corpus; the good
ones are paid, personal-license (Coach Dave Delta, Driver61) — can't be bundled.
