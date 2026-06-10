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

Pack=4 inserts 2 bytes after an odd-length `wchar_t[N]` array **only when the next field is
int/float**. Consecutive wchar arrays get no padding (e.g. the four `wchar_t[15]` at the top of
graphics). Pad spots: graphics after `tyreCompound[33]`, `deltaLapTime[15]`,
`estimatedLapTime[15]`, `trackStatus[33]`; static after `playerNick[33]`,
`trackConfiguration[33]`, `carSkin[33]`.

## Useful runtime facts

- `smVersion` reports the shared-memory layout version ("1.8"), not the game version.
- `graphics.surfaceGrip` always returns 0 in ACC.
- `physics.suspensionDamage` works (PyAcc's "not used" comment is wrong).
- Static page has no `packetId` — seqlock applies to physics/graphics only
  (`AccPageMarshaller.ReadPacketId`).
