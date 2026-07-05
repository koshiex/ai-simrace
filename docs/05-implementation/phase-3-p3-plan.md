# План реализации Phase-3 P3 (SimCoach)

> ## Статус: ратифицировано владельцем (proto-раскладка + скоуп); второй S/D/J-проход (`wf_99b74134-345`) вложен, готово к имплементации
>
> Провенанс: планировочный workflow `wf_8fa62310-195` (scout ×4 → синтез → Strict/Defender/Judge),
> уточнён вторым проходом `wf_99b74134-345` (Strict/Defender/Judge) — его обязательные правки вложены (сводка ниже).
> Вердикт Judge — **revise**; спайн (M42/M43 прогрев → reference-модель → детекторы → диагностика потерь →
> обогащение дебрифа) подтверждён, внутренние противоречия proto-раскладки и графа задач сняты. Владелец
> ратифицировал **полный скоуп P3** (M33–M39, M41–M43; M40 уже перенесён в Phase-4/Voice) и финальную
> аддитивную proto-карту. Следующая фаза — только TTS, откладывать некуда: фаза несёт весь свой скоуп.
>
> **Применённые правки S/D/J-прохода (все 7):**
> 1. `M41-proto` больше не зависит от `M36-dominant` — `trend=12` добавляется над зарезервированным разрывом
>    6–11 без предшественника (AggregatedLoss сейчас заканчивается на поле 5); искусственное ребро убрано.
> 2. `M33` декомпозирован как `M34`: каждый из **трёх** детекторов = отдельный аддитивный proto-коммит +
>    kernel-коммит (+unit-тест) + coach-wiring-коммит. Контракт/логика/wiring не бандлятся, proto-правки не цепляются.
>    (Второй проход: lockup несёт **лишний** `M33-lockup-populate` в Reference — его kernel в Pipeline; см. ниже.)
> 3. `M43` лишён ярлыка «risk-free»; добавлен явный gate ре-валидации ground-truth (оракул 3929 мс «Curva
>    Grande», `docs/05-implementation/ground-truth-revalidation.md`) — **после** M43 и **до** любого
>    LINE-референс-изменения M34/M38. WorldPos-страж отгружается как есть; знаменатель `GridMetrics.Index`
>    унифицируется с ресемплером; решение по `TimeAt` (nearest-index подтверждён) свёрнуто в `M43-gridindex`
>    (второй проход — отдельной строки `M43-timeat` больше нет).
> 4. ADR-набор сведён к реальным решениям с отклонёнными альтернативами и перенумерован подряд от **0017**:
>    reference-снапшоты (M37), median-centerline LINE-vs-TIME (M38), AggregatedLoss policy + инвариант (M35/M36),
>    line-deviation retain-vs-deprecate-in-place поля 9 (M34; знак — inline-комментом в proto). Таксономия детекторов
>    M33 → в feature-док, не ADR. M42 → append-only addendum к ADR-0004, не новый ADR. M41 — без отдельного
>    ADR (grounding имён покрыт ADR-0010).
> 5. `M34` сжат до ~4 задач: чистый kernel над caller-supplied `[lo,hi]`-полосами (bands-хелпер внутрь
>    kernel-коммита), `M34-populate` владеет проводкой `CornerPhaseBands` (нет висящей kernel→bands зависимости),
>    architecture.md-абзац — внутрь ADR/feature-коммита. Единственное exit-действие формы ЛИНИИ
>    (`tighten_exit`/`open_exit`, относительно референса) — в registry-коммит M34; off-track остаётся за
>    существующим `ran_wide` (второй проход).
> 6. `M42`: статус DeepSeek — «ещё не зарегистрирован (config-gated by absence)», не «зарегистрирован-но-выключен»
>    (он присутствует только в тест-файле, не в `appsettings.json`).
> 7. Один PR на M-элемент; строка «M34+M38 одним PR» снята (превышает ~600-строчный потолок ревью).
>
> **Снятый скоуп-форк `track_width`:** отдельный детектор/поле `track_width` **дропнут**; его величина свёрнута
> в `exit_line_deviation_m` (поле 20, M34) как форма выходной ЛИНИИ относительно референса: `+` = шире выход,
> `−` = уже выход — **не** край трассы. Поля CornerEvent 24 нет. Проверено: существующая подсказка «держись ближе
> к апексу» — это действие `tighten_apex` от `racing_line_deviation_m` (поле 9, единый RMS-скаляр); M34 расщепляет
> ровно этот скаляр на знаковые entry/apex/exit — тот же механизм, а не новая геометрия (baked-геометрии кромки
> трассы нет; `world_pos` — позиция машины, не кромки). Off-track / за пределы остаётся за существующим действием
> `ran_wide` (`off_track==true`), M34 его не дублирует (второй проход).

> **Применённые правки второго S/D/J-прохода (`wf_99b74134-345`, все 10):**
> 1. `M43-gate` — merge-**precondition** на PR `M34-populate` и `M38-linedev` (не просто предок в DAG): чек-лист в
>    теле PR с LOCAL-фикстурным прогоном `GroundTruthRevalidationTests` (`SIMCOACH_GROUNDTRUTH_FIXTURE` + новый
>    `SIMCOACH_REQUIRE_GROUNDTRUTH`, при котором gate **падает**, а не скипается); зелёный-из-за-скипа в CI приёмкой
>    не считается. Владелец фикстуры (`truth.json` + MCAP `20260701-171602-738`) — владелец репо, off-repo.
> 2. `M34-kernel` зависит от `[ADR-0018, M43-gridindex]` (реальная code-зависимость `FracIndex`/`InterpWorldTangent`),
>    **не** от ground-truth-барьера; барьер `M43-gate` несёт `M34-populate` (мутирует shipping-математику линии).
> 3. `exit_line_deviation_m` (поле 20) — отклонение от ЛИНИИ-референса, **не** от края трассы; exit-действие сужено
>    до одной подсказки формы линии, вся формулировка «вся ширина / за пределы / off-track» убрана.
> 4. `DominantChannel` — **отдельная** функция над 4 abs-then-average каналами M35 (включая `line`), а не «согласован
>    с `ChooseReason`» (у того нет канала `line`); кросс-юнитная нормализация argmax — в ADR-0020; дебриф рендерит
>    один сигнал доминанты (канал поля 10), поле 5 `dominant_reason` — легаси-ярлык, рядом не рендерится.
> 5. Популяция `brake_lockup_score` (22) — в `CornerEventBuilder` отдельным коммитом `M33-lockup-populate`
>    (`feat(reference)`); поля 21/23 популируются в своих `feat(reference)`-kernel-коммитах — ни одна детекторная
>    популяция не садится в `feat(coach)`.
> 6. `M38-bake`/`M38-store` **сериализуют** существующий `CenterlineBin[]` (выход `MedianCenterlineBuilder`, ADR-0014)
>    в новый schema-versioned документ; новые типы — только слой персиста + embedded-loader, не второе представление
>    центрлайна.
> 7. Тест правдоподобности M35 расщеплён: always-true инвариант суммы (в CI) + отдельный skippable
>    completeness-probe (легитимный пробел детекции не красит билд).
> 8. `M43-timeat` удалён как отдельная строка — `TimeAt` (nearest-index, единственный вызов) подтверждается внутри
>    `M43-gridindex`.
> 9. `ADR-0018` переформулирован: retain-vs-**deprecate-in-place** поля 9 (RMS не выводится из побандовых значений);
>    номер 0018 сохранён, знак — inline в proto.
> 10. Асимметрия proto-гранулярности (M33 расщепляет proto-правки, M35/M36 бандлят с одним потребителем) помечена
>     как намеренная в секвенировании.

---

## Назначение и охват

P3 закрывает оставшиеся M-элементы Phase-3 — санкционированная фаза аддитивного изменения контракта
`telemetry.proto`. Скоуп полный, ничего не отложено: **M33, M34, M35, M36, M37, M38, M39, M41, M42, M43**
(M40 перенесён в Phase-4/Voice — в P3 не входит). Кластеры: reference-spine абсолютной траектории
(M34/M37/M38), полнота детекции (M33), диагностика потерь (M35/M36), обогащение Gold-контекста дебрифа
(M41), опциональное кэширование промпта (M39), быстрые правки корректности и документации (M42/M43).

Жёсткие правила фазы: одна задача = один conventional-commit (build+test+format зелёные); любое proto-поле —
только аддитивно (новые номера, без переиспользования/перенумерации); records/init-only, `IReadOnlyList`/
`IReadOnlyDictionary` на публичной поверхности, один публичный тип на файл, мутация — только внутри
`internal sealed` коллекторов; пороги — через `IOptions<T>` (неоднозначные user-tunable knobs → user-facing
tier, дефолты консервативные); исправления корректности — на стороне детекции (kernels/compute), не через
LLM; русский пользовательский текст — в `.resx`/prompt-ресурсы, идентификаторы и комментарии — английские;
`ComputeService` живёт в `SimCoach.Reference`.

## Сводная таблица proto-изменений (консолидированная, ратифицированная)

Текущие максимумы подтверждены по `src/SimCoach.Contracts/Schemas/telemetry.proto`:
**CornerEvent = 17** (`peak_brake_pct`), **AggregatedLoss = 5** (`dominant_reason`),
**SessionEvent = 18** (`end_tyre_wear_pct`). Раскладка ниже — единственная бесконфликтная и финальная.

| Сообщение | Поле | Тип | № | Элемент | Семантика |
|---|---|---|---|---|---|
| CornerEvent | entry_line_deviation_m | float | 18 | M34 | знаковое отклонение фазы входа; `+` = снаружи/шире линии-референса, `−` = внутри/уже |
| CornerEvent | apex_line_deviation_m | float | 19 | M34 | знаковое, та же конвенция |
| CornerEvent | exit_line_deviation_m | float | 20 | M34 | знаковое; `+` = шире выход, `−` = уже выход, относительно референсной ЛИНИИ (не края трассы); несёт бывший `track_width` |
| CornerEvent | brake_release_diff_m | float | 21 | M33 | ref-относительное; `−` = отпустил тормоз позже референса (зеркало знака `brake_point_diff_m`) |
| CornerEvent | brake_lockup_score | float | 22 | M33 | 0..1, self; пиковый отрицательный `slip_ratio` в фазе торможения, гейт по `abs_active` |
| CornerEvent | short_shift_score | float | 23 | M33 | 0..1, **ref-относительное**; self в более высокой передаче, чем референс, на той же позиции трассы |
| AggregatedLoss | avg_min_speed_deficit_kmh | float | 6 | M35 | усреднённая диагностика дефицита мин. скорости (policy: abs-then-average) |
| AggregatedLoss | avg_line_deviation_m | float | 7 | M35 | усреднённая диагностика отклонения от линии (abs-then-average) |
| AggregatedLoss | avg_brake_point_diff_m | float | 8 | M35 | усреднённая диагностика точки торможения (abs-then-average) |
| AggregatedLoss | avg_throttle_resume_diff_m | float | 9 | M35 | усреднённая диагностика возврата газа (abs-then-average) |
| AggregatedLoss | dominant_channel | string | 10 | M36 | закрытый набор `min_speed`\|`line`\|`brake_point`\|`throttle_resume` |
| AggregatedLoss | dominant_channel_value | float | 11 | M36 | величина доминирующего канала |
| AggregatedLoss | trend | LossTrend | 12 | M41 | тренд потерь по повороту; закреплён за 12 над разрывом 6–11 |
| SessionEvent | balance_profile | BalancePhaseTrend | 19 | M41 | пофазный баланс машины (вход/середина/выход) |
| SessionEvent | sector_membership | repeated SectorCornerMembership | 20 | M41 | привязка сектор→повороты (только `corner_ids`) |

Новые типы (M41):

- `message SectorCornerMembership { int32 sector_idx = 1; repeated string corner_ids = 2; }` — человекочитаемые
  имена резолвятся в Coach (ADR-0010), не в compute.
- `message BalancePhaseTrend { float entry_trend = 1; float mid_trend = 2; float exit_trend = 3; }` — каждое
  `−1..1`, отрицательное = оверстир (конвенция `understeer_trend`, SessionEvent поле 11).
- `enum LossTrend { LOSS_TREND_UNSPECIFIED = 0; LOSS_TREND_IMPROVING = 1; LOSS_TREND_STABLE = 2; LOSS_TREND_WORSENING = 3; }`.

Свободные номера после пакета: **CornerEvent 24** (после дропа `track_width`), **AggregatedLoss 13**,
**SessionEvent 21**. Без изменений: TelemetryFrame (43), SectorEvent (5), LapEvent (8), CornerLoss (3),
StintSummary (5).

## Пораздельный разбор

### M42 — устранение дрейфа документации + guard схемы (S)

- **Точки касания:** `docs/03-functional/functional-requirements.md` (FR-061:81 → слаг `anthropic/claude-sonnet-4.6`,
  DeepSeek «ещё не зарегистрирован — config-gated by absence»; FR-014:28, FR-060:80, FR-072:91 — заметки о
  P3-дивергенциях); `docs/02-architecture/adr/0004-*` (датированный append-only addendum, без переписывания
  Decision); `tests/SimCoach.LLM.Tests/OpenRouterProviderTests.cs` (консолидированный `[Theory]/[InlineData]`
  per-family fixture над реальным `OpenRouterProvider` через `MockHttpMessageHandler`).
- **Задачи=коммиты:** (1) `M42-docs` — согласование FR/ADR-0004 addendum; (2) `M42-test` — per-family
  schema-acceptance.
- **proto:** нет.
- **Риски:** ADR-0004 — append-only (не переписывать Decision); слаг OpenRouter (`claude-sonnet-4.6`, точка) не
  «исправлять» на канонический `claude-sonnet-4-6` (это 404 и ломает `FamilyOf`); DeepSeek фигурирует только в
  тест-файле, не в `appsettings.json` — формулировка «config-gated by absence», не «registered-but-gated-OFF»;
  тест — только no-network lane (`MockHttpMessageHandler`).

### M43 — латентные правки корректности compute + gate ground-truth (S–M)

- **Точки касания:** `src/SimCoach.Reference/CornerEventBuilder.cs:196–212` (RacingLineDeviation — пропуск кадров
  `WorldPos==null` или `(0,0,0)`-сентинела, ссылка на `AccFrameMapper.cs:116`); `src/SimCoach.Reference/GridMetrics.cs:15–48`
  (единый знаменатель через `PositionNormalized`, новый `FracIndex`; `TimeAt` остаётся nearest-index — решение
  сворачивается в тот же коммит, единственный вызов терпит); call-sites `CornerEventBuilder.cs:86–87`,
  `ComputeSession.cs:290`; gate — `tests/SimCoach.Reference.Tests/GroundTruthRevalidationTests.cs` (оракул
  `truth.json`, фикстура `20260701-171602-738`, Monza; **новый** env-флаг `SIMCOACH_REQUIRE_GROUNDTRUTH` —
  fail-on-missing-fixture вместо skip).
- **Задачи=коммиты:** (1) `M43-worldpos` — WorldPos-страж (как есть); (2) `M43-gridindex` — унификация знаменателя
  `GridMetrics.Index` с ресемплером **+ подтверждение**, что `GridMetrics.TimeAt` остаётся nearest-index (единственный
  вызов `ComputeSession.cs:290` терпит — интерполяции для выпила нет; отдельной строки `M43-timeat` больше нет);
  (3) `M43-gate` — ре-валидация ground-truth-оракула (3929 мс «Curva Grande», +14799 мс S1). `M43-gate` — не просто
  предок в DAG, а зафиксированная **merge-precondition** на PR `M34-populate` и `M38-linedev` (см. риски и
  секвенирование).
- **proto:** нет.
- **Риски:** `M43-gridindex` сдвигает границы слотов → корнер/сектор-дельты на суб-мс, world-lookup на суб-метр —
  golden-числа сдвинутся; трактовать новые как корректный baseline и зафиксировать в теле коммита. Сохранить
  `k1<=k0`, `gridLength==0/1` и degenerate-guards. Ключ стража — `WorldPos==null` ИЛИ пара `(0,0,0)`, без
  расширения до порога расстояния; why-комментарий тянет к honest-zero конвенции `AccFrameMapper`. Gate —
  env-gated xUnit, чисто скипается без локальной фикстуры (CI зелёный, MCAP не входит в репо), поэтому зелёный
  CI-прогон **приёмкой не считается**: gate получает флаг `SIMCOACH_REQUIRE_GROUNDTRUTH`, при котором он **падает**
  (а не скипается) без фикстуры. PR `M34-populate` и `M38-linedev` обязаны приложить в теле локальный прогон с
  `SIMCOACH_GROUNDTRUTH_FIXTURE` + `SIMCOACH_REQUIRE_GROUNDTRUTH` (зелёным); фикстуру (`truth.json` + MCAP
  `20260701-171602-738`) держит владелец репозитория off-repo.
- **Почему рано:** чинит тот же `CornerEventBuilder`/`GridMetrics`, который расширяют M34/M38 — golden-перебаза
  случается один раз, до наслоения фич; но метрика `racing_line_deviation_m` NO-GO-сертифицирована, поэтому
  `M43-gate` — обязательная merge-precondition (локальный фикстурный прогон, записанный в теле PR) на
  `M34-populate` и `M38-linedev`, а не просто предшественник в графе (CI его прогнать не может).

### M37 — версионирование референсов вместо перезаписи (M) — FULL

- **Точки касания:** `src/SimCoach.Reference/ReferenceStore.cs:45–89` (MaybeUpdate: версионный путь снапшота +
  insert в history + upsert активного указателя; ctor получает `ReferenceSnapshotRepository`);
  `ReferenceTriple.cs:9–12` (SnapshotFileName/Directory через существующий Sanitize);
  `src/SimCoach.Storage/Repositories/Rows.cs` (`ReferenceSnapshotRow`); новый `ReferenceSnapshotRepository.cs`;
  новая миграция `006_reference_snapshots.sql` (DatabaseMigrator AssertContiguous 1..6); `ReferenceStorageOptions.cs`
  (`MaxSnapshotsPerTriple`, default keep-all); `TelemetryComposition.cs` (DI).
- **Задачи=коммиты:** (0) `ADR-0017`; (1) `M37-migration` — таблица+репозиторий+миграция 006; (2) `M37-snapshot`
  — снапшот вместо перезаписи; (3) `M37-retention` — knob ретенции.
- **proto:** нет (SQLite + Parquet).
- **Риски:** FK `source_session_id` — **ON DELETE SET NULL** (снапшот-строка переживает удаление сессии; сам
  Parquet-файл не под FK — cascade сессий не должен осиротить/ошибочно удалить файлы); рост диска (default
  keep-all безопасен для pre-alpha, отметить trade-off); path traversal — через Sanitize; существующие
  `ReferenceStoreTests` обновить (сейчас предполагают перезапись одного файла).

### M34 — знаковое пофазное отклонение от линии (L) — ~4 задачи

- **Точки касания:** `telemetry.proto` (CornerEvent 18/19/20); новый `SignedLineDeviation.cs` — **чистый** kernel
  над caller-supplied `[lo,hi]`-полосами (знаковый медианный перпендикуляр self vs reference world path в полосе;
  знак = `cross(refTangent, self−ref) × cornerTurnSign`), сюда же bands-хелпер `EntryApexExitBands`,
  переиспользующий `CornerPhaseBands.cs:47–52` Offsets(); `GridMetrics.cs` (InterpWorldTangent на базе
  исправленного `FracIndex` из M43); `CornerEventBuilder.cs:136–143,196–212` (populate: владеет проводкой
  `CornerPhaseBands`, режет 3 полосы, ставит Entry/Apex/Exit; RMS-поле 9 без изменений; все 3 — только на
  hasReference-ветке); Gold-слой `GoldCornerEvent.cs`/`GoldArtifactBuilder.cs`/`CornerGoldView.cs`/`GoldFieldNames.cs`;
  `actionRegistry.json` (пофазные действия формы ЛИНИИ относительно референса: вход `tighten_entry`/`open_entry`,
  апекс `tighten_apex`, выход — **одна** подсказка `tighten_exit`/`open_exit`, гейт по `|exit_line_deviation_m|`,
  знак выбирает текст: `+` = шире выход → `tighten_exit`, `−` = уже выход → `open_exit`; `requires_reference:true`,
  RU-шаблоны). Off-track / за пределы **не** трогаем — это существующее действие `ran_wide` (`off_track==true`);
  величина бывшего `track_width` живёт только как форма линии в знаке поля 20, без формулировок «вся ширина» /
  «за пределы» / off-track.
- **Задачи=коммиты:** (0) `ADR-0018`; (1) `M34-proto` — CornerEvent 18/19/20; (2) `M34-kernel` — чистый знаковый
  kernel над `[lo,hi]` + bands-хелпер (зависит от `M43-gridindex` за `FracIndex`/`InterpWorldTangent`, **не** от
  ground-truth-барьера — kernel чистый); (3) `M34-populate` — популяция builder (владеет `CornerPhaseBands`; несёт
  merge-precondition `M43-gate`, т.к. мутирует shipping-математику линии); (4) `M34-coach` — Gold-поля + пофазные
  registry-действия формы линии (одна exit-подсказка, без track-width/off-track).
- **proto:** CornerEvent 18/19/20.
- **Риски:** корректность знака требует направления поворота — сворачивать знак с turn-sign только на однозначном
  повороте, иначе fall-back и нейтрализация на плоских (это и есть гейт M38); `(0,0,0)`/null WorldPos отравляют
  offset — переиспользовать страж M43 в kernel; `M34-kernel` зависит от **кода** `M43-gridindex`
  (`FracIndex`/`InterpWorldTangent`), а barrier `M43-gate` несёт `M34-populate` (именно он мутирует LINE-математику
  на NO-GO-метрике → local-fixture прогон gate в теле PR); S/F-straddling — унаследованное ограничение
  `CornerPhaseBands` (приемлемо на ACC, отметить); знак-конвенция — inline-комментом в proto (не отдельный ADR);
  пороги — в `IOptions`, без магических чисел в kernel.

### M38 — медианная центральная линия как runtime LINE-референс + гейт по типу поворота (L)

- **Точки касания:** `tools/SimCoach.Bake/Program.cs:117` — **сериализовать уже строящийся** in-memory
  `MedianCenterline`/`CenterlineBin[]` (выход `MedianCenterlineBuilder.Build`, ADR-0014; тот же объект уже кормит
  `CornerCenterlineDetector`) в vendored `centerline.<trackId>.json` рядом с cornerGeometry — **без второй
  деривации/представления**; новые `CenterlineGeometryDocument.cs`/`CenterlineGeometryDataset.cs`/`CenterlineStore.cs`
  (зеркала `CornerGeometryDocument`/`CornerGeometryDataset`) — **только** слой персист-документа + embedded-loader
  над существующими `CenterlineBin`; `TrackModel.cs` + `CornerGeometryDataset.cs:64–71` (пробросить `ApexRadiusM`+Trigger,
  сейчас DROP-аются); `CornerEventBuilder.cs:88–143` (LINE ref = центральная линия для M34-полей и RMS; TIME ref
  = PB остаётся; PB-fallback без vendored центрлайна; нейтрализация полей при `Trigger==LateralG` или
  `ApexRadiusM>LineRelevanceMaxRadiusM`); `ComputeSession.cs:228–241`/`ComputeService.cs:31–64` (загрузка+проброс);
  `ComputeOptions.cs` (`LineRelevanceMaxRadiusM`, Tier-2); `TelemetryComposition.cs` (DI + embedded resource,
  проверить .gitignore-негацию).
- **Задачи=коммиты:** (0) `ADR-0019`; (1) `M38-bake` — сериализация существующего `CenterlineBin[]`
  (выход `MedianCenterlineBuilder`) в vendored schema-versioned/length-pinned документ; (2) `M38-cornermodel` —
  corner radius+channel в runtime Corner; (3) `M38-store` — persist-документ
  `CenterlineGeometryDocument`/`Dataset` + embedded-loader `CenterlineStore` (паттерн
  `CornerGeometryDocument`/`Dataset`, без нового представления линии); (4) `M38-linedev` — отклонение vs центрлайн
  + PB fallback; (5) `M38-gate` — гейт по типу поворота.
- **proto:** нет (`corner_radius_m` на провод не выносится — отклонённая альтернатива в ADR-0019).
- **Риски:** паритет систем координат (центрлайн — бины по метрам, PB ResampledLap — сетка 0..1; семплировать по
  нормализованной позиции консистентно); `M38-linedev` меняет LINE-референс на NO-GO-метрике → несёт
  merge-precondition `M43-gate` (local-fixture прогон в теле PR, `SIMCOACH_REQUIRE_GROUNDTRUTH`) и зависит от
  `M34-populate`; дрейф vendored-ассета (length-pinned + schema-versioned, пере-бейк при геометрии;
  трек без центрлайна → graceful PB fallback); покрытие (только `>=MinLapsForTrust(3)` чистых кругов дают
  доверенный центрлайн; до этого PB fallback); embedded-resource .gitignore trap — проверить `git check-ignore`.

### M33 — три недостающих детектора (L) — brake-release/short-shift по 3 коммита, lockup 4 (populate в Reference)

`track_width` дропнут (см. статус-баннер) — на провод идут три детектора. Таксономия детекторов
(reference-based vs self-only, аддитивность под ADR-0006) — в **feature-док M33**, не отдельный ADR.

- **Точки касания:** `telemetry.proto` (CornerEvent 21/22/23); `BrakeKernels.cs` (brake-release почти бесплатен —
  `BrakeProfile.BrakeOffPosition` уже есть); новый `BrakeLockupKernels.cs` (пиковый отрицательный `slip_ratio` в
  фазе торможения, гейт `abs_active` — ABS циклит slip, cold-start OK); новый `ShortShiftKernels.cs`
  (**ref-относительный**: self-передача выше передачи референса на той же позиции — референс `ResampledLap`
  несёт `Gear`, self raw-кадры несут `rpm`+`gear`; строится **так же, как существующий ref-относительный
  `tighten_apex`** — без rev-limit, без lap-scope per-gear peak-rpm прохода, без нового rpm-плюминга);
  `CornerEventBuilder.cs:61–73,101,138–143`; Gold-слой; `actionRegistry.json` (по одному действию на детектор,
  уникальные id/priority, RU); тесты kernel/builder.
- **Задачи=коммиты (proto-правки НЕ цепляются между собой; популяция `CornerEvent` — всегда в `feat(reference)`,
  никогда в `feat(coach)`):**
  - `M33-brakerelease-proto` (CornerEvent 21) → `M33-brakerelease-kernel` (`feat(reference)`: kernel +unit **и**
    популяция поля 21 в `CornerEventBuilder`) → `M33-brakerelease-coach` (Gold-поле + registry-действие);
  - `M33-lockup-proto` (CornerEvent 22) → `M33-lockup-kernel` (`feat(pipeline)`: `BrakeLockupKernels` +unit, гейт
    abs) → `M33-lockup-populate` (`feat(reference)`: популяция поля 22 в `CornerEventBuilder` + builder-тест —
    отдельный коммит, т.к. kernel живёт в Pipeline, а populate — в Reference) → `M33-lockup-coach`;
  - `M33-shortshift-proto` (CornerEvent 23) → `M33-shortshift-kernel` (`feat(reference)`: kernel +unit,
    ref-относительный, **и** популяция поля 23 в `CornerEventBuilder`) → `M33-shortshift-coach`.
- **proto:** CornerEvent 21/22/23 (по одному полю на proto-коммит, независимо).
- **Риски:** `short_shift` — ref-относительный (не self-эвристика), FP-профиль как у `tighten_apex`, не
  требует ратификации rpm-порога; `brake_lockup` — ABS-managed торможение даёт циклический отрицательный slip,
  обязательный гейт `abs_active`; не дублировать существующий `off_track` (`ran_wide`) по `tyres_out`; каждый
  proto-коммит регенерит Contracts — `dotnet format` после каждого; kernel-коммиты в Pipeline/Reference,
  coach-коммиты добавляют Gold-поле + одну registry-запись. `brake_lockup` несёт **лишний** populate-коммит
  (`M33-lockup-populate`, `feat(reference)`), т.к. его kernel — в Pipeline; brake-release/short-shift kernels уже
  `feat(reference)` и владеют своей populate в том же коммите — детекторная популяция никогда не садится в `feat(coach)`.

### M35 — диагностические скаляры потерь + always-true sum-инвариант + skippable probe полноты (L) — FULL

- **Точки касания:** `CornerEventBuilder.cs:13–14` (CornerContribution += `MinSpeedDiffKmh`/`RacingLineDeviationM`/
  `BrakePointDiffM`/`ThrottleResumeDiffM`), три call-site (80/96/147); `SessionLossAccumulator.cs:15–64` (running
  sums abs по каналам в internal sealed; only `DeltaMs>0`; avg = sum/SampleCount, policy **abs-then-average**);
  `telemetry.proto` (AggregatedLoss 6–9); Gold `GoldAggregatedLoss.cs`/`GoldArtifactBuilder.cs`; новый
  `LossSumInvariantTests.cs` (always-true CI-инвариант: каждый `avg_*` = abs-then-average своих по-корнерных диффов);
  отдельный skippable `LossCompletenessProbeTests.cs` (fixture-anchored detection-completeness probe).
- **Задачи=коммиты:** (0) `ADR-0020`; (1) `M35-contribution` — refactor CornerContribution (чистый плюминг);
  (2) `M35-diagnostics` — proto 6–9 + accumulator + always-true sum-инвариант (CI) + отдельный skippable
  completeness-probe (диагностика полноты, билд не красит).
- **proto:** AggregatedLoss 6/7/8/9.
- **Риски:** CI-инвариант — **always-true** (`avg_*` равен abs-then-average своих диффов; стабилен, законно не
  падает); проверку «объясняют ли 4 прокси `delta_ms` в пределах epsilon» вынести в **отдельный** fixture-anchored
  skippable probe (как `M43-gate`) / reporting-assertion — это detection-completeness probe: mid-corner slowness,
  которую 4 прокси не ловят (её закрывает M34), — **легитимный** пробел покрытия, он **не** должен красить билд;
  abs-then-average (signed схлопнул бы противоположные диффы и обманул тест) — закреплено в ADR-0020;
  `GoldAggregatedLoss` — позиционная record, расширение рябит по фикстурам (обновить в том же коммите).

### M36 — доминирующий канал + число в дебрифе (L, на M35) — FULL

- **Точки касания:** `telemetry.proto` (AggregatedLoss 10–11); `SessionLossAccumulator.cs:44–72` — DominantChannel
  picker как **отдельная** функция над 4 abs-then-average каналами M35 `{min_speed, line, brake_point,
  throttle_resume}` (НЕ «согласован с `ChooseReason`»: `CornerEventBuilder.ChooseReason:220–243` — другой закрытый
  набор, в нём **нет** канала `line`); argmax сравнивает км/ч (поле 6) с метрами (7/8/9) → **обязательна**
  кросс-юнитная нормализация (per-channel веса / z-score / significance-relative), задокументированная в
  **ADR-0020**; Gold `GoldAggregatedLoss.cs`/`GoldArtifactBuilder.cs`; новый `ChannelGloss.cs` (RU + единицы,
  fail-closed, зеркало ReasonGloss); `CoachStrings.resx` (Channel_* + units); `DebriefTemplate.cs:22–30` —
  «why» / `top_priority` рендерит **один** сигнал доминанты: новый канал (поле 10) + значение (поле 11) + единица,
  замещая `dominant_reason` (поле 5).
- **Задачи=коммиты:** (1) `M36-dominant` — proto 10–11 + accumulator; (2) `M36-render` — рендер канала+числа в
  дебрифе.
- **proto:** AggregatedLoss 10/11.
- **Риски:** DominantChannel — **отдельная** функция (не `ChooseReason`: у того нет канала `line`) → per-channel
  unit-тест на **единицы** (`min_speed`=км/ч, `line`/`brake_point`/`throttle_resume`=метры) и на кросс-юнитную
  нормализацию argmax (ADR-0020); `dominant_reason` (поле 5) сохраняется только как легаси / self-only ярлык и
  **никогда** не рендерится рядом с полем 10 (дебриф показывает один сигнал доминанты); RU-текст только в `.resx`;
  `DebriefTemplate` — golden byte-stable, фикстуры обновить в том же коммите.

### M41 — grounded-обогащение дебрифа (L) — FULL, без отдельного ADR

- **Точки касания:** `telemetry.proto` (SessionEvent 19/20, AggregatedLoss 12, новые
  `SectorCornerMembership`/`BalancePhaseTrend`/`LossTrend`); `BalanceKernels.cs` (или новый `PhaseBalanceKernels`
  — пофазный scorer БЕЗ braking-гейта на входе/выходе, с `MinSteadyStateFrames`-полом); `CornerPhaseBands.cs`
  (срезы фаз); `CornerEventBuilder.cs` (пофазный баланс в CornerContribution); `ComputeSession.cs` (пофазные
  аккумуляторы, `_sectorCornerIds` в EmitSector по apex-позиции, LossTrend по номеру круга, BalancePhaseTrend в
  Complete); `SessionLossAccumulator.cs` (per-lap buckets → LossTrend); `ComputeOptions.cs` (трендовые пороги,
  min-frames); Gold — новые `GoldBalanceProfile.cs`/`GoldSectorMembership.cs`, `GoldSessionPayload.cs`,
  `GoldArtifactBuilder.cs:83–102` (resolve `corner_ids`→RU через CornerNameMap, установить SetupHint); новый
  `SetupHintDeriver.cs`; `DebriefTemplate.cs`; `coach.system.debrief.v1.ru.txt` (правило grounding имён из
  `sector_membership`/`aggregated_losses`); `CoachStrings.resx`; `CoachOptions.cs` (чувствительность setup_hint —
  user-facing tier).
- **Задачи=коммиты:** (1) `M41-proto` — SessionEvent 19/20 + AggregatedLoss 12 + новые типы/enum; (2) `M41-balance`
  — пофазный баланс → BalancePhaseTrend; (3) `M41-losstrend` — per-corner loss trend; (4) `M41-membership` —
  grounded sector→corner membership; (5) `M41-goldpayload` — trend/membership/balance в Gold session payload;
  (6) `M41-setuphint` — grounded setup_hint + grounding имён в дебрифе.
- **proto:** SessionEvent 19/20, AggregatedLoss 12, новые типы/enum. `M41-proto` **без предшественника** —
  `trend=12` добавляется над разрывом 6–11 (нет ребра на `M36-dominant`).
- **Риски:** пофазный баланс — сложнейшая часть (BalanceKernels намеренно гейтит steady-state; ослабление на
  входе/выходе рискует вернуть transfer-load bias — держать scale-free asymmetry + пол по кадрам); ACC часто без
  g-force → деградировать до brake/throttle-границ из `CornerPhaseBands`, не обнулять всё; LossTrend при 1–2
  кругах → `LOSS_TREND_UNSPECIFIED` (зеркало ConsistencyStddev-сентинела); sector↔corner на границе —
  детерминированно по apex-позиции, unit-тест на границу; setup_hint пороги — user-facing tier с консервативными
  дефолтами; Gold-determinism/privacy строгие — snake_case, null-dropped, без raw car_id; изменение промпта —
  сверить с RuEval-фикстурами. Отдельного ADR нет: grounding имён покрыт ADR-0010, механика — в feature-доке.

### M39 — опциональное кэширование статического system-префикса (M) — один off-by-default флаг

- **Точки касания:** `OpenRouterProvider.cs:76–99` (BuildBody: при opt-in и Anthropic-семействе — content-parts
  массив с `cache_control:{type:"ephemeral"}` на статическом префиксе; Gemini/OpenAI — plain string auto-cache;
  `directive.SystemInstruction` внутри кэшируемого префикса); `SchemaTranslatorSelector.cs` (FamilyOf гейт);
  `LlmRequest.cs` (+`SystemPromptSuffix` — retry-текст не мутирует префикс); `RouteOptions.cs`/`ResolvedRoute.cs`/
  `LlmRouter.cs` (`CacheSystemPrompt`, Tier-2, default off); `CoachService.cs:324,462,558–559` (retries ставят
  `SystemPromptSuffix`, не `SystemPrompt`); метеринг (LlmUsage/CostCalculator) — без изменений.
- **Задачи=коммиты:** (1) `M39-cacheflag` — единственная задача: `RouteOptions.CacheSystemPrompt` (default off) +
  стабильный кэшируемый префикс между ретраями (volatile retry-текст не выносится вперёд breakpoint).
- **proto:** нет.
- **Риски:** ~1150-токенный префикс **НИЖЕ Anthropic-минимума 2048** — `cache_control` = silent no-op до
  естественного роста префикса; **НЕ паддить промпт**; Gemini corner-route авто-кэширует; ценность в P3 —
  metering-readiness + zero-risk плюминг, не экономия; кэш prefix-exact — любой байт до breakpoint ломает;
  главный риск корректности — не двигать volatile-контент (retry-reminder, per-request Gold) впереди breakpoint.

## ADR к написанию (перенумерованы подряд от 0017)

Следующий свободный номер после существующего `0016` — `0017`. Набор сведён к реальным решениям с отклонёнными
альтернативами:

- **ADR-0017** — Reference snapshots vs in-place overwrite (M37): активный указатель `[references]` +
  history-таблица + retention/FK (ON DELETE SET NULL). Отклонено: перезапись одного файла.
- **ADR-0018** — Line-deviation: **retain-vs-deprecate-in-place** unsigned RMS `racing_line_deviation_m` (поле 9)
  теперь, когда знаковые пофазные 18/19/20 его дополняют (M34). Перенумерация исключена additive-only правилом, так
  что реальное решение — сохранить поле 9 или пометить deprecated; **сохранить**, т.к. RMS не выводится из
  побандовых значений 18/19/20. Знак-конвенция — inline в proto-комментариях, не в ADR.
- **ADR-0019** — Median centerline как runtime LINE-референс + compute-side гейт типа поворота (M38):
  runtime-расширение ADR-0014. Отклонено: `corner_radius_m` на проводе.
- **ADR-0020** — AggregatedLoss: policy агрегации **abs-then-average** + always-true sum-инвариант (M35) +
  **кросс-юнитная нормализация argmax** для DominantChannel M36 (per-channel веса / z-score / significance-relative,
  чтобы сравнивать км/ч с метрами). Отклонено: signed-агрегация (схлопывает противоположные диффы) и сырой argmax
  по смешанным единицам.
- **ADR-0004 addendum** (M42, **не новый ADR**) — append-only: пин `anthropic/claude-sonnet-4.6`, DeepSeek
  «ещё не зарегистрирован (config-gated by absence)», слаг vs каноничный id.

M33 (таксономия детекторов) и M41 (grounded-обогащение) — **без отдельных ADR**: M33 → feature-док, M41 →
feature-док + ADR-0010 (grounding имён в Coach).

## Порядок исполнения (таблица коммитов)

| # | Код | Элемент | Коммит | proto | Зависит от |
|---|---|---|---|---|---|
| 1 | M42-docs | M42 | docs(fr,adr): reconcile FR-061/ADR-0004 debrief default; DeepSeek config-gated by absence | нет | — |
| 2 | M42-test | M42 | test(llm): per-family schema-acceptance fixture (Sonnet-4.6 pre-pin guard) | нет | — |
| 3 | M43-worldpos | M43 | fix(compute): skip null/(0,0,0) WorldPos in RacingLineDeviation | нет | — |
| 4 | M43-gridindex | M43 | fix(compute): unify GridMetrics.Index denominator with resampler; confirm TimeAt stays nearest-index | нет | — |
| 5 | M43-gate | M43 | test(reference): ground-truth re-validation gate + SIMCOACH_REQUIRE_GROUNDTRUTH flag (3929ms Curva Grande) | нет | M43-worldpos, M43-gridindex |
| 6 | ADR-0017 | M37 | docs(adr): reference snapshots vs in-place overwrite | нет | — |
| 7 | M37-migration | M37 | feat(storage): reference_snapshots table + repo (migration 006, FK SET NULL) | нет | ADR-0017 |
| 8 | M37-snapshot | M37 | feat(reference): snapshot references instead of overwrite | нет | M37-migration |
| 9 | M37-retention | M37 | chore(reference): snapshot retention knob (MaxSnapshotsPerTriple) | нет | M37-snapshot |
| 10 | ADR-0018 | M34 | docs(adr): retain vs deprecate-in-place unsigned RMS field 9 | нет | — |
| 11 | M34-proto | M34 | feat(contracts): CornerEvent line-dev fields 18/19/20 | ДА | ADR-0018 |
| 12 | M34-kernel | M34 | feat(reference): pure signed per-phase line-deviation kernel over [lo,hi] | нет | ADR-0018, M43-gridindex |
| 13 | M34-populate | M34 | feat(reference): populate per-phase deviations in CornerEventBuilder | нет | M34-proto, M34-kernel, M43-gate ⟂ |
| 14 | M34-coach | M34 | feat(coach): per-phase line-shape Gold fields + registry actions (single exit tip, no track-width) | нет | M34-populate |
| 15 | ADR-0019 | M38 | docs(adr): median centerline runtime LINE reference + gating | нет | ADR-0018 |
| 16 | M38-bake | M38 | feat(bake): serialize existing MedianCenterline (CenterlineBin[]) to vendored asset | нет | ADR-0019 |
| 17 | M38-cornermodel | M38 | feat(reference): corner radius + channel into runtime Corner | нет | ADR-0019 |
| 18 | M38-store | M38 | feat(reference): centerline persist document + embedded-loader store | нет | M38-bake |
| 19 | M38-linedev | M38 | feat(reference): line deviation vs centerline + PB fallback | нет | M38-store, M34-populate, M43-gate ⟂ |
| 20 | M38-gate | M38 | feat(reference): gate line deviations by corner type | нет | M38-linedev, M38-cornermodel |
| 21 | M33-brakerelease-proto | M33 | feat(contracts): CornerEvent brake_release_diff_m=21 | ДА | — |
| 22 | M33-brakerelease-kernel | M33 | feat(reference): brake-release diff kernel + field-21 populate + unit test | нет | M33-brakerelease-proto, M43-gridindex |
| 23 | M33-brakerelease-coach | M33 | feat(coach): brake-release Gold field + registry action | нет | M33-brakerelease-kernel |
| 24 | M33-lockup-proto | M33 | feat(contracts): CornerEvent brake_lockup_score=22 | ДА | — |
| 25 | M33-lockup-kernel | M33 | feat(pipeline): brake-lockup detector kernel + unit test (abs-gated) | нет | M33-lockup-proto |
| 26 | M33-lockup-populate | M33 | feat(reference): populate CornerEvent.brake_lockup_score in CornerEventBuilder + builder test | нет | M33-lockup-kernel |
| 27 | M33-lockup-coach | M33 | feat(coach): brake-lockup Gold field + registry action | нет | M33-lockup-populate |
| 28 | M33-shortshift-proto | M33 | feat(contracts): CornerEvent short_shift_score=23 | ДА | — |
| 29 | M33-shortshift-kernel | M33 | feat(reference): short-shift reference-relative kernel + field-23 populate + unit test | нет | M33-shortshift-proto, M43-gridindex |
| 30 | M33-shortshift-coach | M33 | feat(coach): short-shift Gold field + registry action | нет | M33-shortshift-kernel |
| 31 | ADR-0020 | M35 | docs(adr): AggregatedLoss abs-then-average + sum-invariant + argmax normalization | нет | — |
| 32 | M35-contribution | M35 | refactor(reference): diagnostic diffs on CornerContribution | нет | ADR-0020 |
| 33 | M35-diagnostics | M35 | feat(contracts,reference): AggregatedLoss 6-9 + sum-invariant + skippable completeness probe | ДА | M35-contribution |
| 34 | M36-dominant | M36 | feat(contracts,reference): dominant channel 10-11 (distinct picker, cross-unit norm) | ДА | M35-diagnostics |
| 35 | M36-render | M36 | feat(coach): render channel+value in debrief (replaces dominant_reason) | нет | M36-dominant |
| 36 | M41-proto | M41 | feat(contracts): SessionEvent 19/20 + AggregatedLoss 12 + new msgs/enum | ДА | — |
| 37 | M41-balance | M41 | feat(reference): per-phase balance -> BalancePhaseTrend | нет | M41-proto |
| 38 | M41-losstrend | M41 | feat(reference): per-corner loss trend | нет | M41-proto |
| 39 | M41-membership | M41 | feat(reference): grounded sector->corner membership | нет | M41-proto |
| 40 | M41-goldpayload | M41 | feat(coach): trend/membership/balance in Gold session payload | нет | M41-balance, M41-losstrend, M41-membership |
| 41 | M41-setuphint | M41 | feat(coach): grounded setup_hint + debrief name grounding | нет | M41-goldpayload |
| 42 | M39-cacheflag | M39 | feat(llm): RouteOptions.CacheSystemPrompt (default off) + retry-stable prefix | нет | — |

**⟂ = merge-precondition, не DAG-предок.** `M43-gate` env-скипается в CI (MCAP off-repo), поэтому PR `M34-populate`
и `M38-linedev` мержатся только с приложенным в теле PR **локальным** прогоном `GroundTruthRevalidationTests`
(`SIMCOACH_GROUNDTRUTH_FIXTURE` + `SIMCOACH_REQUIRE_GROUNDTRUTH`=fail-on-missing, зелёным). 42 коммита: удалён
`M43-timeat` (свёрнут в `M43-gridindex`), добавлен `M33-lockup-populate` (populate поля 22 живёт в Reference, а не
в Pipeline-kernel/coach).

## Секвенирование и батчинг в PR

Порядок ведёт от изменений без контракта к контрактным и от reference-spine к потребителям:

1. **Прогрев** — M42 (docs+тест), M43 (**две** compute-правки: `M43-worldpos`, `M43-gridindex` со свёрнутым
   TimeAt-подтверждением) + **`M43-gate`**. M43 чинит тот же `CornerEventBuilder`/`GridMetrics`, что расширяют
   M34/M38 (golden-перебаза один раз). `M43-gate` — **не** просто предок в DAG, а зафиксированная
   **merge-precondition** на PR `M34-populate` и `M38-linedev`: CI-прогон gate env-скипается (MCAP off-repo),
   поэтому зелёный-из-за-скипа приёмкой не считается — оба PR обязаны приложить в теле локальный фикстурный прогон
   `GroundTruthRevalidationTests` с `SIMCOACH_GROUNDTRUTH_FIXTURE` + новым `SIMCOACH_REQUIRE_GROUNDTRUTH` (при
   котором gate падает, а не скипается; оракул 3929 мс «Curva Grande», +14799 мс S1). Фикстуру держит владелец
   репо off-repo.
2. **Reference-модель** — M37 (storage-only, независим), затем M34 (единственная proto-правка CornerEvent
   18–20; `M34-kernel` — чистый, зависит только от **кода** `M43-gridindex`, барьер `M43-gate` несёт
   `M34-populate`), затем M38 (runtime-надстройка, без proto; `M38-bake`/`M38-store` сериализуют существующий
   `CenterlineBin[]`). **M34 и M38 — отдельные PR** (совместно превышают ~600-строчный потолок ревью).
3. **Детекторы** — M33 (CornerEvent 21–23), три декомпозированных детектора, дизъюнктная ветка.
4. **Диагностика потерь** — M35 → M36 (AggregatedLoss 6–11).
5. **Обогащение дебрифа** — M41 (SessionEvent 19–20 + AggregatedLoss 12 + новые типы) — последним.
6. **M39** — без proto, без общих файлов — параллелится в любой момент.

**Батчинг:** один PR на M-элемент; ADR-коммит открывает ветку, контрактный коммит — первым внутри неё. M36
складывается поверх M35 отдельным PR (оба малые, под потолком). Строка «M34+M38 одним PR» снята.

**Гранулярность proto-коммитов (намеренная асимметрия, не рассинхрон):** M33 расщепляет каждую аддитивную
proto-правку в отдельный contracts-only коммит — **только** чтобы не цеплять три правки одного сообщения
`CornerEvent` подряд; а `M35-diagnostics` / `M36-dominant` бандлят proto + единственного потребителя одним
коммитом. Оба варианта дают зелёный build — это осознанный выбор.

## Решения владельца — закрыто (ратифицировано)

1. **proto-раскладка** — ратифицирована: CornerEvent 18–20 (M34) / 21–23 (M33); AggregatedLoss 6–9 (M35) /
   10–11 (M36) / 12 (M41); SessionEvent 19–20 (M41); новые `SectorCornerMembership`/`BalancePhaseTrend`/`LossTrend`.
   `track_width` (было 24) дропнут — его величина живёт как форма выходной ЛИНИИ в знаке `exit_line_deviation_m`
   (20), не как край трассы (off-track остаётся за `ran_wide`).
2. **Скоуп** — полный P3, ничего не отложено: M33–M39, M41–M43 (M40 в Phase-4/Voice). Следующая фаза — TTS-only.
3. **M35/M36** — в скоупе, FULL: диагностические скаляры 6–9, доминирующий канал+значение 10–11 (DominantChannel —
   отдельная функция над 4 каналами M35 с кросс-юнитной нормализацией, не `ChooseReason`), always-true sum-инвариант
   (CI) + отдельный skippable completeness-probe, канал+число в детерминированном дебрифе (замещает `dominant_reason`
   поля 5). Policy — **abs-then-average** (ADR-0020).
4. **M37** — FULL: `reference_snapshots` + репозиторий + миграция 006 + knob ретенции; FK **ON DELETE SET NULL**
   (снапшот-строка переживает удаление сессии; Parquet не под FK).
5. **M38** — vendored schema-versioned центрлайн + PB-fallback; `corner_radius_m` на провод не выносится.
6. **M33** — три детектора на проводе (`brake_release_diff_m`, `brake_lockup_score`, `short_shift_score`);
   brake-release/short-shift — по 3 коммита (kernel `feat(reference)` владеет и populate поля), lockup — 4
   (kernel в Pipeline → отдельный `M33-lockup-populate` в Reference → coach); `short_shift` — ref-относительный
   (как `tighten_apex`), rpm-порог не нужен; `track_width` дропнут.
7. **Config-tier** — M41 setup_hint, M34 line-action пороги, M33 coaching-гейты: неоднозначные → user-facing
   tier, консервативные дефолты.
8. **M39** — единственный флаг `RouteOptions.CacheSystemPrompt` (default off); признан silent no-op ниже
   Anthropic-2048 (~1150-токенный префикс), Gemini авто-кэширует; промпт не паддится; ценность — metering-readiness.
9. **ADR** — набор сведён и перенумерован подряд: 0017 (M37), 0018 (M34 retain-vs-deprecate-in-place поля 9),
   0019 (M38), 0020 (M35 policy/sum-инвариант + M36 нормализация argmax); addendum к 0004 (M42). M33/M41 — без
   отдельных ADR.
10. **Ground-truth gate** — `M43-gate` — merge-precondition (локальный фикстурный прогон с
    `SIMCOACH_REQUIRE_GROUNDTRUTH`, записанный в теле PR) на `M34-populate` и `M38-linedev`, а не только предок в
    графе: CI его env-скипает (MCAP off-repo), поэтому зелёный CI приёмкой не считается.
