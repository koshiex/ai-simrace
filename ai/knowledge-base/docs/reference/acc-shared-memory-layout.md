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
- `graphics.surfaceGrip` always returns 0 in ACC.
- `physics.suspensionDamage` works (PyAcc's "not used" comment is wrong).
- Static page has no `packetId` — seqlock applies to physics/graphics only
  (`AccPageMarshaller.ReadPacketId`).
- The struct ports marshal more than `AccFrameMapper` currently surfaces into `TelemetryFrame`.
  Already-marshalled-but-unmapped (no struct change needed to use them): `graphics.CarCoordinates`
  (flattened `float[60][3]`; player car = `CarCoordinates[PlayerCarId*3 + {0,1,2}]`, world XYZ in m),
  `graphics.CurrentSectorIndex` (0-based), `static.SectorCount`. Phase 2 maps these to
  `world_pos` / `current_sector_index` / `sector_count` — mapper-only, no marshalling work.
