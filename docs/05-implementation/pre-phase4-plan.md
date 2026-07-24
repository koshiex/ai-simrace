# План — «Добить всё до Phase 4»

Статус на 2026-07-17: Phase 3 закрыта (M1–M46, beyond-PB B1/B2/B3 влиты, #38 в main, в игре ок).
Этот план закрывает все хвосты ДО Phase 4 (Voice).

**Решения владельца (2026-07-17, зафиксированы):**
- **Centerlines для 12 недостающих трасс** = медиана из accreplay-ghost'ов (не заезды владельца).
- **lap_pb** = гейтить по побитию сохранённого PB (не session-best).

---

## Track A — Мелкие фиксы (1 маленький PR, off fresh main, сразу)

- **A1. lap_pb false «Личный рекорд».** Сейчас `isPb = clean && LapTimeMs < _runningBestMs` (session-best).
  Добавить условие: «Личный рекорд» только когда круг реально быстрее сохранённого PB
  (`deltaMs is not null && deltaMs < 0`). Иначе lap_pb не файрить (session-best без слова «рекорд»
  не озвучиваем — Lap-cadence и так есть debrief/catch_all). Файл: `ComputeSession.cs` (HandleLap,
  ~519). Тесты: (a) session-best но медленнее PB → НЕ lap_pb; (b) быстрее PB → lap_pb.
- **A2. Закоммитить KB-находки** уже в рабочем дереве: `session-log-forensics.md` (LINE-tier маркер,
  Gotcha 4 is_pb session-scoped, `references` reserved-word / `sessions.id`) + `INDEX.md`.

Объём: 1 файл кода + тесты + 2 KB. Прямой PR, без workflow.

---

## Track B — Мульти-трек reference из ghost'ов (главный пак, ultracode S→D→J)

**Цель:** alien-линии на все 14 GT3-трасс + centerlines на 12 недостающих, чисто из публичных
accreplay-данных (без заездов владельца, без имён пилотов — та же приватность, что B3).

**Переиспользуем (подтверждено рекогносцировкой):**
- `MedianCenterlineBuilder.Build(...)` — median из набора laps (position-first, MinLapsForTrust=3).
- `CornerCenterlineDetector` — детект углов из centerline (curvature + fused |lat G|).
- `AccTrackCatalog.TryGetLapLengthM` — lap length всех 14 трасс (и +11) уже в репо.
- `tools/SimCoach.GhostImport` — fetch / decode / lap-split / align / resample / seam-mask / persist.

### B1. Ghost-median centerline builder (новый путь в GhostImport)
- Fetch top-N GT3 ghost'ов/трек (N≈12), decode, lap-split, взять complete-loop лап от каждого
  (переиспользовать iterate-to-usable из B3 — топ-борда часто recon без замыкания петли).
- Resample каждый по arc-length → метровые бины; `MedianCenterlineBuilder` → `MedianCenterline`.
  Lap length из `AccTrackCatalog`.
- Coherence guard (≥3 usable ghost'а). Emit `centerline.<track>.json` (`CenterlineGeometryDocument`,
  SchemaVersion=1, LapCount=N, Bins).

### B2. Vendor centerlines 12 трасс
- Скопировать `centerline.<track>.json` в `src/SimCoach.Reference/Data/` (embed-glob уже покрывает;
  культурный фикс `LogicalName`/`WithCulture` уже на месте — среди 14 только `spa` была culture-collision,
  перепроверить весь набор).

### B3. Corner geometry из centerline (расширение — corner-level коучинг на всех трассах)
- Прогнать `CornerCenterlineDetector` на ghost-centerline → `cornerGeometry.<track>.json` + track model.
- **РИСК:** детектор фузит median |lateral G|, а ghost-записи G НЕ несут (только world X/Z, brake,
  throttle, ts — см. `acc-ghost-format-re.md`). Нужно решить в blueprint: (a) curvature-only fallback
  в детекторе (R≤180 м без G-fusion), либо (b) отложить corner-geom, отгрузить сейчас только
  alien+centerline (тогда на 12 трассах — линия-подсказки без corner-tip'ов до отдельного захода).

### B4. Alien lines все 14
- Прогнать существующий import против каждой centerline (Monza/Spa — своя M38; 12 — ghost-derived) →
  `alien_line.<track>.parquet`. Dry-gate (OD12) применяется на рантайме как есть.
- Validate median-deviation ≤ ceiling (2 м) per track; трассы, что не проходят guard или без usable
  loop, — **явно залогировать и пропустить** (не тихо), список в PR.

### B5. Vendor alien lines + provenance
- Копировать `alien_line.<track>.parquet` в Data/; provenance без имени (как B3).

**Dev-time генерация** (сетевой fetch 14 треков) — ручной шаг вне CI, как для Monza/Spa.

### Открытые под-решения Track B (решить в blueprint / S→D→J)
- B3 corner-geometry: curvature-only fallback vs отложить corner-geom.
- N ghost'ов на трек (глубина median): 12? Порог usable.
- ADR: ghost-derived centerline меняет провенанс M38 (медиана чужих кругов вместо своих) → **ADR-0022**.
- Проверить нет ли ещё culture-collision среди 14 track-id (только `spa` подтверждена).

---

## Track C — Блокированное / пассивное (не автономно)

- **C1. M18 калибровка** (`MinDimensionScore`/`PassBar` RU-eval) — ✅ **СДЕЛАНО 2026-07-22** (6 живых
  прогонов судьи `anthropic/claude-sonnet-4.6` через `OPENROUTER_API_KEY`). Итог: пороги `PassBar=3.5` /
  `GroundednessFloor=3.0` / `MinDimensionScore=2.0` подтверждены данными — good-фикстуры composite ≥ 4.10
  (маржа ≥ 0.6), три known-bad анкора топят по одному измерению каждый (fabricated g=0, raw-number tone=1,
  transliteration ru=0) → per-dimension floor ловит с маржой ≥ 1.0. Флип `EnforceGoodFixtureBar=true` (гейт
  теперь блокирующий). Найденный при калибровке флап — live-дебриф протекал сырое «мс» в `top_priority` —
  починен в источнике (debrief-промпт правило 5), не понижением планки. См. [[rueval-gate-calibration]].
- **C2. M26 fire-rate** (BalanceKernels steady-state) — ⛔ **не делаем сейчас** (macOS-разработка, нет
  заездов ACC). «not empirically validated, watch in-game» — финалить нечего без телеметрии владельца.

---

## Порядок исполнения

1. **Track A** — сразу, маленький PR (решения приняты, низкий риск).
2. **Track B** — blueprint → S→D→J валидация плана → реализация (task=commit, abort-on-non-green) →
   adversarial код-ревью → dev-time генерация 14 треков → vendor → PR. Большой пак → **ultracode**.
3. **Track C** — по мере (ключ / заезды на владельце).

После Track A+B «всё до Phase 4» закрыто (C — внешне-блокировано). Дальше — Phase 4 (Voice/TTS).
