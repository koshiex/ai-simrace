# SimCoach — Мастер-бэклог доработок Фазы 3 (сводный)

*Дата: 2026-07-01 · Ветка `feat/phase-3-pr8` · Авторитетный прогон: `20260701-171602-738` (Monza / BMW M4 GT3, live LLM, 1 чистый PB-круг).*

> ## Статус исполнения (обновлено 2026-07-05)
>
> **P0 + P1 + P2 закрыты (32 пункта: M1–M32 + M45; M15 снят M5).** Осталось только **P3 (M33–M43)**.
>
> | Пак | Пункты | PR | Валидация |
> |---|---|---|---|
> | LLM/prompt-batch | M4,M5,M6,M8,M11,M12,M13,M14,M17,M23,M29 | в #26 | S→D→J |
> | Detection-truthfulness (P0) | M1,M2,M3,M16,M24,M25,M27 | **#26** влит | ground-truth 2/2 (NO-GO снят) |
> | P1 качество | M9,M7,M10,M18 (+track-limits фикс) | **#27** влит | S→D→J + игра |
> | P2 Wave A | M26,M19,M21,M20 (+proto `peak_brake_pct`) | **#28** влит | S→D→J + игра |
> | P2 Wave B | M22,M28,M30,M31,M32,**M45**,M32-high | **#31** | S→D→J + игра ✅ |
>
> **NO-GO по правдивости детекции СНЯТ** (#26): ground-truth 2/2 — «3929мс Curva Grande»→~0, «−14.8с S1»→~+17мс.
>
> **Валидация P2 Wave B в игре (2026-07-05, сессия `20260705-130034-345`):** M45 (severity по потере) + M10 cap → corner-советы ~7/круг → **~2/круг**, все Medium (ноль High-инфляции), повторов нет. Подтверждено — стена советов ушла. См. [[session-log-forensics]].
>
> **Находка для P3 (из игры):** коуч сравнивает с ТВОИМ PB (self-reference), не с идеалом. Медленный, но ровный пилот (напр. 1:53 на Monza) даёт delta vs own PB ≈ 0 → мало советов, хотя точек роста vs идеал много. **Абсолютный/reference-free эталон нужен** — ядро P3: M38 (runtime median centerline), абсолютный min-speed, M34 (phase-line). Плюс M44 (голос vs оверлей).
>
> ### Хвосты / уже сделано попутно
> - **Track-limits over-silencing — ПОЧИНЕНО** (в #27): расцеплены латчи — эмиссия по `!IsInPitLane`, агрегаты/эталон по `IsValidLap && !pit`. Срезанный круг коучится живьём, статистику не пачкает.
> - **M44 — оверлей вместо тишины (идея владельца):** срезанные каденсом советы → в оверлей, не дроп. Ратифицировано; реализация — **Phase 5 (Overlay)**, в `implementation-plan.md`. Актуально для медленного пилота (см. находку выше).

Этот документ сводит воедино три ревью Фазы 3 в один приоритизированный план. Детали и доказательства — в исходных документах:

- **[ПД]** [`phase-3-acceptance.md`](phase-3-acceptance.md) — дефекты продукта/UX/полноты (находки #A…#Q + пробелы полноты).
- **[СИС]** [`phase-3-acceptance-addendum.md`](phase-3-acceptance-addendum.md) — ground-truth валидация детекции (105201 кадр из MCAP), вердикт готовности, спайн-бэклог #1…#15.
- **[LLM]** [`phase-3-llm-strengthening.md`](phase-3-llm-strengthening.md) — усиление LLM-слоя (n1…n39).

Ссылки вида `[СИС#2]`, `[ПД#A]`, `[LLM n34]` указывают на исходный пункт.

---

## 1. Как читать этот бэклог (логика гейта и вердикт)

**Вердикт приёмки:** инженерный спайн Фазы 3 построен и подключён (D0–D9), но **качество коучинга не принято**, а готовность к Фазе 4 — **NO-GO**. Причина: слой детекции на личном рекорде водителя эмитирует фактически неверные числа (инвертированный знак, физически невозможные величины), а таксономия действий неполна. Строить TTS поверх этого = уверенно ОЗВУЧИТЬ ложь.

**Приоритеты выстроены как релиз-гейт, а не как «список по важности»:**

- **P0 — гейт Фазы 4.** Пока не закрыто — нельзя ни показывать пилоту, ни начинать Voice/TTS. Две группы: (а) правдивость детекции (корень большинства ложных советов); (б) грубые user-visible баги, дешёвые и разрушающие доверие с первой фразы.
- **P1 — качество коучинга.** Высокая ценность, в основном prompt/config quick-wins, **не зависят** от починки детекции — можно делать параллельно с P0.
- **P2 — полнота, робастность, наблюдаемость.** Расширение таксономии, fallback, метрики, латентные дыры.
- **P3 — архитектурное.** Contract/compute-changes и то, что честно относится к следующим фазам.

**Ключевой принцип разделения P0:** правдивость детекции чинится **алгоритмически** (guard + выравнивание span + гейт круга), а НЕ усилением LLM. LLM-победы (имена, краткость, abstain) независимы и дёшевы — поэтому тоже в раннем эшелоне.

---

## 2. P0 — Гейт Фазы 4 (обязательно до старта Voice/TTS)

### 2A. Правдивость детекции (корень ложных советов; чинится алгоритмически)

| # | Пункт | Тип | Эффорт | Куда смотреть | Что закрывает |
|---|---|---|---|---|---|
| ✅ M1 | **Coachable-lap гейт** на эмиссии `EmitCorner`/`EmitSector` и во ВСЕ сессионные аккумуляторы (racing, non-pit, valid) | детекция / архитектура | medium | `ComputeSession.cs:96-109,201,220-221`; `SessionLossAccumulator.cs:24-42` | `[СИС#1]` `[ПД#B,#C,#E]` — ложные советы на out/in-круге, отравлённые средние, инвертированный debrief |
| ✅ M2 | **Выравнивание span**: self time-at-position считать над тем же `[Start,End]`, что и ref; триггер возврата газа — только для exit-специфичных self-кернелов | kernel-correctness | medium | `CornerEventBuilder.cs:79-92`; `CornerTracker.cs:58-68` | `[СИС#2]` `[ПД#A]` — «3929мс Curva Grande» (= время прохождения эталона), truncated self-кинематика |
| ✅ M3 | **Plausibility-guard** перед рендером/фразингом: отбрасывать/понижать потерю, если величина или знак противоречат круговой delta (потеря сектора > дефицита круга; знак против круговой delta) | детекция / readiness | medium | `ComputeSession.cs:348-354`; `TipValidator.cs:82-119`; debrief assembly | `[СИС#3]` `[ПД#D]` — фильтр «уверенно озвученной лжи»; страховка на случай остаточных артефактов |

> **Почему все три, а не одна:** M1 убирает out/in-контаминацию, но `[СИС#2.2]` доказал — «3929мс» воспроизводится и на чистом круге (это дефект span, M2). M3 — последний рубеж: даже с M1+M2 любой новый артефакт детекции не должен доходить до озвучки. Это и есть алгоритмический guard, который заменяет «LLM должен понять, что число — бред» (он не может, он селектор+фразер).

### 2B. Грубые user-visible баги (дёшево, бьют по доверию сразу)

| # | Пункт | Тип | Эффорт | Куда смотреть | Что закрывает |
|---|---|---|---|---|---|
| ✅ M4 | **Пустой плейсхолдер `lap_pb`** «Личный рекорд! Главная зона — .» — политика отсутствующего параметра | баг | quick-win | `actionRegistry.json:310`; `GoldArtifactBuilder.cs:117`; `PhraseRenderer.cs:22/42-44` | `[ПД#N]` |
| ✅ M5 | **Русские имена поворотов**: добавить `corner_name_ru`=`GetShort` в Gold + жёсткое prompt-правило «только RU-форма, не транслитерировать» + перевести template-путь на `GetShort` | gold + prompt | quick-win | `GoldCornerEvent.cs`; `GoldArtifactBuilder.cs:30`; `coach.system.v1.ru.txt` (правило 2); `CoachService.cs:305`; `CornerNameMap.cs` | `[ПД#P]` `[LLM n34/n3]` — зоопарк «Curva Grande»↔«Роджи»↔«Параболике» |
| ✅ M6 | **Один совет на фразу + запрет сырых чисел в голосе** (числа только в debrief) | prompt | quick-win | `coach.system.v1.ru.txt:5,7-8`; `coach.fewshot.v1.ru.json` | `[ПД#Q,#D]` `[LLM n1/n2]` — «TTS не успевает / info-garbage» |

> **M4–M6 не зависят от 2A** и делаются параллельно. M5 использует уже существующие RU-формы (`GetShort`), которые сейчас вычисляются, но в Gold не попадают.

---

## 3. P1 — Качество коучинга (высокая ценность, не зависит от детекции)

| # | Пункт | Тип | Эффорт | Куда смотреть | Источник |
|---|---|---|---|---|---|
| M7 | **Право промолчать (abstain)**: sentinel `"none"` в enum на слабом catch-all; трактовать как тишину; границы (High никогда не молчит) | schema + code | medium | `OutputSchema.cs:25-46`; `CoachService.cs:247-262`; `CoachOptions.cs` | `[LLM n29/n8]` `[ПД#I]` |
| ✅ M8 | **Привязка `phrase_ru` к смыслу `action_id`** + RU-хинт действия в меню + anti-example | prompt + data | quick-win | `PromptBuilder.cs:130`; `actionRegistry.json`; `coach.system.v1.ru.txt` | `[LLM n4/n30]` |
| M9 | **Фазовый контекст для `straighter_braking`**: overlap только в turn-in/apex, не brake-на-прямой | детекция / дизайн | medium | `actionRegistry.json:52-66`; `BrakeOverlapSteerKernels.cs`; `CornerPhaseResolver.cs` | `[ПД#F]` — «шикана: не тормози» |
| M10 | **Cadence-governor**: приоритет по потере времени, cooldown, «одна вещь за раз» (пересекается с M6/M7) | продукт / дизайн | medium | `RuleEngine.cs`; `RuleEngineOptions.cs`; `CoachService.cs:200-235` | `[ПД#I,#Q,#J]` |
| ✅ M11 | **Evidence-weighted арбитраж** в corner-промпте: выбирать по подтверждению числами Gold, не blind-first-pick | prompt | quick-win | `coach.system.v1.ru.txt` (правило 1) | `[LLM n28]` |
| ✅ M12 | **Cold-start ветка** (нет эталона): что говорить без reference-полей | prompt | quick-win | `coach.system.v1.ru.txt`; few-shot `:46-58` | `[LLM n7]` `[ПД#H]` |
| ✅ M13 | **`temperature=0`, `top_p=1`** на каденс-routes (сейчас sampling не задан вообще) | config | quick-win | `RouteOptions.cs`; `OpenRouterProvider.cs:85-99`; `appsettings.json` | `[LLM n21]` |
| ✅ M14 | **Свап corner-модели** → `google/gemini-3.1-flash-lite` (+ явный thinking-off, + rate-card) — режет хвост таймаутов на 2с-кэпе | routing | quick-win | `appsettings.json:60,70-72` | `[LLM n15/n16]` |
| 🟡 M15 | **Единый word-cap**: имя поворота не считать словами (в основном решается M5: `corner_name_ru` = 1–2 токена) | schema | medium | `PhraseWordCount.cs`; `TipValidator.cs:57`; `CoachOptions.cs:15` | `[ПД#K/L/M]` `[LLM n9]` |
| ✅ M16 | **`brake_point_diff_m`**: расширить окно ~200м вверх по трассе до реальной зоны торможения (сейчас часто схлопывается в 0) | kernel-correctness | medium | `BrakeKernels.cs:47-56`; `CornerEventBuilder.cs:83-85` | `[СИС#5]` |
| ✅ M17 | **Debrief как bounded-аналитик**: plausibility-guard противоречивых потерь (prompt-only) + заземление «почему» (категория + мс) | prompt | quick-win | `coach.system.debrief.v1.ru.txt` | `[LLM n31/n32-QW]` `[ПД#3]` |
| M18 | **RU-eval гейт (m5)**: LLM-судья + рубрика + фикстуры no-PB/corner/debrief + числовой порог — обещан планом, не построен | полнота / тесты | medium | новый eval-проект в `tests/`; `phase-3-detailed-plan.md:1108-1132` | `[ПД#23]` — регрессионный барьер для всех prompt-правок выше |

> **M18 — стратегический:** без него все правки промптов (M5,M6,M8,M11,M12,M17) делаются «на глаз». Строить его стоит рано, чтобы измерять эффект остальных P1.

---

## 4. P2 — Полнота, робастность, наблюдаемость

| # | Пункт | Тип | Эффорт | Куда смотреть | Источник |
|---|---|---|---|---|---|
| M19 | **Reference-free tier**: абсолютные/self-best действия «широкая линия / медленный минимум» для cold-start (16/25 действий требуют reference → на первом круге 0 совета) | детекция / дизайн | medium | `ActionRegistry.cs:96-104`; `actionRegistry.json` (`requires_reference`); `racing_line_deviation_m` | `[СИС#8]` `[ПД#H]` |
| M20 | **Session-метрики через детерминированный слой**: Session Gold field set + templated debrief (сейчас `GoldFieldNames.For(Session)` бросает; consistency считается, но в тип не доходит) | полнота | medium | `GoldFieldNames.cs:43`; `ComputeSession.cs:386-414`; `DebriefTemplate.cs` | `[СИС#7]` `[ПД §2]` |
| M21 | **`corner_catch_all` — молчать вместо raw-длительности**; активировать мёртвое поле `reason` + `reason_ru` глосс; де-дубль same-family действий | детекция + prompt | medium | `actionRegistry.json:222-236`; `WhenClause.cs`/`ClauseEvaluator.cs`; `GoldArtifactBuilder.cs:45` | `[ПД#G]` `[LLM n35]` |
| M22 | **Реальная fallback-цепочка**: расширить триггер роутера (Timeout/ServerError, не только CircuitOpen) + задать `FallbackRouteKey` (debrief → `claude-haiku-4.5`) + circuit-tuning | code + config | medium | `LlmRouter.cs:52-59`; `RouteOptions.cs`; `appsettings.json:89-93` | `[LLM n17/n18/n23]` |
| ✅ M23 | **Наблюдаемость accept/fallback**: структурный лог/счётчик source+cadence+причина отбраковки (сейчас теряется в `out _`) | баг / метрики | quick-win | `CoachService.cs:246-262,391-405` | `[ПД#O]` `[LLM n33]` |
| ✅ M24 | **`min_speed`/throttle-resume self-кернелы над полным span**, а не 2-кадровым окном (следствие M2) | kernel-correctness | medium | `CornerTracker.cs:52-62`; `ThrottleSpeedKernels.cs:22-31` | `[СИС#6]` |
| ✅ M25 | **Замена mean-of-crossings на best-lap/median** посекторную delta (частично решается M1) | accuracy / aggregation | medium | `ComputeSession.cs:411-414,220-221` | `[СИС#4]` |
| M26 | **`BalanceKernels`**: steady-state gating + нормализация understeer/oversteer до clamp/порогов (сейчас перенос массы под тормозом читается как understeer; уже гейтит live-действия) | kernel-correctness | medium | `BalanceKernels.cs:33-52`; `ComputeSession.cs:132-134` | `[СИС#9]` |
| ✅ M27 | **`IsInPitLane` в clean-предикат** (согласовать с fuel-гейтом) | детекция | quick-win | `CleanLapPredicate.cs:29`; `ComputeSession.cs:249-256` | `[ПД#E]` `[СИС]` |
| M28 | **Персистить `reasoning_tokens`** + подтвердить thinking-off из данных; retry-промпт эхом причины отказа; per-family robustness (Gemini `maxItems`, debrief-validation, refusal-log) | code / наблюдаемость | quick-win | `SqliteCostMeter.cs`; `OpenRouterProvider.cs:237-241`; `GeminiSchemaTranslator.cs`; `coach.retry.v1.ru.txt` | `[LLM n22/n6/n13]` |
| ✅ M29 | **`MonthlyBudgetUsd` ненулевой** перед Live (сейчас 0 = off); safe-ceiling ~$5-10 | config | quick-win | `appsettings.json:51` | `[LLM n26]` `[ПД#25]` |
| M30 | **A/B RU-качества one-liner** (2.5 vs 3.1 vs DeepSeek V4 Flash vs Qwen3.6) на реальных Gold-событиях | eval | medium | shadow-harness; `coach_tips`/`llm_usage` | `[LLM n19]` |
| M31 | **Bounded confidence-поле** (enum high/low) + логирование — только вместе с abstain-гейтом (M7) и после замера калибровки | schema + code | quick-win | `OutputSchema.cs:38-42`; `CoachService.cs:265-272` | `[LLM n12/n33]` |
| M32 | **Дедуп per corner_id+lap + межкруговая память** (пересекается с M10) | дизайн | medium | `CoachService.cs`; `RuleEngine.cs` | `[ПД#J]` `[LLM n37]` |

---

## 5. P3 — Архитектурное / следующие фазы (contract/compute-change)

| # | Пункт | Тип | Эффорт | Куда смотреть | Источник |
|---|---|---|---|---|---|
| M33 | **Отсутствующие кернелы**: brake lockup (`slip_ratio`+`abs_active`+`brake` есть), short-shift (`gear`+`rpm`), kerb/track-width (`tyres_out`/`world_pos`), brake-release timing | детекция / completeness | large | `src/SimCoach.Pipeline/Kernels/` + proto + `actionRegistry.json` | `[СИС#10]` `[ПД §таксономия]` |
| M34 | **Phase-segmented signed line-deviation** (entry/apex/exit) + per-phase действия — сейчас 1 скаляр RMS, нельзя различить early/late apex; лучше detection-side, не corner-LLM | детекция / completeness | large | `CornerEventBuilder.cs:126-142`; `telemetry.proto:96`; `ClauseEvaluator` | `[СИС#11]` `[LLM n36]` `[ПД§таксономия]` |
| M35 | **Строгий plausibility-тест** (диагностические скаляры per-loss в `AggregatedLoss`) — для теста «потеря >150мс, но min_speed≈0 и deviation≈0»; требует политики агрегации | compute + contract | large | `CornerEventBuilder.cs:13`; proto `AggregatedLoss` (regen); `GoldAggregatedLoss` | `[LLM n32-arch]` `[СИС]` |
| M36 | **«Канал+число» в `AggregatedLoss`** для объяснения «почему» с конкретикой канала на debrief | compute + contract | large | `GoldAggregatedLoss`; детекция | `[LLM n31-arch]` |
| M37 | **Версионирование/снапшот эталона** (не перезаписывать parquet in-place) для аудируемости delta | reference-quality | medium | `ReferenceStore.MaybeUpdate`; `ComputeSession.cs:294-298` | `[СИС#12]` |
| M38 | **Runtime-линия от медианного centerline** (ADR-0014, сейчас только офлайн) вместо одного PB-круга; гейт line-действий по типу поворота | reference-quality | large | `CornerEventBuilder.cs:90,126`; `MedianCenterlineBuilder` | `[СИС#13]` |
| M39 | **Prompt-caching** статического ~1150-токенного префикса (автоматически при свапе на 3.1; для Anthropic — при префиксе ≥2048) | config + code | medium | `PromptBuilder.cs`; `BuildBody` | `[LLM n24]` |
| M40 | **Streaming debrief** (подготовка к P4-TTS, не даёт выигрыша в P3) | code | large | `OpenRouterProvider.cs:73-74`; `RouteOptions.Stream` | `[LLM n25]` |
| M41 | **Обогащение Gold-контекста**: prior-lap trend (corner), sector→corner-membership (debrief, сейчас галлюцинирует имена), session car-balance rollup → grounded `setup_hint`, per-phase баланс-скоры | compute + gold | medium–large | `GoldSessionContext`; `ComputeSession.cs:231,202-204`; `GoldSessionPayload.cs` | `[LLM n37/n38/n39/n36]` |
| M42 | **Doc-drift**: FR-061 → `claude-sonnet-4.6`; DeepSeek «not yet added»; сверить FR-014/060/072; per-family schema-acceptance real-HTTP фикстура | полнота / docs | quick-win | `functional-requirements.md:81`; `phase-3-detailed-plan.md:1166-1172`; `tests/SimCoach.LLM.Tests` | `[ПД#24/#27/#28]` `[LLM n24]` |
| M43 | **Latent-guards**: null/(0,0,0) WorldPos в RacingLineDeviation; общий знаменатель `Index`/resampler + интерполяция `TimeAt` | kernel-correctness | quick-win | `CornerEventBuilder.cs:133-137`; `AccFrameMapper.cs:116`; `GridMetrics.cs:22` | `[СИС#15]` |

---

## 6. Последовательность и зависимости

**Рекомендуемый порядок исполнения:**

1. **Спринт «Правда» (P0-2A + M18):** M1 → M2 → M3, параллельно строить M18 (RU-eval гейт). Это снимает NO-GO по фактической части. Обязательно — **повторная ground-truth валидация против того же CSV** (`frames-20260701-171602-738.csv`) после правок.
2. **Спринт «Голос-готовность» (P0-2B, параллельно):** M4, M5, M6 — дешёвые prompt/gold правки, независимы от спринта 1. После них фраза, которую услышит пилот, перестаёт быть мусором.
3. **Спринт «Качество» (P1):** M7–M17, приоритет quick-wins (M8,M11,M12,M13,M14) + M9 (шикана) + M17 (debrief). Мерить эффект через M18.
4. **Далее P2 → P3** по мере надобности.

**Явные зависимости:**
- M15 (word-cap) и M5 (RU-имена) — M5 делает M15 почти ненужным (имя = 1–2 токена).
- M31 (confidence) деплоить **только** с M7 (abstain).
- M24, M25 — следствия/добивка M2, M1.
- M22 (fallback) — сначала расширить триггер роутера, потом задавать `FallbackRouteKey`.
- M35/M36 (строгий guard, «канал+число») — надстройка над M3; делать только если prompt-only M3/M17 окажется недостаточен.
- Все prompt-правки (M5,M6,M8,M11,M12,M17) — эффект измерять через M18.

**Минимальная планка «GO на Фазу 4»** = закрыты M1, M2, M3 (правдивость), M4, M5, M6 (не-мусорная фраза), и пройдена повторная ground-truth валидация + базовый прогон M18.

---

## 7. Сводка по источникам (трассируемость)

| Исходный документ | Пункты | Как вошли в мастер-бэклог |
|---|---|---|
| **[ПД]** acceptance | #A–#Q, пробелы полноты #23–#28 | #A→M2, #B/C→M1, #D→M3/M6, #E→M1/M27, #F→M9, #G→M21, #H→M12/M19, #I→M7/M10, #J→M32, #K/L/M→M15, #N→M4, #O→M23, #P→M5, #Q→M6/M10, #23→M18, #24/27/28→M42 |
| **[СИС]** addendum | спайн #1–#15, вердикт NO-GO | #1→M1, #2→M2, #3→M3, #4→M25, #5→M16, #6→M24, #7→M20, #8→M19, #9→M26, #10→M33, #11→M34, #12→M37, #13→M38, #14→M21, #15→M43 |
| **[LLM]** strengthening | n1–n39, бэклог P0–P3 | n1/2→M6, n3/34→M5, n4/30→M8, n7→M12, n8/29→M7, n9→M15, n12/33→M31, n13/6/22→M28, n15/16→M14, n17/18/23→M22, n19→M30, n21→M13, n24→M39/M42, n25→M40, n26→M29, n28→M11, n31/32→M17/M35/M36, n35→M21, n36/37/38/39→M41 |

*Все пункты заземлены на файл:строку / лог / строку БД / измеренное значение или цитированный внешний источник в исходных документах. Уже-исправленные баги (HTTP 400, timeout, cost, shutdown-drain, non-monotonic crash) и артефакты fake-провайдера в бэклог не включены.*
