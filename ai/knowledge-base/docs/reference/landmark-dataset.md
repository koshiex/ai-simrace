# Corner-landmark dataset (CrewChiefV4)

Vendored MIT file at `src/SimCoach.Reference/Data/trackLandmarksData.json` (Britton IT Ltd) —
the source of named corner geometry for dataset-covered tracks (ADR-0010). 447 KB, 261 entries
spanning many sims; embedded resource, parsed by `LandmarkDataset`.

## Use the ACC-specific `accTrackName`, not `acTrackNames`

Each entry has multiple per-sim name fields (`rf1TrackNames`, `irTrackName`, `acTrackNames`,
`accTrackName`, …). For ACC, the authoritative field is **`accTrackName`** — a string of the form
`"Spa:track config"`. `acTrackNames` (Assetto Corsa, e.g. `["spa:"]`) is a different, partly-stale
namespace; do not key off it for ACC.

Normalize to our `track_id` by taking the substring before `':'`, trimmed and lower-cased:
`"Spa:track config"` → `spa`, `"brands_hatch:track config"` → `brands_hatch`. No alias table is
needed — all covered names map cleanly to `AccTrackCatalog` ids this way.

## ACC coverage: 8 tracks only

Only 8 entries carry an `accTrackName`: `barcelona`, `brands_hatch`, `donington`, `hungaroring`,
`monza`, `nurburgring`, `spa`, `zolder`. Every other ACC track (the catalog has 25) falls back to
`TrackModelBuilder`'s lap-derived, nameless model.

## Landmark fields and the range check

Each landmark: `landmarkName`, `distanceRoundLapStart`, `distanceRoundLapEnd` (metres round the
lap; an `isCommonOvertakingSpot` flag is unused). CrewChief assumes its own lap length, which can
differ from `AccTrackCatalog`'s. `LandmarkDataset` converts metres → normalized position via the
injected lap length and enforces `0 ≤ start < end ≤ lapLengthM`; one out-of-range landmark drops
the **whole** track to the derive fallback rather than placing a corner wrong. Spa (15 landmarks)
fits: max end 6830 m ≤ 7004 m lap.
