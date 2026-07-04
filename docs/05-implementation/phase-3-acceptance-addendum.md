# Аддендум к system-review Phase 3 — системная валидация детекции против ground truth

> **Сводный план:** [`phase-3-master-backlog.md`](phase-3-master-backlog.md) — единый приоритизированный бэклог всех трёх ревью. Спайн-пункты #1…#15 ниже вошли туда как M1…M43. Также: [`phase-3-acceptance.md`](phase-3-acceptance.md) (дефекты продукта/UX), [`phase-3-llm-strengthening.md`](phase-3-llm-strengthening.md) (LLM-слой).

Сессия `20260701-171602-738` · Monza · BMW M4 GT3 · 1 зачётный чистый круг (PB, `lap_time_ms=114849`, `is_pb=1`, `is_clean=1`). Все числа ниже измерены из декодированных кадров, рядом стоит то, что выдал pipeline.

---

## 1. Методология и статус ground-truth

**Как валидировали:** реальный hand-rolled MCAP-декодер (`McapSegmentEnumerator.Read` через throwaway .NET 9 dumper с `ProjectReference` на `SimCoach.Storage`/`SimCoach.Contracts`) → покадровый CSV **105201 кадр** (`segment-0000..0004.mcap`) → оракул истины на pandas/awk → сравнение с эмитированными значениями из `simcoach.db` (`coach_tips`, `laps`, `references`) и логов.

**Что удалось измерить, а не вывести из кода:**
- **Измерено из кадров (сильная уверенность):** структура заезда (3 прохода трассы по `normalized_car_position`, 2 wrap-а на строках 58530/104405), тайминг круга и секторов, вся покадровая кинематика поворотов (min speed, точки торможения/газа, руль, Glat, локап/пробуксовка), время нахождения в span каждого поворота.
- **Симуляция реального kernel-кода на кадрах (сильная уверенность):** прогон `CornerTracker`/`CornerEventBuilder`/`ThrottleSpeedKernels` над кадрами воспроизвёл эмитированные `delta_ms` и `min_speed_diff` — корневой механизм подтверждён, а не додуман.
- **Только чтение кода (средняя уверенность):** `BalanceKernels` (per-wheel `wheel_slip` не попал в CSV), `RacingLineDeviation` null-guard, `GridMetrics.Index` для дробных длин трассы, полнота таксономии действий.
- **Обратный расчёт (ограниченная уверенность):** эталон S1 (~36467ms) восстановлен обратным счётом — исходный эталон физически перезаписан этим же кругом (см. §4).

**Доверие выводам:** высокое для разделов 2–3 (числа воспроизведены из кадров), высокое для §5 (структурные факты по коду), среднее для эталон-относительных абсолютов, где сам эталон недоступен.

**Ключевые caveats данных:** (1) ACC `lap_number` бесполезен — 104403/105201 кадра = «1»; сегментация pipeline по `normalized_car_position` — верное решение. (2) ~50% кадров — дубликаты SHM (over-poll 333Hz поверх ~200Hz физики); тайминги считались по `t_ms`, устойчиво. (3) `is_in_pit_lane=1` у 11806 кадров — это суммарно out-lap + in-lap, не только out-lap.

---

## 2. Точность детекции (измерено против реальности)

### 2.1. Тайминг круга и секторов — ТОЧЕН (здоровое ядро)

| Метрика | Эмитировано (БД) | Истина из кадров | Вердикт |
|---|---|---|---|
| Круг (flying) | 114849ms | 114846–114849ms | **верно** (0..+3ms, квантование линии) |
| S1 | 35994ms | 35994ms (ncp 0.000..0.337) | **верно** (0ms) |
| S2 | 40172ms | 40172ms (0.337..0.665) | **верно** (0ms) |
| S3 | 38655ms | 38680ms | **верно** (+25ms, точка линии) |
| Круговая delta vs эталон | −1381ms | 114849 − 116230 (прежний эталон) | **верно** (сравнение полных совпадающих span) |

Сегментация, посекторный тайминг и круговая delta работают на полных выровненных span и **воспроизводимо точны**.

### 2.2. «3929мс» на Curva Grande (`monza_t03`) — МУСОР (delta = минус время прохождения эталона)

- БД id46: `corner_catch_all`, `rendered_param='3929мс'`, сгенерировано на **OUT-LAP** (+64s).
- **Корневой механизм (уточнён против основного дефектного дока — коллапсирует SELF-окно, а не ref-член):** self-окно поворота ограничено триггером возврата газа (`CornerTracker.cs:58-68`, fire при `throttle>=0.5` за точкой min-speed), а ref-окно — геометрией `[Start,End]` (`CornerEventBuilder.cs:79-81`). На плоском полногазовом повороте min-speed стоит на входе, газ уже >0.5 на 2-м кадре → self-окно схлопывается до **2 кадра / 2ms**, тогда как ref-span = ~3931ms.
- `deltaMs = selfDur(2) − refDur(≈3931) ≈ −3929` → abs-рендер → **3929мс**. Это **время прохождения эталоном** Curva Grande, ложно поданное как «потеря».
- Ground truth Curva Grande: тормоз **0.00**, min **202.6 km/h**, gear 5, max|steer| **0.40 рад** — плоский kink на полном газу. Потерять 3929ms в одном таком повороте на круге, отставшем всего на 1381ms суммарно, физически невозможно.
- **Не out-lap-only:** self-vs-self прогон PB-круга даёт `t03=−3928` и на чистом круге. Гейт out/in-lap НЕ чинит это число — нужен fix выравнивания span.

### 2.3. «Сектор 1: 14799мс loss» на PB-круге — МУСОР (среднее, отравленное out-lap-ом)

- БД id57 `top_losses_json`: `{"corner":"Сектор 1","ms":14799}`. LLM добросовестно озвучил скормленное.
- Механизм: `_sectorDeltaAccum` (`ComputeSession.cs:220-221`) принимает delta **каждого** пересечения сектора; `SectorAvgDeltas()` (`:411-414`) возвращает среднее по всем пересечениям, включая out-lap с пит-лейном → в debrief через `GoldArtifactBuilder.cs:88`.
- Измерено: **out-lap S1 = 66535ms** (в т.ч. выезд с боксов), flying S1 = 35994ms. Обратный эталон refS1 ≈ 36467ms. Среднее delta = ((66535−36467)+(35994−36467))/2 = **14800ms** ≈ выданные 14799ms (int-truncation).
- **Правда:** на PB-круге S1 был **на ~473ms БЫСТРЕЕ** эталона (лучший S1 дня). Эмитировано: **+14799ms потери**. Ошибка ~15.3s, **знак инвертирован**.

### 2.4. Прочее эмитированное

| Событие | Эмитировано | Истина | Вердикт |
|---|---|---|---|
| id56 `lap_pb` | «Личный рекорд! Главная зона — .» | пустой `{top_corner}` | косметика: `TopLosses` фильтрует `DeltaMs>0`, на PB-круге таких нет |
| id48/id52 `higher_min_speed` | «В Лесмо 1 держи выше скорость» ×2 | один симптом на out-lap и flying | дубль из-за отсутствия гейта |
| Parabolica `min_speed_diff` | +15.1 km/h завышение | true min 127.3 vs 142.4 (2-кадровое окно) | систематическая ошибка (см. §3) |

---

## 3. Корректность кернелов

### CornerTracker + CornerEventBuilder (delta / self-окно) — КРИТИЧНО
Единый корень трёх дефектов: **self-окно (триггер возврата газа) и ref-окно (геометрия `[Start,End]`) покрывают разный физический span.**
- **`delta_ms` систематически неверна для ВСЕХ поворотов**, катастрофично для полногазовых. Self-vs-self прогон PB-круга: `t01=−708, t02=−1331, t03=−3928, t06=−3392, t09=−2770, t11=−6072`; сумма фантомного «выигрыша» ≈ −24..−27s против истинной круговой delta −1.381s. Не аддитивна к круговой delta, ошибка на порядок.
- **Self-кинематика truncated:** на `t03/t09/t10/t11` окно = 2 кадра. `min_speed_diff` Parabolica завышен на +15.1 km/h; на `t03` min совпал случайно (min на входе), но trail-brake/balance/wheelspin/jitter кернелы на всех четырёх поворотах работают по «пустому» окну.
- **Где смотреть:** `CornerEventBuilder.cs:79-81` (разность несовпадающих span), `CornerTracker.cs:58-68` (fire-семантика). **Fix:** measure self time-at-position над тем же `[Start,End]`, что и ref; триггер возврата газа оставить только для exit-специфичных self-кернелов.

### BrakeKernels (brake_point_diff_m) — ВЫСОКО
Зона торможения лежит **вне** окна поворота (геом. Start = поворот руля, но тормозят на прямой перед ним). Измерено: onset опережает Start на 41–290m. Для `t05/t09/t10/t11` водитель дотормозил до Start → `BrakeOnPosition=null` → `brake_point_diff_m` схлопывается в 0 для крупнейшей зоны (Parabolica). Для тормозящих-у-Start поворотов clip к ≈Start занижает onset на 40–150m. Коучинг «тормози позже/раньше» либо невозможен, либо задемпфирован. **Fix:** расширить буфер на ~200m вверх по трассе от Start. `BrakeKernels.cs:47-56`, `CornerEventBuilder.cs:83-85`.

### BalanceKernels (understeer/oversteer) — СРЕДНЕ (но уже LIVE-гейт)
Использует `WheelSlip` (raw, 0..12.37, ненормированный) по всем кадрам с `|steer|>0.05`, включая торможение/поворот руля → front-load bias читается как understeer. Уже гейтит live-действия: `actionRegistry.json:45,92,138` (`oversteer_score gt 0.6`, `understeer_score gt 0.6/0.7`) — пороги могут срабатывать на перенос массы под тормозом, не на реальный понос. **Fix:** ограничить steady-state mid-corner (низкий brake, высокий Glat, низкий Glong), нормализовать до clamp `[-1,1]`. `BalanceKernels.cs:33-52`, `ComputeSession.cs:132-134`. (Примечание: латеральный slip front-vs-rear для understeer — правильный сигнал; «lateral-contaminated» warning из WheelspinKernels относится к пробуксовке, не к балансу.)

### RacingLineDeviation — НИЗКО (латентно)
`selfX = frame.WorldPos?.X ?? 0f` (`CornerEventBuilder.cs:133-137`): null WorldPos → вклад ~(340..430m)² в RMS. В live-кадрах WorldPos заполнен (from ACC-mapper всегда), но тот же (0,0,0) sentinel достижим через fallback mapper-а при out-of-range playerSlot (`AccFrameMapper.cs:116`). **Fix:** skip кадров с null/(0,0,0); тот же паттерн в `PositionResampler.cs:138-140`.

### GridMetrics.Index — НИЗКО (латентно, сейчас недостижимо)
`Index` инвертирует по `(gridLength-1)`, `PositionResampler` хранит по `k/lapLengthM`. Для целочисленных длин (все 24 трассы в `AccTrackCatalog.cs` целые, Monza 5793) дрейф суб-метровый. Живой остаток — только неинтерполированное `TimeAt` (квантование ~1m/~до одного per-metre-dt на каждой границе). Дробная длина дала бы систематический дрейф, но триггера сейчас нет. **Fix:** общий знаменатель `lapLengthM` + интерполяция `TimeAt`.

---

## 4. Качество эталона и сегментации

- **Сегментация — верна.** По `normalized_car_position` (не по мусорному `lap_number`); границы секторов и круга точны до мс (§2.1).
- **Эталон = один авто-перезаписываемый быстрейший чистый круг** (`ReferenceStore.MaybeUpdate`, `ComputeSession.cs:295-297`). Monza-эталон **перезаписан этим же PB-кругом** (`references.source_session_id=20260701-171602-738`, `lap_time_ms=114849`). Runtime-сравнение шло с прежним эталоном 116230ms (correct: delta посчитана до swap-а), но кадры прежнего эталона **физически недоступны** — deltas неаудируемы/невоспроизводимы после побития PB. Это observability-gap, не delta-баг (круговая delta корректна).
- **Единственный эталон времени — это нормально** (delta-to-PB, индустриальная семантика; медиана фабриковала бы несуществующий круг). Но: нет истории/версии, нет outlier-rejection против атипичного «чистого» круга.
- **Runtime-линия сравнивается с одним PB-кругом**, а не с медианным centerline. ADR-0014 median centerline (`MedianCenterlineBuilder`/`CornerCenterlineDetector`) используется **только офлайн** (Bake + review page), не в runtime compute-path. `racing_line_deviation_m` относительно одного произвольного круга без outlier-rejection.
- **Единый архитектурный корень §2.2/2.3:** corner- и sector-события эмитируются и агрегируются на out/in-lap/pit-lane **без гейта** по racing/clean-кругу. `ComputeSession.Accept:96-109` зовёт `EmitCorner`/`EmitSector` каждый кадр; `HandleLap` знает про clean/fuel-гейты (`:243,250`), но эмиссия и аккумуляторы (`_sectorDeltaAccum`, `_sessionLosses`) — нет.

---

## 5. Полнота детекции (системный потолок)

Если LLM — селектор+фразер, потолок качества задают (1) покрытие таксономии и (2) точность детекции. Структурные дыры покрытия:

| Дыра | Факт | Почему это потолок |
|---|---|---|
| **No-reference коллапс** | без эталона доступно **5 из 15** corner-действий (только car-control симптомы: `straighter_braking`, `smoother_steering`, `ease_understeer`, `settle_oversteer`, `wheelspin_on_exit`); 16/25 действий `requires_reference` (`ActionRegistry.cs:100`) | Первый в жизни чистый круг на новой связке track/car/weather → **0** совета по линии/торможению/скорости/апексу. Эталоны персистятся по triple, так что это cold-start только, но именно там 0 коучинга по абсолютным метрикам. |
| **Dead `reason`-field** | `ChooseReason` вычисляет причину (`CornerEventBuilder.cs:99-172`), но `WhenClause` только Number\|Bool; **0/25** действий роутят по `reason` | Причинный ярлык вычислен и не может выбрать совет; роутинг держится на переиздании тех же числовых порогов. (Уточнение: `reason` можно фразить через `PhraseRenderer`, но никем не используется; cause-specific коучинг доступен через числовые diff-clauses — потолок ниже, чем «unreachable».) |
| **Phase-blind линия** | 1 скалярный `racing_line_deviation_m` (RMS по всему повороту), 1 line-действие `tighten_apex` | Нельзя выразить wide-entry / early-vs-late-apex / exit-wide — противоположные фиксы. (Смягчение: entry/apex/exit консеквенции частично несут `brake_point_diff_m`/`min_speed_diff_kmh`/`throttle_resume_diff_m`.) |
| **Отсутствующие кернелы** | нет detector-ов: brake lockup (`slip_ratio`+`abs_active`+`brake` есть), short-shift (`gear`+`rpm` есть), kerb/track-width (`tyres_out`/`world_pos` есть), brake-release timing | Входные данные в кадре присутствуют, детекторов нет — стандартные категории коучинга недостижимы. `gear` (−1..6) и exit-only `slip_ratio` не потребляются целиком. |
| **Session-метрики в никуда** | `ConsistencyStddevMs`/`TheoreticalBestGapMs`/`SectorAvgDeltas` считаются, но `GoldFieldNames.For(Session)` бросает `NotSupportedException`; **0** session-cadence действий | Всё session-scoped уходит **только** в неограниченную debrief-прозу — ровно тот канал, где родилось ложное «14799мс». Самые числочувствительные метрики едут по наименее контролируемому каналу. |
| **Fabricating fallback + дубли** | 3 catch-all (corner/sector/lap) на `abs(delta)>threshold` с причинно-пустой фразой; `min_speed_diff_kmh<-3` в 4 действиях | `corner_catch_all` фабрикует число-длительность как «потерю» на повороте без решения (Curva Grande); near-duplicate speed-советы конкурируют за 5 слотов меню. |

---

## 6. Роль LLM при текущем уровне детекции

Архитектура — **LLM как селектор+фразер**: все телеметрические решения алгоритмические, LLM озвучивает поданное число verbatim. Это подтверждено: debrief (claude-sonnet-4.6) добросовестно произнёс «Сектор 1: 14799мс» — число пришло из `SectorAvgDeltas`, LLM его не оспорил и не мог.

Следствие: **потолок качества коучинга = min(точность детекции, покрытие таксономии)**, и обе половины сейчас пробиты. Числа, которые LLM озвучит, местами не просто неточны — они **инвертированы по знаку** (потеря заявлена там, где выигрыш) и **физически невозможны** (потеря в одном повороте > дефицита всего круга). Пока LLM не аналитик, у него нет механизма отловить это — значит нужен **алгоритмический plausibility-guard** между детекцией и фразингом (сейчас `TipValidator` проверяет только структуру/длину, не величину/знак). Усиливать LLM-часть до починки детекции бессмысленно: она озвучит ложь увереннее.

---

## 7. Вердикт готовности к Фазе 4 (Voice/TTS)

## NO-GO (гейт: red)

Точность half-spine расслаивается:
- **Точно / готово к застройке:** сегментация круга и секторов, посекторный тайминг (0ms), круговая delta, покадровая кинематика поворотов — всё воспроизведено из кадров, математика ядра здорова. Требуемая переработка **ограничена** полосой reference/delta/aggregation/gating — это не переписывание spine.
- **Неверно / блокирует:** всё, что содержит **delta vs эталон** и **сессионные агрегаты** — corner `delta_ms` (несовпадение span), посекторные средние (отравление out-lap), из-за отсутствия coachable-lap гейта.

Риск: озвучить через TTS «−14.8s в S1» и «3929мс потери в Curva Grande» на **личном рекорде** водителя — это уверенно произнесённая ложь на лучшем заезде, прямой репутационный ущерб. NO-GO — не полировка, а hard blocker.

**Минимальная планка до старта Фазы 4** (после — повторная валидация против этого же CSV):
1. Гейт «coachable lap» (racing, non-pit, valid) на входе `EmitCorner`/`EmitSector` и во все аккумуляторы.
2. Выравнивание span в `CornerEventBuilder`: self time-at-position над `[Start,End]`, а не над окном триггера газа.
3. Замена mean-of-crossings на best-clean-lap / median посекторную delta.
4. Plausibility-guard перед фразингом: отбрасывать, если сектор-потеря > круговой дефицит или знак противоположен круговой delta (проверка «потеря ≤ время сектора» этот кейс НЕ ловит — 14799<35994).
5. Silent fallback вместо `corner_catch_all` с raw-длительностью.

---

## 8. Приоритизированный список исправлений спайна (системный уровень)

| # | Приоритет | Пункт | Тип | Куда смотреть |
|---|---|---|---|---|
| 1 | **P0** | Coachable-lap гейт на эмиссии и агрегации (закрывает и ложные corner-советы, и отравлённые средние) | gating / архитектура | `ComputeSession.cs:96-109, 201, 220-221`; `SessionLossAccumulator.cs:24-42` |
| 2 | **P0** | `delta_ms` и self-кернелы над несовпадающим span — выровнять self и ref на `[Start,End]` | kernel-correctness | `CornerEventBuilder.cs:79-92`; `CornerTracker.cs:58-68` |
| 3 | **P0** | Plausibility-guard (величина/знак vs круговая delta) перед рендером/фразингом | readiness | `ComputeSession.cs:348-354`; `TipValidator.cs:82-119`; debrief assembly |
| 4 | **P1** | Замена mean-of-crossings на best-lap/median посекторную delta | accuracy / aggregation | `ComputeSession.cs:411-414, 220-221` |
| 5 | **P1** | `brake_point_diff_m`: расширить окно вверх по трассе (~200m) до реальной зоны торможения | kernel-correctness | `BrakeKernels.cs:47-56`; `CornerEventBuilder.cs:83-85` |
| 6 | **P1** | `min_speed`/throttle-resume self-кернелы над полным span, а не 2-кадровым окном | kernel-correctness | `CornerTracker.cs:52-62`; `ThrottleSpeedKernels.cs:22-31` |
| 7 | **P1** | Session-метрики через детерминированный слой (Session Gold field set + templated debrief с guard) вместо свободной прозы | completeness | `GoldFieldNames.cs:43`; `ComputeSession.cs:386-414`; `DebriefTemplate.cs` |
| 8 | **P1** | No-reference tier: абсолютные/self-best coaching-действия для cold-start | completeness | `ActionRegistry.cs:96-104`; `actionRegistry.json` (`requires_reference`) |
| 9 | **P2** | `BalanceKernels`: steady-state gating + нормализация understeer/oversteer до clamp/порогов | kernel-correctness | `BalanceKernels.cs:33-52`; `ComputeSession.cs:132-134` |
| 10 | **P2** | Отсутствующие кернелы: lockup, short-shift, kerb/track-width, brake-release | completeness | `src/SimCoach.Pipeline/Kernels/` (+ proto + `actionRegistry.json`) |
| 11 | **P2** | Phase-segmented signed line-deviation (entry/apex/exit) + per-phase действия | completeness | `CornerEventBuilder.cs:126-142`; `telemetry.proto:96` |
| 12 | **P2** | Версионирование/снапшот эталона (не перезаписывать parquet in-place) для аудируемости delta | reference-quality | `ReferenceStore.MaybeUpdate`; `ComputeSession.cs:294-298` |
| 13 | **P2** | Runtime-линия от медианного centerline (ADR-0014), не от одного PB-круга; гейт line-действий по типу поворота | reference-quality | `CornerEventBuilder.cs:90,126`; `MedianCenterlineBuilder` |
| 14 | **P2** | `corner_catch_all` — молчать вместо raw-длительности; `reason`-string clause operator; де-дубль same-family действий | completeness | `actionRegistry.json:222-236`; `WhenClause.cs`/`ClauseEvaluator.cs` |
| 15 | **P2** | Null/(0,0,0) WorldPos guard; общий знаменатель `Index`/resampler + интерполяция `TimeAt` | kernel-correctness | `CornerEventBuilder.cs:133-137`; `AccFrameMapper.cs:116`; `GridMetrics.cs:22` |