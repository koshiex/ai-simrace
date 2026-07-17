# Track B — мульти-трек reference из accreplay-ghost'ов (blueprint)

**Цель:** alien-LINE + baked centerline + corner-geometry (track-модель) на **все 14 GT3-трасс ACC**,
собранные целиком из публичных accreplay-ghost'ов — без заездов владельца, без имён пилотов.
После Track B коуч полноценно работает (линия + corner-tip'ы) на всех трассах.

**Решения владельца (2026-07-17):**
- Centerlines для 12 недостающих = медиана из accreplay-ghost'ов.
- Corner-geometry для 12 = детект из ghost-centerline, **curvature-only** (ghost'ы не несут lateral-G).

## Переиспользуем (подтверждено рекогносцировкой)

- `MedianCenterlineBuilder.Build(...)` — median по 1-метровым бинам, `MinLapsForTrust=3`.
- `CornerCenterlineDetector.Detect(centerline, perLapCenterlines)` — углы из centerline. **Фьюжн
  `fused = MathF.Max(absK·180, |G|)` (cs:350).** При `LateralG=0` во всех бинах fused = чисто кривизна
  → **curvature-only детект БЕЗ изменения детектора** (просто строим centerline c G=0). Апекс/радиус —
  из точки максимальной кривизны. Chicane-split (consensus по per-lap) тоже работает на fused=кривизна.
- `AccTrackCatalog.TryGetLapLengthM` — lap length всех 14 (+11) трасс уже в репо.
- `tools/SimCoach.GhostImport` — fetch / decode / lap-split (iterate-to-usable) / align / resample /
  seam-mask / persist. `tools/SimCoach.Bake` — образец сериализации corner-geometry JSON.

## Дизайн по под-фазам

### B1. Ghost-median centerline builder (новый путь `GhostImport bake-centerline`)
- Fetch top-N GT3 ghost'ов/трек (N≈12) через существующий `FetchGt3LeaderboardAsync`; decode + lap-split
  каждого; взять **complete-loop** лап от каждого (переиспользовать iterate-to-usable — топ-борда часто
  recon без замыкания петли). Собрать пул из K usable ghost-лапов (нужно K≥`MinLapsForTrust`=3).
- Каждый ghost-лап → `MedianCenterline`-совместимый набор (world X/Z по 1-метровым бинам через
  `MedianCenterlineBuilder`). Lap length из `AccTrackCatalog`. **LateralG=0** во всех бинах.
- Coherence guard (переиспользовать `CenterlineCoherence`): ≥3 usable, разумный span, нет дыр.
- Emit `centerline.<track>.json` (`CenterlineGeometryDocument`, SchemaVersion=1, LapCount=K, Bins,
  LateralG=0).

### B2. Corner geometry из ghost-centerline (`GhostImport bake-corners`)
- `CornerCenterlineDetector.Detect(ghostCenterline, perLapGhostCenterlines)` → `DetectedCorner[]`
  (curvature-only, т.к. G=0). Сериализовать в `cornerGeometry.<track>.json` в формате, что читает
  `CornerGeometryDataset` (свериться с `tools/SimCoach.Bake` output + `cornerGeometry.monza.json`).
- **Валидация per-track:** число/позиции углов sanity-чек против известной раскладки трассы (напр.
  Silverstone ~18, Barcelona ~14). Логировать детект, не принимать молча.

### B3. Alien lines все 14 (`GhostImport import`, как есть)
- Против каждой centerline (Monza/Spa — своя M38; 12 — ghost-derived B1) прогнать существующий import
  (align fastest-usable → resample → seam-mask → `alien_line.<track>.parquet`). Dry-gate (OD12) на
  рантайме без изменений.
- Guard: median-deviation ≤ 2 м. Трасса без usable loop / за ceiling — **явно залогировать и
  пропустить**, список пропусков в PR (не тихо).

### B4. Vendor
- Скопировать `centerline.<track>.json` (12), `cornerGeometry.<track>.json` (12),
  `alien_line.<track>.parquet` (≤14) в `src/SimCoach.Reference/Data/`. Embed-glob + культурный фикс
  (`LogicalName`/`WithCulture`) уже на месте. **Перепроверить culture-collision по всем 14 track-id**
  (пока подтверждена только `spa`); `cota`, `imola` и т.п. проверить через `CultureInfo.GetCultureInfo`.
- Регресс-тест: `Load()` резолвит по одной новой трассе каждого типа (centerline/corner/alien).

## ADR-0022 — ghost-derived centerline
Провенанс M38-centerline меняется: для 12 трасс это медиана **чужих** быстрых кругов (accreplay),
а не своих. Семантика та же («медианная ездовая линия»), но источник иной и G=0 (corner-geom
curvature-only). Зафиксировать в ADR: источник, приватность (только агрегат, без имён/ghost'ов),
curvature-only детект, отличие от M38-owner-baked (Monza/Spa остаются owner-baked).

## Разбивка на коммиты (task=commit, abort-on-non-green)
1. `feat(ghostimport): ghost-median centerline builder (bake-centerline)` + юнит-тесты на синт-ghost'ах.
2. `feat(ghostimport): corner detection from ghost centerline (bake-corners, curvature-only)` + тесты.
3. `docs(adr): ADR-0022 ghost-derived centerline provenance`.
4. `feat(reference): vendor <track> centerline+corners+alien` — **по одной трассе на коммит** (или
   пачкой), после dev-time генерации; каждый green.
5. `test(reference): Load() resolves a ghost-vendored track (culture recheck)`.

## Dev-time генерация (ручной шаг вне CI, сетевой)
Для каждой из 12: `GhostImport bake-centerline --track X` → `bake-corners --track X` → `import --track X`
→ проверить guard'ы/лог → скопировать 3 артефакта в Data/. Как Monza/Spa. Пропуски документировать.

## Открытые пункты для S→D→J валидации плана
- N ghost'ов на трек и порог usable K; что если K<3 (трасса без достаточного числа замыкающих ghost'ов).
- Формат `cornerGeometry.<track>.json`: точно ли `DetectedCorner` → нужный JSON (свериться с Bake +
  `CornerGeometryDataset` loader); нужен ли landmark-слой или corner-id схема.
- Curvature-only качество: не пере-/недо-детектит ли на трассах с длинными пологими поворотами
  (где G помогал бы); нужен ли отдельный curvature-порог.
- Culture-collision по 14 track-id (полный список проверить).
- Per-track приёмка: чем валидируем корректность corner-раскладки без ground-truth заезда.
- Ghost-lap resample rate: достаточно ли точек ghost-записи для 1-метрового бина на длинных трассах.
