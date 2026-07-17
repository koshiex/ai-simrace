# Track B — мульти-трек reference из accreplay-ghost'ов (blueprint, S→D→J-validated)

**Цель:** alien-LINE + baked centerline + corner-geometry (track-модель) на **все 14 GT3-трасс ACC**,
собранные целиком из публичных accreplay-ghost'ов — без заездов владельца, без имён пилотов.
После Track B коуч работает на всех трассах: линия везде; corner-tip'ы на медленных И быстрых поворотах.

**Решения владельца (2026-07-17/18):**
- Centerlines для 12 недостающих = медиана из accreplay-ghost'ов.
- Corner-geometry для 12 = детект из ghost-centerline; ghost'ы не несут lateral-G, поэтому **добавляем
  curvature-integral канал** (sustained-bend), чтобы вернуть быстрые повороты R>180 м (OD-B1).
- **Отгружаем все трассы, что прошли калибровку; провальные skip+list в PR** (OD-B2).
- 2 м alignment-guard на ghost-трассах = информационный (ADR-0022), реальные бэкстопы — coherence +
  corner-layout калибровка (OD-B3).

## Что S→D→J валидация подтвердила / опровергла
- **ОПРОВЕРГНУТО (не риск):** `DetectedCorner → cornerGeometry.<track>.json` уже решён —
  `CornerGeometryDocument.FromDetected` + `tools/SimCoach.Bake` сериализуют. `AccTrackCatalog` реально
  покрывает все 14. csproj-глоб + `WithCulture=false`/`LogicalName` глобально нейтрализуют culture —
  per-track `CultureInfo` перепроверка НЕ нужна (только `Load()` regression-тест + `git check-ignore -v`).
- **ПОДТВЕРЖДЕНО (blockers, чинятся ниже):** нет GhostRecord→TelemetryFrame адаптера; нет общей
  cross-lap distance-оси; import резолвит centerline через embedded-only `Load()` (порядок генерации).

## Переиспользуем
- `MedianCenterlineBuilder.Build(IReadOnlyList<IReadOnlyList<TelemetryFrame>>)` — median по 1-метровым
  бинам `floor(LapDistanceM)`, `MinLapsForTrust=3`. **Пропускает кадры с `SpeedMps<=0` или `WorldPos==null`.**
- `CornerCenterlineDetector.Detect` — `fused = Max(absK·180, |G|)` (cs:350), активен при `fused≥1.0`.
- `CenterlineAligner.Align` — nearest-bin проекция на существующий centerline (для bootstrap-оси).
- `AccTrackCatalog.TryGetLapLengthM` — lap length всех 14. `GhostImport` fetch/decode/lap-split/import.
- `CornerGeometryDocument.FromDetected`, `tools/SimCoach.Bake` (образец + `CornerGeometryReviewPage`).

## B0. Транскрибировать 12 accreplay trackId
`AccReplayClient._trackIds` сейчас только monza=3/spa=2 → `TrackIdFor` кидает для остальных 12. Перенести
проверенную карту из `acc-ghost-format-re.md` (все 14) в код. **Коммит 0.**

## B1. Ghost-median centerline builder (`GhostImport bake-centerline`)
### B1a. GhostRecord→TelemetryFrame адаптер (BLOCKER-фикс, отдельный коммит+тесты)
GhostRecord = WorldX/Y/Z, Yaw, Brake, Throttle, RawTimestamp — нет Speed/WorldPos-message/LapDistanceM.
Наивный кадр (Speed=0) → builder пропускает 100% → пустой centerline → `LapCount<3` → `TryGetCenterline`
false → **инертный ассет без ошибки**. Адаптер: `WorldPos` из XZ; `GForceG=null` (→G=0); **положительный
placeholder `SpeedMps`** (документировать, что teleport/stationary-guard для ghost'ов инертен);
`LapDistanceM` из общей оси (B1b). Юнит-тесты: `LapCount` и непустые бины на синт-ghost'ах.

### B1b. Общая cross-lap distance-ось (BLOCKER-фикс)
Owner-круги делят sim-сплайн (`LapDistanceM = NormalizedCarPosition·length`); ghost'ы — нет: у каждого своя
кумулятивная arc-length (разная总 длина) и своя фаза старта (`LapSplitter` стартует с `records[0]`), так что
`floor(LapDistanceM)`-биннинг мешает физически смещённые точки → смазанный centerline (дрейф апекса,
сдвоенные/пропущенные повороты). **Bootstrap:** взять самый быстрый usable ghost как провизорный centerline;
спроецировать остальные K-1 через `CenterlineAligner` на общую 0..N ось; затем median по бинам. `LateralG=0`.
Full-lap span coherence-чек. **Пере-вывести coherence + alignment пороги под ghost-arc базис — НЕ
переиспользовать owner-tuned 1 м/2 м вслепую** (owner-envelope Spa 0.52 м / Monza 0.33 м); `CenterlineCoherence`
бинит по той же оси → наследует misalignment и может pass-on-bad-bake.

Emit `centerline.<track>.json` (`CenterlineGeometryDocument`, SchemaVersion=1, LapCount=K, Bins, LateralG=0).

## B2. Curvature-integral sustained-bend канал (OD-B1) — производственный код
### Проблема
С G=0 `fused=Max(absK·180,0)` → активен только R≤180 м. Быстрые дуги (Curva Grande R=255.9 м, spa_t02
R=272.9 м, spa_t16 R=243.6 м — в owner-картах живут ТОЛЬКО из-за G) исчезают.
### Дизайн
Третий канал в `SignalsFor`: `sustained[i]` = сглаженный интеграл `|kappa|` по окну ±W м (= суммарное
изменение heading). Длинная дуга R над arc L даёт heading-change L/R (Curva Grande ~0.78 рад); прямая ~0;
короткий пологий кинк — мало. Масштаб `SustainedScale` так, что настоящая быстрая дуга ≥ `ActiveThreshold`.
`fused[i] = Max(absK·180, gs, sustained[i]·SustainedScale)`. Все пороги — новые именованные const.
### Регрессионный контракт (КРИТИЧНО)
Канал меняет ПРОИЗВОДСТВЕННЫЙ `CornerCenterlineDetector` (только dev-time bake; рантайм грузит готовый
JSON — Monza/Spa вендоренные карты НЕ меняются). Калибровочный гейт (network-free unit-тест): прогнать
детектор на существующих owner-centerline Monza/Spa (у них ЕСТЬ per-bin LateralG) в двух режимах —
(1) G-intact + новый канал → должно совпасть с owner-baked `cornerGeometry.*.json` (не сломать); (2) G=0 +
новый канал → должен ВЕРНУТЬ monza_t03/spa_t02/spa_t16 (recovery ≥ порога). W/SustainedScale тюним против
этого оракула. Так же меряем apex-drift и extent-shrink и задаём численную приёмку для 12.

## B3. Corner geometry из ghost-centerline (`GhostImport bake-corners`)
`CornerCenterlineDetector.Detect(ghostCenterline, perLapGhostCenterlines)` (curvature+sustained, G=0) →
`CornerGeometryDocument.FromDetected` → `cornerGeometry.<track>.json`. **Апекс = геометрический ЦЕНТР
extent** (`BuildCorner apexIdx=(start+end)/2`); radius/trigger — из тончайшей точки. На ghost-картах
`Trigger=Curvature`, `PeakLateralG=0` у всех (byLoad не срабатывает) — фиксируем в ADR-0022 как намеренное
отличие от owner-baked.

## B4. cornerNames.json (SimCoach.Coach) — авторинг RU-имён (OD-B1 полнота)
Имена живут в `src/SimCoach.Coach/Data/cornerNames.json` (ДРУГАЯ сборка!), ключ
`{trackId:{trackId_tNN:{name,short}}}`; сейчас только monza+spa. Без них 12 трасс → `CornerNameForms.Positional`
→ «поворот N». Corner-id = порядок детекта (`trackId_tNN`), НЕ семантика. **Авторить строго против
БАКНУТЫХ id ПОСЛЕ детекта** (через `CornerGeometryReviewPage`), не из знания трассы заранее — иначе «Copse»
сядет на артефакт/не тот апекс и пройдёт count-only. Ре-бейк трассы инвалидирует имена. Коммит на трассу.

## B5. Alien lines все 14 (`GhostImport import`, как есть)
Против каждой centerline (Monza/Spa — своя M38; 12 — ghost-derived B1) → `alien_line.<track>.parquet`,
dry-gate (OD12) без изменений. Guard median-deviation ≤ ceiling — на ghost-трассах ИНФОРМАЦИОННЫЙ
(self-referential, OD-B3). Трасса без usable loop / за ceiling — **явно skip+list** в PR.

## B6. Vendor + regression
Копировать `centerline`/`cornerGeometry`/`alien_line` в `src/SimCoach.Reference/Data/` (по трассе или пачкой,
каждый green). Regression: `Load()` резолвит по одной новой трассе каждого типа. `git check-ignore -v` на
каждый вендоренный файл (вместо CultureInfo-перепроверки).

## ADR-0022 — ghost-derived reference
Провенанс: для 12 трасс centerline = медиана ЧУЖИХ быстрых кругов (accreplay), не своих; G=0; corner-detect
через curvature+sustained-integral (не G). Приватность: только агрегат, никогда .ghost/имена. Дегенеративные
поля на ghost-картах: `Trigger=Curvature`, `PeakLateralG=0`. 2 м alignment-guard — информационный на
ghost-трассах (реальные бэкстопы: coherence + calibration + corner-layout). Отличие от owner-baked Monza/Spa.

## Порядок dev-time генерации (сетевой, вне CI) — ПЕРЕУПОРЯДОЧЕН
На трассу X: `bake-centerline --track X` → **скопировать `centerline.X.json` в Data/** (import резолвит через
embedded `Load()`, rebuild авто на `dotnet run` т.к. project-ref) → `import --track X` → `bake-corners --track X`
→ скопировать `cornerGeometry.X.json` → авторить `cornerNames` против review-page → калибровочная приёмка →
если прошла, вендор 3 ассета; иначе skip+list.

## Разбивка на коммиты (task=commit, abort-on-non-green)
0. `feat(ghostimport): transcribe 12 accreplay trackIds`.
1. `feat(ghostimport): GhostRecord→TelemetryFrame adapter` + тесты.
2. `feat(ghostimport): ghost-median centerline builder w/ bootstrap axis (bake-centerline)` + тесты.
3. `feat(reference): curvature-integral sustained-bend channel + Monza/Spa calibration gate` + тесты.
4. `feat(ghostimport): corner detection from ghost centerline (bake-corners)` + тесты.
5. `docs(adr): ADR-0022 ghost-derived reference`.
6. dev-time генерация → `feat(reference): vendor <track> centerline+corners+alien+names` (по трассе, green).
7. `test(reference): Load() resolves a ghost-vendored track`.

## Приёмка (OD-B2)
Калибровочный гейт (B2) задаёт численный per-track порог отклонения от owner-эталона (все tight-повороты
на месте + быстрые восстановлены каналом + count в пределах N). Трасса проходит → вендор; не проходит →
skip+list в PR. Track B «готов» = все прошедшие калибровку отгружены, провальные перечислены.

## Остаточные риски → адверсальное код-ревью на фазе реализации
- Тюнинг W/SustainedScale: не пере-детектит ли sustained-канал шиканы/эсы как один длинный поворот.
- Bootstrap-ось: устойчивость если самый быстрый ghost сам кривой (fallback на второй).
- Ghost resample rate: хватает ли точек записи на 1-метровый бин длинных трасс.
