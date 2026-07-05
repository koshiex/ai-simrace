# План реализации Phase-3 P3 (SimCoach)

> ## Статус плана: REVISE (по итогам S→D→J) — ждёт ратификации владельца
>
> Источник: планировочный workflow `wf_8fa62310-195` (scout ×4 → синтез → Strict/Defender/Judge).
> Вердикт Judge — **revise**: спайн верный (M42/M43 прогрев → reference-модель → детекторы → обогащение),
> но раскладка proto и граф задач имеют внутренние противоречия + переусложнение. Ниже — форки владельцу и
> правки плана; тело плана (синтез) сохранено под чертой как исходный материал, финализируется после локов.
>
> ### Форки для владельца (ревью, проверено по коду — рекомендация: отложить/урезать)
> Часть пунктов из «всё кроме M40» оказалась build-ahead:
> 1. **M35/M36 → отложить.** Беклог сам гейтит их (стр.155): «делать только если prompt-only M3/M17 окажется
>    недостаточен». M3-guard + M17 уже в проде, NO-GO снят (2/2). ADR-0021 + 4 задачи + 6 proto-номеров
>    (AggregatedLoss 6–11) до срабатывания гейта — спекуляция. Оставить только резерв номеров 6–12.
> 2. **M37 → отложить / кап на raw-parquet.** Историю снапшотов никто не читает (`IReferenceQueryRepository`
>    отдаёт только активную строку; impl отложена в P6/P7). Таблица+репо+ретенция — аудит без аудитора.
> 3. **M39 → отложить / свернуть в один off-by-default флаг.** ~1150-токенный префикс — silent no-op ниже
>    Anthropic-минимума 2048; debrief пиннут на Sonnet-4.6; Gemini corner-route авто-кэширует. Выигрыша в P3
>    нет, только metering-readiness.
> 4. **M33 детекторы → урезать провод.** `track_width`(24) непостроим (нет track-edge геометрии);
>    `short_shift`(23) без rpm в ResampledLap + corner-window builder → высокий FP. На провод только
>    решённые/вычислимые: `brake_release`(21), `brake_lockup`(22). short_shift — self-only без proto;
>    track_width — дроп до появления геометрии.
>
> Пост-урезки proto-раскладка чистая: CornerEvent 18–20 (M34) + 21–22 (M33); AggregatedLoss 12 (M41 trend);
> SessionEvent 19–20 (M41) + новые типы `SectorCornerMembership`/`BalancePhaseTrend`/`LossTrend`.
>
> ### Правки плана (не решения — применю после локов скоупа)
> - **M41-proto:** убрать dep `[M36-dominant]` → только `ADR-0022` (trend=12 добавляется над gap 6–11,
>   предшественник не нужен; AggregatedLoss сейчас 1–5).
> - **M33:** декомпозировать каждый детектор как M34 (proto-коммит / kernel-коммит+тест / coach-wiring) —
>   не бандлить контракт+логику+wiring в один коммит.
> - **M43:** снять ярлык «risk-free»; добавить gate ground-truth ре-валидации (3929мс Curva Grande oracle)
>   после M43 и **до** M34/M38 (меняют LINE-референс на сертифицированной NO-GO метрике).
> - **ADR-набор:** оставить 0017 (если M37 остаётся) / 0019; ADR-0018 сузить до «аддитив vs перенумерация»
>   (знак — inline-коммент в proto); 0020/0021 — в feature-доки/комменты, не отдельные ADR.
> - **M34:** ~4 задачи (kernel чистый над `[lo,hi]`, bands внутрь kernel, docs — в ADR).
> - **M42:** DeepSeek → «not yet registered (config-gated by absence)», не «registered-but-gated-OFF».
> - **Секвенирование:** один PR на M-элемент (снять «M34+M38 в один PR» — >600 строк ревью-потолка).
>
> Полный список findings S/D/J — в `wf_8fa62310-195` journal; сведены в required-changes Judge (12 пунктов).

---

## Назначение и охват

P3 закрывает оставшиеся M-элементы Phase-3 (M33–M43) — санкционированная фаза изменения контракта `telemetry.proto`.
Пакет объединяет четыре кластера: reference-spine абсолютной траектории (M34/M37/M38), полнота детекции
(M33/M35/M36), обогащение Gold-контекста дебрифа и кэширование промпта (M41/M39), быстрые правки корректности и
документации (M42/M43).

Жёсткие правила фазы: одна задача = один conventional-commit (build+test+format зелёные); любое proto-поле —
только аддитивно (новые номера, без переиспользования/перенумерации); records/init-only, `IReadOnlyList`/
`IReadOnlyDictionary` на публичной поверхности, мутация — только внутри `internal sealed`; пороги — через
`IOptions<T>`; исправления корректности — на стороне детекции (kernels/compute), не через LLM; русский
пользовательский текст — в `.resx`/prompt-ресурсы; `ComputeService` живёт в `SimCoach.Reference`.

## Сводная таблица proto-изменений (консолидированная, дедуплицированная)

Скауты независимо запросили пересекающиеся номера (M33 и M34 — оба на CornerEvent 18–20; M35 и M41 — оба на
AggregatedLoss 6). Ниже — единая бесконфликтная раскладка. Текущие максимумы подтверждены по файлу:
CornerEvent = 17, AggregatedLoss = 5, SessionEvent = 18.

| Сообщение | Поле | Тип | № | Элемент | Семантика |
|---|---|---|---|---|---|
| CornerEvent | entry_line_deviation_m | float | 18 | M34 | знаковое отклонение фазы входа; + = снаружи/шире линии-референса, − = внутри/уже |
| CornerEvent | apex_line_deviation_m | float | 19 | M34 | знаковое, та же конвенция |
| CornerEvent | exit_line_deviation_m | float | 20 | M34 | знаковое, та же конвенция |
| CornerEvent | brake_release_diff_m | float | 21 | M33 | ref-относительное; отриц. = отпустил тормоз позже референса (зеркало brake_point_diff_m) |
| CornerEvent | brake_lockup_score | float | 22 | M33 | 0..1, self; пиковый отрицательный slip_ratio в фазе торможения, гейт по brake/abs_active |
| CornerEvent | short_shift_score | float | 23 | M33 | 0..1, self-эвристика; переключение ниже пикового rpm передачи за круг |
| CornerEvent | track_width_usage | float | 24 | M33 | 0..1, self; прокси кербы/ширина трека (возможен descope/переименование в kerb_strike_score) |
| AggregatedLoss | avg_min_speed_deficit_kmh | float | 6 | M35 | усреднённая величина диагностики мин. скорости |
| AggregatedLoss | avg_line_deviation_m | float | 7 | M35 | усреднённая величина диагностики отклонения от линии |
| AggregatedLoss | avg_brake_point_diff_m | float | 8 | M35 | усреднённая величина диагностики точки торможения |
| AggregatedLoss | avg_throttle_resume_diff_m | float | 9 | M35 | усреднённая величина диагностики возврата газа (policy: abs-then-average) |
| AggregatedLoss | dominant_channel | string | 10 | M36 | закрытый набор min_speed\|line\|brake_point\|throttle_resume |
| AggregatedLoss | dominant_channel_value | float | 11 | M36 | усреднённая величина доминирующего канала |
| AggregatedLoss | trend | LossTrend | 12 | M41 | тренд потерь по повороту; закреплён за 12, чтобы не конфликтовать с 6–11 даже если M35/M36 отложены |
| SessionEvent | balance_profile | BalancePhaseTrend | 19 | M41 | пофазный баланс машины (вход/середина/выход) |
| SessionEvent | sector_membership | repeated SectorCornerMembership | 20 | M41 | привязка сектор→повороты (только corner_ids) |

Новые типы (M41):

- `message SectorCornerMembership { int32 sector_idx = 1; repeated string corner_ids = 2; }` — человекочитаемые имена резолвятся в Coach (ADR-0010), не в compute.
- `message BalancePhaseTrend { float entry_trend = 1; float mid_trend = 2; float exit_trend = 3; }` — каждое −1..1, отрицательное = оверстир (конвенция understeer_trend, SessionEvent поле 11).
- `enum LossTrend { LOSS_TREND_UNSPECIFIED = 0; LOSS_TREND_IMPROVING = 1; LOSS_TREND_STABLE = 2; LOSS_TREND_WORSENING = 3; }`.

Свободные номера после пакета: CornerEvent 25, AggregatedLoss 13, SessionEvent 21. Без изменений: TelemetryFrame (43),
SectorEvent (5), LapEvent (8), CornerLoss (3), StintSummary (5).

## Порядок исполнения (таблица коммитов)

| # | Код | Элемент | Коммит | proto | Зависит от |
|---|---|---|---|---|---|
| 1 | M42-docs | M42 | docs(fr,adr): reconcile FR-061/ADR-0004 debrief default, DeepSeek gated OFF | нет | — |
| 2 | M42-test | M42 | test(llm): per-family schema-acceptance fixture (Sonnet-4.6 pre-pin guard) | нет | — |
| 3 | M43-worldpos | M43 | fix(compute): skip null/(0,0,0) WorldPos in RacingLineDeviation | нет | — |
| 4 | M43-gridmetrics | M43 | fix(compute): unify GridMetrics mapping + interpolate TimeAt | нет | — |
| 5 | ADR-0017 | M37 | docs(adr): reference snapshots vs in-place overwrite | нет | — |
| 6 | M37-migration | M37 | feat(storage): reference_snapshots table + repo (migration 006) | нет | ADR-0017 |
| 7 | M37-snapshot | M37 | feat(reference): snapshot references instead of overwrite | нет | M37-migration |
| 8 | M37-retention | M37 | chore(reference): snapshot retention knob | нет | M37-snapshot |
| 9 | ADR-0018 | M34 | docs(adr): phase-segmented signed line-deviation | нет | — |
| 10 | M34-proto | M34 | feat(contracts): CornerEvent line-dev fields 18/19/20 | ДА | ADR-0018 |
| 11 | M34-bands | M34 | feat(pipeline): EntryApexExitBands helper | нет | ADR-0018 |
| 12 | M34-kernel | M34 | feat(reference): signed per-phase line-deviation kernel | нет | M43-gridmetrics, M43-worldpos |
| 13 | M34-populate | M34 | feat(reference): populate per-phase deviations in CornerEventBuilder | нет | M34-proto, M34-bands, M34-kernel |
| 14 | M34-gold | M34 | feat(coach): per-phase line-deviation Gold fields | нет | M34-populate |
| 15 | M34-registry | M34 | feat(coach): per-phase line actions in registry | нет | M34-gold |
| 16 | M34-docs | M34 | docs(architecture): phase-segmented signed line deviation | нет | M34-registry |
| 17 | ADR-0019 | M38 | docs(adr): median centerline runtime LINE reference + gating | нет | ADR-0018 |
| 18 | M38-bake | M38 | feat(bake): vendored median-centerline asset | нет | ADR-0019 |
| 19 | M38-cornermodel | M38 | feat(reference): corner radius + channel into runtime Corner | нет | ADR-0019 |
| 20 | M38-store | M38 | feat(reference): centerline dataset + store | нет | M38-bake |
| 21 | M38-linedev | M38 | feat(reference): line deviation vs centerline + PB fallback | нет | M38-store, M34-populate |
| 22 | M38-gate | M38 | feat(reference): gate line deviations by corner type | нет | M38-linedev, M38-cornermodel |
| 23 | ADR-0020 | M33 | docs(adr): detection-kernel taxonomy + additive scalars | нет | — |
| 24 | M33-brakerelease | M33 | feat: brake-release diff (CornerEvent 21) | ДА | ADR-0020, M34-proto |
| 25 | M33-lockup | M33 | feat: brake-lockup detector (CornerEvent 22) | ДА | M33-brakerelease |
| 26 | M33-shortshift | M33 | feat: short-shift detector (CornerEvent 23) | ДА | M33-lockup |
| 27 | M33-trackwidth | M33 | feat: kerb/track-width detector (CornerEvent 24) | ДА | M33-shortshift |
| 28 | ADR-0021 | M35 | docs(adr): AggregatedLoss diagnostics + plausibility invariant | нет | — |
| 29 | M35-contribution | M35 | refactor(reference): diagnostic diffs on CornerContribution | нет | ADR-0021 |
| 30 | M35-diagnostics | M35 | feat(contracts,reference): AggregatedLoss 6-9 + strict test | ДА | M35-contribution |
| 31 | M36-dominant | M36 | feat(contracts,reference): dominant channel 10-11 | ДА | M35-diagnostics |
| 32 | M36-render | M36 | feat(coach): render channel+number in debrief | нет | M36-dominant |
| 33 | ADR-0022 | M41 | docs(adr): grounded debrief enrichment | нет | ADR-0021 |
| 34 | M41-proto | M41 | feat(contracts): SessionEvent 19/20 + AggregatedLoss 12 + new msgs | ДА | ADR-0022, M36-dominant |
| 35 | M41-balance | M41 | feat(reference): per-phase balance -> BalancePhaseTrend | нет | M41-proto |
| 36 | M41-losstrend | M41 | feat(reference): per-corner loss trend | нет | M41-proto |
| 37 | M41-membership | M41 | feat(reference): grounded sector->corner membership | нет | M41-proto |
| 38 | M41-goldpayload | M41 | feat(coach): trend/membership/balance in Gold session payload | нет | M41-balance, M41-losstrend, M41-membership |
| 39 | M41-setuphint | M41 | feat(coach): grounded setup_hint + debrief name grounding | нет | M41-goldpayload |
| 40 | M39-breakpoint | M39 | feat(llm): cache static prefix via cache_control breakpoint | нет | — |
| 41 | M39-retrystable | M39 | feat(llm): stable cached prefix across retries | нет | M39-breakpoint |

## Пораздельный разбор

### M42 — устранение дрейфа документации + guard схемы (S)

- **Точки касания:** `docs/03-functional/functional-requirements.md` (FR-061:81 → слаг `anthropic/claude-sonnet-4.6`, DeepSeek gated OFF; FR-014:28, FR-060:80, FR-072:91 — заметки о P3-дивергенциях); `docs/02-architecture/adr/0004-*` (датированный addendum, без переписывания Decision); `tests/SimCoach.LLM.Tests/OpenRouterProviderTests.cs` (консолидированный `[Theory]/[InlineData]` per-family fixture над реальным OpenRouterProvider через MockHttpMessageHandler).
- **Задачи:** (1) docs-коммит согласования FR/ADR; (2) test-коммит per-family schema-acceptance.
- **proto:** нет.
- **Риски:** ADR-0004 — append-only (не переписывать Decision); слаг OpenRouter (`claude-sonnet-4.6`, точка) не «исправлять» на канонический `claude-sonnet-4-6` (это 404 и ломает FamilyOf); тест — только no-network lane (MockHttpMessageHandler).

### M43 — латентные правки корректности compute (S–M)

- **Точки касания:** `src/SimCoach.Reference/CornerEventBuilder.cs:196–212` (RacingLineDeviation — пропуск кадров `WorldPos==null` или `(0,0,0)`-сентинела, ссылка на `AccFrameMapper.cs:116`); `src/SimCoach.Reference/GridMetrics.cs:15–48` (единый знаменатель через `PositionNormalized`, новый `FracIndex`, интерполяция `TimeAt`); call-sites `CornerEventBuilder.cs:86–87`, `ComputeSession.cs:290`.
- **Задачи:** (1) страж WorldPos; (2) унификация маппинга позиция↔сетка + интерполяция TimeAt.
- **proto:** нет.
- **Риски:** задача (b) сдвигает границы слотов → корнер/сектор-дельты на суб-мс, world-lookup на суб-метр — golden-числа сдвинутся; трактовать новые как корректный baseline и зафиксировать в теле коммита. Сохранить `k1<=k0`, `gridLength==0/1` и degenerate-guards. Ключ стража — `WorldPos==null` ИЛИ пара `(0,0,0)`, без расширения до порога расстояния; why-комментарий тянет к honest-zero конвенции AccFrameMapper.
- **Почему рано:** чинит тот же CornerEventBuilder/GridMetrics, который расширяют M34/M38 — golden-перебаза случается один раз, до наслоения фич.

### M37 — версионирование референсов вместо перезаписи (M)

- **Точки касания:** `src/SimCoach.Reference/ReferenceStore.cs:45–89` (MaybeUpdate: версионный путь снапшота + insert в history + upsert активного указателя; ctor получает `ReferenceSnapshotRepository`); `ReferenceTriple.cs:9–12` (SnapshotFileName/Directory через существующий Sanitize); `src/SimCoach.Storage/Repositories/Rows.cs` (`ReferenceSnapshotRow`); новый `ReferenceSnapshotRepository.cs`; новая миграция `006_reference_snapshots.sql` (DatabaseMigrator AssertContiguous 1..6); `ReferenceStorageOptions.cs` (Tier-2 `MaxSnapshotsPerTriple`, default keep-all); `TelemetryComposition.cs` (DI).
- **Задачи:** (1) таблица+репозиторий+миграция; (2) снапшот вместо перезаписи; (3) knob ретенции.
- **proto:** нет (SQLite + parquet).
- **Риски:** FK `source_session_id` — ON DELETE SET NULL (снапшот-строка переживает удаление сессии; сам parquet не под FK, cascade сессий не должен осиротить/ошибочно удалить файлы); рост диска (default keep-all безопасен для pre-alpha, но отметить trade-off); path traversal — через Sanitize; существующие ReferenceStoreTests обновить (сейчас предполагают перезапись одного файла).

### M34 — знаковое пофазное отклонение от линии (L)

- **Точки касания:** `telemetry.proto` (CornerEvent 18/19/20); `CornerPhaseBands.cs:47–52` (EntryApexExitBands, переиспользовать Offsets() чтобы не дублировать единое определение apex); `GridMetrics.cs` (InterpWorldTangent на базе исправленного FracIndex из M43); новый `SignedLineDeviation.cs` (знаковый медианный перпендикуляр self vs reference world path в полосе; знак = cross(refTangent, self−ref) × cornerTurnSign); `CornerEventBuilder.cs:136–143,196–212` (3 полосы, срезы selfSpan, установка Entry/Apex/Exit; RMS поле 9 без изменений; все 3 — только на hasReference-ветке); Gold-слой `GoldCornerEvent.cs`/`GoldArtifactBuilder.cs`/`CornerGoldView.cs`/`GoldFieldNames.cs`; `actionRegistry.json` (пофазные действия tighten_entry/open_entry/run_wider_exit/tighten_exit, `requires_reference:true`, RU-шаблоны); docs.
- **Задачи:** (1) proto; (2) EntryApexExitBands; (3) знаковый kernel; (4) популяция builder; (5) Gold-поля; (6) registry-действия; (7) docs.
- **proto:** CornerEvent 18/19/20 (см. таблицу).
- **Риски:** корректность знака требует направления поворота — сворачивать знак с turn-sign только на однозначном повороте, иначе fall-back и нейтрализация на плоских (это и есть гейт M38); `(0,0,0)`/null WorldPos отравляют offset — переиспользовать страж M43 в новом kernel; S/F-straddling — унаследованное ограничение CornerPhaseBands (приемлемо на ACC, отметить); пороги — в IOptions, без магических чисел в kernel.

### M38 — медианная центральная линия как runtime LINE-референс + гейт по типу поворота (L)

- **Точки касания:** `tools/SimCoach.Bake/Program.cs:117–128` (сериализовать vendored `centerline.<trackId>.json` рядом с cornerGeometry); новые `CenterlineGeometryDocument.cs`/`CenterlineGeometryDataset.cs`/`CenterlineStore.cs` (зеркала CornerGeometry*); `TrackModel.cs` + `CornerGeometryDataset.cs:64–71` (пробросить ApexRadiusM+Trigger, сейчас DROP-аются); `CornerEventBuilder.cs:88–143` (LINE ref = центральная линия для M34-полей и RMS; TIME ref = PB остаётся; PB-fallback без vendored центрлайна; нейтрализация полей при Trigger==LateralG или ApexRadiusM>LineRelevanceMaxRadiusM); `ComputeSession.cs:228–241`/`ComputeService.cs:31–64` (загрузка+проброс); `ComputeOptions.cs` (LineRelevanceMaxRadiusM, Tier-2); `TelemetryComposition.cs` (DI + embedded resource, проверить .gitignore-негацию).
- **Задачи:** (1) bake-ассет; (2) corner radius+channel в runtime Corner; (3) dataset+store; (4) отклонение vs центрлайн + PB fallback; (5) гейт по типу поворота.
- **proto:** нет (TIME-семантика PB и корректность отрасли сохранены; corner_radius_m на провод не выносится — отклонённая альтернатива в ADR-0019).
- **Риски:** паритет систем координат (центрлайн — бины по метрам, PB ResampledLap — сетка 0..1; семплировать по нормализованной позиции консистентно); дрейф vendored-ассета (length-pinned + schema-versioned, пере-бейк при геометрии; трек без центрлайна → graceful PB fallback); покрытие (только >=MinLapsForTrust(3) чистых кругов дают доверенный центрлайн; до этого PB fallback); embedded-resource .gitignore trap — проверить git check-ignore.

### M33 — четыре недостающих пофазных детектора (L)

- **Точки касания:** `telemetry.proto` (CornerEvent 21–24); `BrakeKernels.cs` (brake-release почти бесплатен — BrakeProfile.BrakeOffPosition уже есть); новые `BrakeLockupKernels.cs` (пиковый отрицательный slip в фазе торможения, гейт abs_active), `ShortShiftKernels.cs` (gear+rpm, lap-relative peak), `TrackWidthKernels.cs` (tyres_out+world_pos, самый неуверенный); `CornerEventBuilder.cs:61–73,101,138–143`; Gold-слой; `actionRegistry.json` (по одному действию на детектор, уникальные id/priority, RU); тесты kernel/builder/Gold/registry.
- **Задачи:** (0) ADR-0020; (1) brake-release; (2) lockup; (3) short-shift; (4) track-width.
- **proto:** CornerEvent 21/22/23/24 (каждый slice добавляет одно поле — одно логическое изменение).
- **Риски:** short-shift без референса (ResampledLap несёт Gear, но не rpm — только self lap-relative эвристика, оптимальный rpm car-specific → возможны FP, порог на ратификацию владельцу); track-width слабейший (нет track-edge геометрии — реалистичный минимум это kerb-strike/track-limits прокси, кандидат на descope); не дублировать существующий off_track (ran_wide) по tyres_out; ABS-managed торможение может давать циклический отрицательный slip — гейтить; каждый коммит регенерит Contracts — dotnet format после каждого.

### M35 — диагностические скаляры потерь + инвариант правдоподобности (L)

- **Точки касания:** `CornerEventBuilder.cs:13–14` (CornerContribution += MinSpeedDiffKmh/RacingLineDeviationM/BrakePointDiffM/ThrottleResumeDiffM), три call-site (80/96/147); `SessionLossAccumulator.cs:15–64` (running sums abs по каналам в internal sealed; only DeltaMs>0; avg = sum/SampleCount, policy abs-then-average); `telemetry.proto` (AggregatedLoss 6–9); Gold `GoldAggregatedLoss.cs`/`GoldArtifactBuilder.cs`; новый `LossPlausibilityInvariantTests.cs`.
- **Задачи:** (0) ADR-0021; (1) refactor CornerContribution (чистый плюминг); (2) proto/accumulator/strict-test.
- **proto:** AggregatedLoss 6/7/8/9.
- **Риски:** инвариант может законно упасть (mid-corner slowness не ловится 4 прокси — это указывает на M34, а не на баг теста; рамка epsilon должна ЭКСПОНИРОВАТЬ пробел); abs-then-average (не signed — signed схлопнёт противоположные диффы и обманет тест) — закрепить в ADR; GoldAggregatedLoss — позиционная record, расширение рябит по фикстурам (обновить в том же коммите).

### M36 — доминирующий канал + число в дебрифе (L, на M35)

- **Точки касания:** `telemetry.proto` (AggregatedLoss 10–11); `SessionLossAccumulator.cs:44–72` (DominantChannel picker, согласован с `CornerEventBuilder.ChooseReason:220–243`); Gold `GoldAggregatedLoss.cs`/`GoldArtifactBuilder.cs`; новый `ChannelGloss.cs` (RU + единицы, fail-closed, зеркало ReasonGloss); `CoachStrings.resx` (Channel_* + units); `DebriefTemplate.cs:22–30` (обогащение «why» значением+единицей).
- **Задачи:** (1) proto+accumulator; (2) рендер канал+число в дебрифе.
- **proto:** AggregatedLoss 10/11.
- **Риски:** channel↔reason drift — оба из единого ChooseReason; корректность единиц (min_speed=км/ч, line/brake/throttle=метры — тест per-channel); RU-текст только в .resx; DebriefTemplate — golden byte-stable, фикстуры обновить в том же коммите.

### M41 — grounded-обогащение дебрифа (L)

- **Точки касания:** `telemetry.proto` (SessionEvent 19/20, AggregatedLoss 12, новые SectorCornerMembership/BalancePhaseTrend/LossTrend); `BalanceKernels.cs` (или новый PhaseBalanceKernels — пофазный scorer БЕЗ braking-гейта на входе/выходе, с MinSteadyStateFrames-полом); `CornerPhaseBands.cs` (срезы фаз); `CornerEventBuilder.cs` (пофазный баланс в CornerContribution); `ComputeSession.cs` (пофазные аккумуляторы, `_sectorCornerIds` в EmitSector по apex-позиции, LossTrend по номеру круга, BalancePhaseTrend в Complete); `SessionLossAccumulator.cs` (per-lap buckets → LossTrend); `ComputeOptions.cs` (трендовые пороги, min-frames); Gold — новые `GoldBalanceProfile.cs`/`GoldSectorMembership.cs`, `GoldSessionPayload.cs`, `GoldArtifactBuilder.cs:83–102` (resolve corner_ids→RU через CornerNameMap, установить SetupHint); новый `SetupHintDeriver.cs`; `DebriefTemplate.cs`; `coach.system.debrief.v1.ru.txt` (правило grounding имён из sector_membership/aggregated_losses); `CoachStrings.resx`; `CoachOptions.cs` (чувствительность setup_hint — user-facing).
- **Задачи:** (0) ADR-0022; (1) proto; (2) пофазный баланс; (3) loss trend; (4) sector membership; (5) Gold session payload; (6) setup_hint + grounding имён.
- **proto:** SessionEvent 19/20, AggregatedLoss 12, новые типы/enum.
- **Риски:** пофазный баланс — сложнейшая часть (BalanceKernels намеренно гейтит steady-state; ослабление на входе/выходе рискует вернуть transfer-load bias — держать scale-free asymmetry + пол по кадрам); ACC часто без g-force → деградировать до brake/throttle-границ из CornerPhaseBands, не обнулять всё; LossTrend при 1–2 кругах → UNSPECIFIED (зеркало ConsistencyStddev-сентинела); sector↔corner на границе — детерминированно по apex-позиции, unit-тест на границу; setup_hint пороги — user-facing tier с консервативными дефолтами; Gold-determinism/privacy строгие — snake_case, null-dropped, без raw car_id; изменение промпта — сверить с RuEval фикстурами.

### M39 — кэширование статического system-префикса (M)

- **Точки касания:** `OpenRouterProvider.cs:76–99` (BuildBody: при opt-in и Anthropic-семействе — content-parts массив с `cache_control:{type:"ephemeral"}` на статическом префиксе; Gemini/OpenAI — plain string auto-cache; directive.SystemInstruction внутри кэшируемого префикса); `SchemaTranslatorSelector.cs` (FamilyOf гейт); `LlmRequest.cs` (+`SystemPromptSuffix` — retry-текст не мутирует префикс); `RouteOptions.cs`/`ResolvedRoute.cs`/`LlmRouter.cs` (`CacheSystemPrompt`, Tier-2, default off); `CoachService.cs:324,462,558–559` (BuildRetryPrompt пишет suffix, retries ставят SystemPromptSuffix, не SystemPrompt); метеринг (LlmUsage/CostCalculator) — без изменений.
- **Задачи:** (1) breakpoint-плюминг; (2) стабильный префикс между ретраями.
- **proto:** нет.
- **Риски:** ~1150-токенный префикс НИЖЕ Anthropic-минимума 2048 (4096 для Opus) — cache_control silent no-op до естественного роста префикса; НЕ паддить промпт искусственно; честный ближний выигрыш — Gemini auto-cache + готовность метеринга; кэш prefix-exact — любой байт до breakpoint ломает; главный риск корректности — не двигать volatile-контент (retry-reminder, per-request Gold) впереди breakpoint (для этого и Task 2).

## ADR к написанию

- **ADR-0017** — Reference snapshots vs in-place overwrite (M37): активный указатель [references] + history-таблица + retention/FK.
- **ADR-0018** — Пофазное знаковое отклонение от линии на CornerEvent (M34): поля 18/19/20 + конвенция знака, почему RMS-поле 9 сохранено.
- **ADR-0019** — Медианная центральная линия как runtime LINE-референс + compute-side гейт типа поворота (M38): runtime-расширение ADR-0014, отклонённая альтернатива corner_radius_m на проводе.
- **ADR-0020** — Таксономия детекторов P3 + аддитивные скаляры CornerEvent (M33): reference-based vs self-only, рацио short-shift, аддитивность под ADR-0006.
- **ADR-0021** — Диагностические скаляры AggregatedLoss + policy агрегации + инвариант правдоподобности (M35, покрывает M36).
- **ADR-0022** — Grounded-обогащение дебрифа: sector→corner membership, пофазный баланс, тренд потерь (M41); имена и фразировка setup_hint остаются в Coach (ADR-0010).
- **ADR-0004 addendum** (M42, не новый ADR) — пин anthropic/claude-sonnet-4.6, DeepSeek gated OFF, слаг vs каноничный id.

Номера 0017–0022 — следующие свободные после существующего 0016 (окончательное присвоение — за владельцем).

## Секвенирование и батчинг в PR

Порядок ведёт от изменений без контракта к контрактным и от reference-spine к потребителям. Сначала прогрев без риска
(M42 docs+тест, M43 две compute-правки), причём M43 чинит GridMetrics/WorldPos-страж в том же
CornerEventBuilder/GridMetrics, что расширяют M34/M38 — golden-перебаза один раз. Затем независимый storage-only M37,
после — LINE-модель M34 (единственная proto-правка CornerEvent 18–20) и её runtime-надстройка M38 (без proto).
Детекторы M33 (CornerEvent 21–24) и диагностика M35/M36 (AggregatedLoss 6–11) — дизъюнктные ветки; обогащение M41
(SessionEvent 19–20 + AggregatedLoss 12 + новые типы) — последним; кэширование M39 (без proto, без общих файлов)
параллелится в любой момент.

Батчинг: один PR на M-элемент, ADR-коммит открывает ветку, контрактный коммит — первым внутри неё. M34+M38 — один PR
line-модели; M35+M36 — один PR за одним product-gate; M39 — отдельный low-blast PR.

## Решения для владельца (до старта имплементации)

1. **Ратифицировать консолидированную proto-раскладку** (заголовок): CornerEvent 18–20 (M34) / 21–24 (M33); AggregatedLoss 6–9 (M35) / 10–11 (M36) / 12 (M41); SessionEvent 19–20 (M41); новые SectorCornerMembership/BalancePhaseTrend/LossTrend. Разрешает конфликт независимо запрошенных одинаковых номеров.
2. **Product-gate M35/M36** — оверлей над M3; строить в P3 или отложить. При отладке 6–11 остаются reserved-unused, M41.trend всё равно = 12.
3. **Семантика strict-инварианта M35** — блокирующий тест или документированный флаг пробела детекции (указывает на M34); закрепить abs-then-average и epsilon.
4. **M38 runtime-центрлайн** — подтвердить vendored schema-versioned ассет + PB-fallback и отказ от corner_radius_m на проводе.
5. **M37 layout снапшотов** — схема версионного имени, дефолт ретенции (keep-all vs cap), поведение FK при удалении сессии.
6. **Гранулярность M35+M36** — один AggregatedLoss-контракт/PR или раздельные proto-коммиты.
7. **Объём/пороги M33** — descope track-width до kerb-strike или дроп; ратификация short-shift rpm-порога.
8. **Config-tier** — M41 setup_hint, M34 line-action пороги, M33 coaching-гейты: user-facing CoachOptions vs internal ComputeOptions (неоднозначное → user-facing, дефолты консервативные).
9. **Флаг M39** — явный RouteOptions.CacheSystemPrompt (default off) vs auto-by-family; признать silent no-op на Anthropic ниже 2048 (без паддинга).
10. **Присвоение номеров ADR** — подтвердить 0017–0022 и addendum к 0004.
