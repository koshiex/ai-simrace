# План реализации: Detection-Truthfulness Pack — снятие NO-GO для Phase 4

> **Валидация:** этот план прошёл цикл Strict → Defender → Judge. Все required revisions Judge применены (self-buffer/upstream tension, Complete()-comparand после overwrite reference, track-model в exit-gate harness, self==ref formулировки acceptance, EmitSector wire-point, DurationMs mislabel, orphaned config, M1 residual + render-path coverage). Проза русская, идентификаторы/пути английские. Все file:line сверены на текущем дереве (ветка `feat/phase-3-fixes-batch`).

## Решения по Open Questions (утверждено пользователем, 2026-07-02)

Все 9 MAJOR-развилок разрешены владельцем. Реализация задач ниже ведётся с этими решениями; секция «Open Questions» в конце документа считается закрытой.

- **Q1 (emit-гейт, M1) → Latch.** Frame-level latch: при первом pit/invalid/unbounded кадре глушим все дальнейшие эмиты круга (+ `HasStartedLap` boundedness). Код структурировать так, чтобы позже локально переключиться на buffer-flush.
- **Q2 (структура предикатов) → вариант (c).** Общий frame-level `IsCoachableFrame` для M1-гейта и fuel-гейта + отдельный whole-lap `CleanLapPredicate` для reference-seeding. M27 минимальный (только добавить `IsInPitLane` в clean-предикат).
- **Q3 (corner-окно, M2/M24) → Full-span + two-window split, НО через обязательную эмпирическую валидацию ПЕРЕД коммитом M2.** См. «Pre-gate V0» ниже: сначала проверить на реальных записях из `%LOCALAPPDATA%/SimCoach/recordings` против ручных эталонных бейков (`cornerGeometry.monza.json`), что full-span `[Start,End]` даёт корректные per-corner числа и что сами бейки геометрически пригодны. Только при зелёной валидации M2 идёт в реализацию.
- **Q4 (sector-delta, M25) → Median чистых кругов** для attribution потерь в debrief/коучинге. **Важно (форвард-нота для UI P5):** это НЕ семантика «лучший сектор» (фиолетовый) — фиолетовый должен сравниваться с ПРЕДЫДУЩИМ ЛУЧШИМ сектором, не с медианой. M25 меняет только агрегат потерь (`sector_avg_delta_ms` field 14, proto не трогаем, смысл документируем); best-sector highlighting (`_bestSectorMs`, min) остаётся отдельным — при реализации не смешивать эти два канала.
- **Q5 (где guard, M3) → compute-side helper**, нейтрализация обнулением reference-относительной потери (silent fallback), покрывает `EmitCorner` И realtime `EmitSector`.
- **Q6 (политика guard, M3) → Drop silently + пороги в config** (`IOptions<ComputeOptions>`: Tier-A потолок ~2000мс/поворот, Tier-B ratio ~1.0–1.2×|дефицит круга|, floor ~300мс; сравнение ТОЛЬКО с дефицитом круга, никогда с абсолютом сектора). **UX-нота (на будущее, для UI, НЕ для гоночного аудио):** возможен визуальный статус «данных нет» вместо тихого drop — но во время заезда это аудиомусор, поэтому в аудио — молчим.
- **Q7 (brake-окно, M16) → `BrakeWindowUpstreamM=300м`** config-default. Pre-gate V0 эмпирически показал: brake onset лежит на **58–301м выше** baked Start у всех тормозных углов, поэтому 200м промахнулся бы по глубоким зонам (Parabolica ~301м) — по твоей пред-авторизации значение поднято до 300м. M16 окно проектировать с pre-Start lookback, НЕ под строгий `[Start,End]`.
- **Q8 (exit-gate) → env-gated xUnit** (скип без `SIMCOACH_GROUNDTRUTH_FIXTURE`) + закоммиченные dumper/oracle/doc. Сырые MCAP не коммитим (privacy/.gitignore).
- **Q9 (допуски приёмки) → умеренные полосы** (±150мс/поворот, ±3 км/ч, ±25м торможение, сумма ±30%) для oracle-заземлённых self-метрик + знак/порядок величины для reference-относительных; отклонять при неверном знаке независимо от величины.

### Pre-gate V0 (условие Q3, ДО реализации M2/M24)
Эмпирическая валидация full-span подхода на данных из `%LOCALAPPDATA%/SimCoach/recordings` (эталон `20260701-171602-738` + мультилэп `20260701-151452-346`) против ручных бейков `src/SimCoach.Reference/Data/cornerGeometry.monza.json`.

**Результат V0 (2026-07-02): GO (высокая уверенность, оба верификатора).** Отчёт: [`phase-3-detection-pregate-v0.md`](phase-3-detection-pregate-v0.md).
- Full-span воспроизводимо убирает «3929мс»: t03/t09/t10/t11 в текущем окне схлопываются до 2 кадров/~2мс; full-span даёт self ≈ 3928–3975мс на PB И на двух независимых летучих кругах (не тавтология). min-speed чинится (t11: 39.6→35.4 м/с).
- **Ручные бейки геометрически пригодны как есть — жёсткий фикс не требуется** (8/11 углов имеют явный апекс внутри `[Start,End]`, apexErr ≤42м; ни один End не обрезает апекс).
- **Runtime грузит 11-угольный ручной бейк** (подтверждено логами «baked (11 corners)»); устаревшая 6-угольная `Derived` on-disk модель НЕ используется — риск снят.

**Импликации для реализации M2/M24 (внесены в задачи):**
1. Окно M2 обязано ограничиваться ОДНИМ пересечением спана (грязный/pit/spin круг пересекает спан многократно → конкатенация исказит) — согласуется с M1-latch.
2. min-speed для углов без минимума (t03/t09, LateralG/transit) и для входов шикан (t04/t09, апекс в парном t05/t10) — обработка вынесена в **Open-decision D-minspeed** ниже (коучинг-материально, ждёт решения владельца).
3. M16 — pre-Start lookback 300м (Q7), не строгий `[Start,End]`.

### D-minspeed → РЕШЕНО (2026-07-02): подавлять min-speed при отсутствии истинного минимума в спане
Политика (максимум качества коучинга при минимуме риска): в M24 kernel вычисляет флаг `HasInSpanMinimum` — истинный локальный минимум скорости внутри `[Start,End]` (минимум НЕ на границе Start/End и с реальным замедлением, а не монотонный профиль). Если `HasInSpanMinimum=false` — min-speed-действия для этого угла НЕ эмитятся (min-speed вклад нейтрализуется, как в M3 silent-fallback). Следствие: плоские t03/t09 молчат по min-speed (там нечего советовать); у входов шикан (t04/t09) min-speed-совет несёт парный нижестоящий элемент t05/t10, чей спан реально содержит апекс — совет по шикане звучит корректно и стабильно, без граничного шума. `delta_ms` и прочие метрики этих углов не затрагиваются. Явная «парная» модель (объединённый спан шиканы) — возможное будущее улучшение, в этот pack не входит.

## Цель и назначение

Фаза 3 получила вердикт **NO-GO для Phase 4**, потому что слой детекции выдаёт фактически неверные числа даже на personal-best круге. Эталонный прогон — сессия `20260701-171602-738` (Monza / BMW M4 GT3, 1 чистый PB-круг, `lap_time_ms=114849`, `is_pb=1`, `is_clean=1`, дельта круга к прежнему reference **−1381ms**). На этом круге детекция породила как минимум две доказанные лжи:

- **«3929мс Curva Grande» (`monza_t03`)** — время reference на прохождение поворота, выданное как потеря водителя (на самом деле flat full-throttle kink);
- **«Сектор 1: 14799мс loss»** — потеря с **инвертированным знаком** на секторе, который был *самым быстрым S1 дня* (S1 на ~473ms быстрее reference).

Этот pack делает выдаваемые числа **правдивыми**. После него ре-валидация по ground-truth (105201 декодированный MCAP-кадр) должна подтвердить корректность эмитируемых чисел — это и есть gate, снимающий NO-GO.

## Область (scope)

**В scope:** M27 (pit-lane в clean-предикате), M1 (coachable-lap gate на emission и все аккумуляторы), M2 (span alignment) + M24 (min-speed/throttle-кернелы по полному span), M25 (median/best sector-delta вместо mean-of-crossings), M16 (расширение brake-окна вверх по трассе), M3 (plausibility-guard), и завершающий exit gate — ground-truth ре-валидация.

**Вне scope (флаги scope-tension → Open Questions):** полный rework `corner_catch_all`/`sector_catch_all` (M21/P2), reference-versioning (M37), RU-eval gate (M18 — параллельный отделяемый трек), любое изменение `.proto`/контракта. Все семь фиксов достижимы **без изменения proto** — это value-correctness внутри существующих полей.

**Жёсткие конвенции (CLAUDE.md), которые план обязан соблюдать:** records/init-only; `_camelCase` включая `private static readonly`; `var` IDE0007/0008; `TreatWarningsAsErrors` ON; никаких magic numbers (всё через `IOptions<ComputeOptions>` + `EnsureValid`); русский пользовательский текст только в prompt/`.resx`; `System.Text.Json`; **каждая задача = ОДИН conventional commit**, без `Co-Authored-By`; `ComputeSession` остаётся в `SimCoach.Reference`.

---

## Одна архитектурная истина, которую обязан усвоить каждый планировщик задач

`ComputeSession.Accept(frame)` (`ComputeSession.cs:96-115`) вызывает `EmitCorner` и `EmitSector` **потоково, покадрово, во время круга** — *до* того как круг закрылся. Но вердикт «clean» (`CompletedLap.IsClean`) известен только в `HandleLap` (конец круга). Поэтому «coachable-lap gate» **не может** просто прочитать `IsClean`: в момент emission цельно-круговая чистота ещё неизвестна.

- **Доступно покадрово в момент emission:** `frame.IsInPitLane`, `frame.IsValidLap`, `frame.TyresOut`, и «начался ли уже bounded-круг» (`_startedAtLine`, приватное в `LapSegmenter.cs`). Out-lap-кадры предшествуют первому пересечению стартовой линии.
- **Ретроспективно (только в конце круга):** цельно-круговой «clean», финальный `is_pb`, и — критично — **best-lap deficit измеряется в `HandleLap` против *pre-update* reference** (см. TASK 6): `deltaMs = LapTimeMs - _reference.TMsFromLapStart[^1]` (`ComputeSession.cs:262-263`), а `_reference` **перезаписывается** тем же PB-кругом строкой `_reference = self` (`ComputeSession.cs:297`).

Это единственное самое важное конструктивное ограничение и оно порождает **Q1** (архитектура emit-пути) и жёстко определяет, где брать comparand для M3.

---

## Порядок задач (каждая задача = один commit)

| # | Task | Commit subject | Зависит от |
|---|------|----------------|-----------|
| 1 | **M27** | `fix(pipeline): exclude pit-lane laps from clean predicate` | — |
| 2 | **M1** | `feat(compute): gate corner/sector emission and accumulators to coachable laps` | M27 (soft) |
| 3 | **M2+M24** | `fix(compute): measure corner self-delta over the full [Start,End] span` | M1 (order) |
| 4 | **M25** | `fix(reference): aggregate sector deltas by median of clean laps, not mean` | M1 |
| 5 | **M16** | `fix(reference): extend brake window upstream to the real braking zone` | **M2/M24 (жёсткая — общий буфер)** |
| 6 | **M3** | `feat(reference): plausibility-guard implausible corner/sector losses before phrasing` | M1 + M2 |
| 7 | **Exit gate** | `test(reference): add ground-truth revalidation exit gate for detection truthfulness` | ВСЕ выше |

---

## TASK 1 — M27: `IsInPitLane` в clean-предикат

**commit:** `fix(pipeline): exclude pit-lane laps from clean predicate`

### Цель
Круг, заезжавший в пит-лейн, не должен считаться «clean». Сейчас может: измеренная сессия 151452 lap4 (S1=63254ms, пит-круг) была `is_clean=1` и раздула `clean_lap_count` до 2. Fuel-путь уже исключает пит-кадры (`ComputeSession.cs:243` `!f.IsInPitLane`), а clean-предикат — нет; две трактовки «racing lap» расходятся. `[ПД#E]`

### Файлы (file:line)
- `src/SimCoach.Pipeline/Segmentation/CleanLapPredicate.cs:29` — единственное дисквалифицирующее условие.
- Потребители (read-only, поведение ужесточается): `LapSegmenter.cs:114` → `CompletedLap.IsClean` → `ComputeSession.cs:249` (`clean`) → `_cleanLapCount/_cleanLapSumMs/_bestSectorMs/_endTyreWearPct` (`:252-256`), `ConsistencyStddevMs`, `TheoreticalBestGapMs`, `LapEvent.is_clean`, PB-selection (`:265-271`), и re-check в `ReferenceStore.MaybeUpdate` (`ReferenceStore.cs:52`).

### Подход
Добавить `frame.IsInPitLane` в дисквалификатор `CleanLapPredicate.cs:29`:
`if (!frame.IsValidLap || frame.IsInPitLane || frame.TyresOut != 0 || (frame.FlagsActive & DisqualifyingFlags) != 0)`.
Обновить XML-doc summary (`:18`). **Не** трогать fuel-gate (`ComputeSession.cs:243`) и **не** консолидировать три call-site в один предикат — это Q5 (владеет M1), вне scope этой задачи ради атомарности и обратимости коммита.

### Риски
- Ужесточение «clean» снижает eligibility для reference-seed и consistency-input; на короткой сессии с единственным пит-смежным кругом `clean_lap_count` может упасть до 0 и обнулить `ConsistencyStddevMs`/PB. Это *ожидаемая* коррекция, но любой golden-fixture, скармливавший пит-кадр в «clean» круг, перевернётся — grep фикстуры на `IsInPitLane = true`.
- `CornerEventBuilder.OffTrack` (`:106-117`) независимо проверяет `IsValidLap`/`TyresOut` и **не** пит — M27 намеренно не меняет corner off-track labeling; зафиксировать асимметрию, чтобы будущий reviewer её «не починил».

### Тесты
- Новый `tests/SimCoach.Pipeline.Tests/Segmentation/CleanLapPredicateTests.cs`: круг с одним `IsInPitLane=true` кадром → `IsClean == false`; полностью valid non-pit → `true`; регрессия на существующие valid/tyres-out/flag кейсы.
- Расширить `LapSegmenterTests.cs`, если он ассертит `IsClean` на pit-содержащем круге.
- Exit gate (общий): out-lap/in-lap (11806 пит-кадров) больше не `is_clean`; `clean_lap_count == 1`.

### Acceptance
1. Любой круг с ≥1 `IsInPitLane`-кадром → `IsClean == false`.
2. `clean_lap_count` на эталонной сессии = 1.
3. `dotnet build` + `dotnet format` чисто; no proto change.

**Blocked-pending:** Q5 (shared vs distinct predicate) — но M27 намеренно минимален и не зависит от исхода Q5.

---

## TASK 2 — M1: Coachable-lap gate на emission и все аккумуляторы

**commit:** `feat(compute): gate corner/sector emission and accumulators to coachable laps`

### Цель
Прекратить эмиссию corner/sector-событий и подпитку session-аккумуляторов на кадрах, не входящих в *coachable* (racing / non-pit / valid / bounded) круг. Это фикс, убивающий инвертированный **«+14799ms S1 loss»** на PB-круге: out-lap S1 (66535ms, включая pit exit) сейчас усредняется в `sector_avg_delta_ms`, потому что `EmitSector` срабатывает на каждом пересечении без lap-context gate. `[#B, #C, #E; addendum §2.3, §4]`

Scope — *только механизм gate*. M1 **не** чинит span-mismatch (это M2; self-vs-self replay подтверждает, что 3929ms Curva Grande переживает M1), **не** меняет статистику агрегации сектора (mean→median — это M25) и **не** добавляет pit в clean-предикат (M27). M1 — несущий prerequisite, убирающий контаминированные сэмплы, чтобы M25/M3 работали на доверенных входах.

### Файлы (file:line)
- `src/SimCoach.Reference/ComputeSession.cs`
  - `:87-125` `Accept` — покадровый цикл; добавить сопровождение live coachable-флага вверху (после `InitSession`), gate вызова `EmitCorner` (`:101`) и `EmitSector` (`:108`).
  - `:195-205` `EmitCorner` — `_domain.Publish` + `_lapLosses.Add` + `_sessionLosses.Accept` (`:201`) + balance-аккумуляторы (`:202-204`) подавляются, когда текущий круг не coachable.
  - `:207-235` `EmitSector` — `_sectorDeltaAccum` (`:220-221`) и `_domain.Publish` (`:232`) подавляются, НО bookkeeping `_prevSectorCrossPos` (`:234`) должен продолжать продвигаться (см. Риски).
  - `:421-430` `ResetForNextLap` — очистка poison-latch здесь (re-arm на каждом пересечении, рядом с `_lapLosses.Clear()`).
  - Новое приватное поле у `:61`: `private bool _lapPoisoned;`.
- `src/SimCoach.Pipeline/Segmentation/LapSegmenter.cs:22` — `_startedAtLine` (private) экспонировать read-only свойством (`HasStartedLap => _startedAtLine`), зеркаля существующее `CrossedThisFrame` (`:37`). Out-lap-кадры имеют `_startedAtLine == false`.
- `src/SimCoach.Reference/SessionLossAccumulator.cs:24-42` — без изменений класса; он просто больше не вызывается на poisoned-кругах (его фильтр `DeltaMs <= 0` остаётся, ортогонален M1).
- `src/SimCoach.Reference/ComputeOptions.cs:7-35` — дом для toggle, ЕСЛИ он добавится; рекомендация — не добавлять (предикат — чистая булева логика, никаких порогов → без magic numbers). См. Q1.

### Подход (рекомендация: frame-level latch, pending Q1)
1. Добавить `private bool _lapPoisoned;`.
2. Вверху `Accept` (после `InitSession`, `:94`): `if (frame.IsInPitLane || !frame.IsValidLap) { _lapPoisoned = true; }` (TyresOut намеренно исключён из M1 — принадлежит off-track/clean labeling; сворачивание — решение Q5).
3. `private bool CurrentLapCoachable => !_lapPoisoned && _lapSegmenter.HasStartedLap;` — терм `HasStartedLap` отбрасывает out-lap-кадры до первого пересечения стартовой линии (сессии 162041/165856 с `lap_count==0`, #C).
4. Gate в call-site внутри `Accept`:
   - `:96-103` — `tracker.Accept(frame)` вызывать **безусловно** (трекеры должны продолжать работать для window-state и re-arm), но `EmitCorner` — только при `CurrentLapCoachable`.
   - `:105-109` — `_sectorSegmenter.Accept(frame)` безусловно; `EmitSector` — только при `CurrentLapCoachable`. Для сохранения непрерывности `_prevSectorCrossPos`: (предпочтительно) всегда выполнять position-bookkeeping, gate-ить только publish + запись `_sectorDeltaAccum` внутри `EmitSector`. **Не** пропускать всё тело `EmitSector`, иначе следующая sector-delta на том же круге посчитается от устаревшей prev-position.
5. `_lapPoisoned = false;` в `ResetForNextLap` (`:421-430`), который уже выполняется на каждом пересечении (`:121-124`). Это re-arm — poisoned-круг не отравляет сессию навсегда.

`ResetForNextLap` выполняется в *конце* `Accept` на кадре-пересечении, после `HandleLap`; latch управляет текущим накапливающимся кругом и очищается с началом следующего. На самом кадре-пересечении трекеры не срабатывают, sector-split не ожидается — stale-latch на кадре-пересечении не проблема; отразить в тесте.

Lap-level аккумуляторы `HandleLap` не трогать — `_cleanLapSumMs`/`_bestSectorMs`/`_endTyreWearPct` уже gated на `clean` (`:249-257`), fuel на `!IsInPitLane` (`:243`). Утечка — не там, а в mid-lap emission-пути.

### Риски
- **Поздний mid-lap poison (Q1).** Latch подавляет все дальнейшие emit после первого poison-кадра, но событие, уже опубликованное ранее на круге, который *позже* нырнул в пит, un-emit-ить нельзя. Три *измеренных* контаминанта (out-lap, in-lap/pit, invalid) все ловятся frame-level latch, т.к. pit/invalid-кадры на этих кругах предшествуют coachable-поворотам. Полностью корректная обработка «clean-повороты, затем dirty-хвост» требует buffer-and-flush в `HandleLap` (Q1 опция b) — автономно не внедрять.
- **Дрейф `_prevSectorCrossPos`** при wholesale-пропуске `EmitSector` — mitigated шагом 4.
- **Сдвиг balance/understeer trend.** Подавление `EmitCorner` роняет `_understeerAccum`/`_oversteerAccum`/`_balanceCornerCount` → `SessionEvent.understeer_trend` меняется для сессий с out/in-lap. Это намеренно (сэмплы были мусором), но сдвинет golden-fixtures.
- **Tracker re-arm.** НЕ gate-ить `tracker.Accept`/`_sectorSegmenter.Accept` — только emit-вызовы. Gate трекеров оставит их latched и сломает re-arm.
- **Downstream** (`SessionLossAccumulator`/`GoldArtifactBuilder`/action-registry) просто видят меньше событий; проверить, что ни один consumer не предполагает ≥1 corner/sector-события на сессию (пустой `aggregated_losses` уже proto3-valid, `:155`).

### Тесты
- **Unit (новое, `ComputeSessionTests.cs` через `ComputeTestHarness`):**
  - Синтетический поток: out-lap с `IsInPitLane=1` и/или до первого пересечения → чистый flying-круг → in-lap с `IsInPitLane=1`. Ассерт: ни одного `CornerEvent`/`SectorEvent` для out/in-lap; `SectorEvent.DeltaMs` и `SessionEvent.SectorAvgDeltaMs` отражают только flying-круг (нет 66535ms-poisoned mean).
  - `IsValidLap=0` mid-lap latches poison: дальнейших emit нет; следующий круг (после пересечения) эмитит нормально (доказывает re-arm).
  - `lap_count==0` out+in-only поток (сессии 162041/165856): ноль corner/sector-событий, пустой `aggregated_losses`.
  - **Late-dirty residual (pin Q1):** круг, эмитирующий валидные повороты, затем ныряющий в пит в самом конце — ассертит, что уже-опубликованные ранние повороты остаются (latch не un-emit-ит), а хвост подавлен. Документирует границу frame-level latch.
- **Обновить:** `SessionLossAccumulatorTests`, `Phase2ComputeE2EGoldenTests`/`GoldSessionArtifactTests` golden-значения (understeer_trend, sector_avg_delta), любой replay-тест, считающий события.
- **Exit gate (общий):** дебриф без S1-loss, S1 показывает gain (negative) или опущен; out/in-lap-пересечения не вносят ничего в sector-average.

### Acceptance
1. На CSV/replay out-lap и in-lap вносят **ноль** в любой corner-event, sector-average, session aggregated-loss.
2. Дебриф без Sector-1-loss; S1 — gain или опущен.
3. Трекеры корректно re-arm между кругами.
4. No proto change; `HandleLap` lap-level аккумуляторы не тронуты.
5. Build + format чисто; unit-тесты зелёные; golden-fixtures re-based с объяснением сдвига в теле коммита.
6. **Known limitation (pending Q1):** corner/sector-события, эмитированные до того как круг **поздно** нырнул в пит, не un-emit-ятся; это ограничено тем, что такие события — подлинные racing-pace сэмплы, отравленный хвост подавлен latch, а M3 бэкстопит magnitude/sign; полная корректность — Q1 опция (b).

**Blocked-pending:** Q1 (emit-path архитектура), Q5 (shared vs distinct predicate). Реализовать рекомендованный frame-level latch так, чтобы поздний своп на buffer-and-flush (Q1 опция b) локализовался в `ComputeSession`.

---

## TASK 3 — M2 (+M24): Span alignment корнера

**commit:** `fix(compute): measure corner self-delta over the full [Start,End] span`

### Цель
Убить класс фантомных потерь «3929мс Curva Grande». Self-сторона каждого `CornerEvent.delta_ms` должна измерять время водителя-time-at-position по **той же геометрической `[corner.StartPosition, corner.EndPosition]`**, что и reference, вместо collapsed-throttle-resume-буфера. Как прямое следствие (M24) — считать **all self-derived kernels** (`min_speed`, `trail_brake`, balance, wheelspin, brake_overlap, steering_jitter) по под-окну `[Start,End]`, чтобы кинематика бралась с реального поворота, а не с 2-кадрового огрызка. Corner-exit (throttle-resume) точка сохраняется только для exit-специфичного `throttle_resume_diff_m`.

Эталонный сбой (§2.2): self-vs-self replay на PB-круге дал `t03=-3928, t06=-3392, t09=-2770, t11=-6072`, сумма ≈−24…−27s фантомного «gain» против истинной дельты круга −1381ms. Корень — span-mismatch, поэтому **M1 это не чинит — чинит M2.**

### Корень (verified in tree)
- `CornerTracker.Accept` (`CornerTracker.cs:36-71`): arming — `if (pos < Corner.StartPosition || pos > Corner.EndPosition) return null;` (`:39`), т.е. **буфер стартует ровно на `StartPosition` и содержит НОЛЬ кадров выше по трассе.** Затем return-to-throttle trigger срабатывает первым (`:58-60`: `pastMinSpeed && ThrottlePct >= _resumeThrottlePct`), с window-end-crossing лишь как backstop (`:65-67`). На flat full-throttle kink min-speed сидит на входе, throttle уже ≥0.5 на 2-м кадре → `Fire()` возвращает ~2-кадровый буфер.
- `CornerEventBuilder.Build` (`:79-81`): `selfDurationMs = DurationMs(selfFrames)` (helper `:119-124` — уже `selfFrames[^1].T - selfFrames[0].T`); `refDurationMs = refLap.TMsFromLapStart[k1] - refLap.TMsFromLapStart[k0]` (`:80`) — ref-время по **полному** grid-slice `[k0,k1]` (`:63-64`, ≈3931ms). `deltaMs = 2 − 3931 ≈ −3929`.
- То же 2-кадровое окно скармливается **всем** self-кернелам (`:34-49`) → `min_speed_diff_kmh` и т.д. на огрызке (M24).

### Файлы (file:line)
- `src/SimCoach.Reference/CornerTracker.cs:29-85` — переписать окно: fire на window-end (`pos > Corner.EndPosition`) как **primary** close; удалить throttle-resume early-fire (`:58-60`) и **ныне-unused** поле `_resumeThrottlePct` (`:13,23,59`) + параметр конструктора (`:20-24`). (Unused private field сломает build под `TreatWarningsAsErrors`.) Arming остаётся на `StartPosition` — **upstream-расширение arming принадлежит M16, НЕ M2** (см. TASK 5 revision).
- `src/SimCoach.Reference/CornerEventBuilder.cs:79-81` — **явно выбирать под-окно `[Start,End]`** для self-измерения (см. Подход шаг 2). **`DurationMs` (`:119-124`) остаётся байт-в-байт как есть** — это НЕ функциональная правка M2; функциональный фикс M2 — переписка окна трекера (какие кадры попадают в буфер). НЕ помечать `DurationMs` как «ядро M2» и НЕ удалять его без причины.
- `src/SimCoach.Reference/ComputeSession.cs:416-418` — `RebuildCornerTrackers` роняет аргумент `_options.ResumeThrottlePct`.
- `src/SimCoach.Reference/ComputeOptions.cs:10,20-22` — судьба осиротевшего `ResumeThrottlePct` (см. шаг 4).
- Тесты: `CornerTrackerTests.cs`, `CornerEventBuilderTests.cs`, `tests/SimCoach.Pipeline.Tests/Kernels/ComputeKernelsTests.cs`.

### Подход (один commit; M2 и M24 делят переписку CornerTracker)
1. **Расширить окно трекера (общее M2/M24).** В `CornerTracker.Accept` сохранить `_active`/`_emitted`/min-speed-bookkeeping (`:36-56`), сделать `pos > Corner.EndPosition` (`:65`) **primary** fire и удалить throttle-resume early-fire (`:58-60`). `Fire()` возвращает каждый кадр от arming-точки до первого-за-`End`. `_minIndex`/`_minSpeed` остаётся.
2. **Self span measurement (ядро M2) — ОБЯЗАТЕЛЬНО суб-окно, никогда raw endpoints.** В `Build` выбрать под-окно из буфера с `pos ∈ [StartPosition, EndPosition]` (bracket-кадры) и измерять self-time по нему. **Причина, почему нельзя брать `selfFrames[0]`/`selfFrames[^1]` напрямую:** как только M16 сдвинет arming-точку вверх по трассе, `selfFrames[0]` окажется upstream от `StartPosition`, и в **каждый** поворот вольётся паразитная положительная дельта. Baseline: первый/последний буферизованный кадр *внутри* `[Start,End]`. Опциональное уточнение (только если exit gate покажет систематический bias): линейно интерполировать timestamp на точных `StartPosition`/`EndPosition` из bracketing-кадров (self-аналог `GridMetrics.TimeAt`). **То же суб-окно `[Start,End]` использовать для ВСЕХ delta/min-speed/kinematic кернелов (M24)** — не гнать их по расширенному M16-буферу.
3. **Сохранить exit-кернел.** `throttle_resume_diff_m` (`:87-89`) использует `speedSelf/RefThrottleOnPosition` из `ThrottleSpeedKernels.Analyze`, находящий первый устойчивый throttle после min-speed по (теперь полному) окну — семантически resume-точка. Одно окно анализа `[Start,End]`; resume-точка выводится внутри кернела, не усечением буфера.
4. **Судьба dead-config (default: descope-fallback — держим span-commit одной логической правкой).** Удалить early-fire + unused `_resumeThrottlePct` (поле/ctor-param/`RebuildCornerTrackers`-аргумент — требуется `TreatWarningsAsErrors`). **Оставить** `ThrottleSpeedKernels` const `0.5f` как есть; **оставить** `ComputeOptions.ResumeThrottlePct` с его `EnsureValid`-guard плюс one-line comment «больше не gate-ит emission». **НЕ** протаскивать `ResumeThrottlePct` в Pipeline-кернел в этом коммите — это связало бы Pipeline-константу с Reference-config и сделало coaching-порог config-driven, зарытым в span-fix. Если reconciliation нужен — отдельный follow-up commit. Open question не нужен (значение неизменно); отметить в теле коммита.

### Риски
- **Emission-latency / materiality (MAJOR → Q2).** Fire на геометрическом конце вместо throttle-resume сдвигает *когда* эмитит каждый поворот и меняет **каждое** self-кернел-поле. Не land-ить без sign-off по Q2.
- **Взаимодействие с M16 (см. Judge revision, dependency-graph).** M2 и M16 **НЕ** независимо-revertible: M16 мутирует ровно тот `CornerTracker`-буфер, из которого M2 читает delta-математику. Реверт любого требует ре-проверки инварианта `[Start,End]`-slice. Шаг 2 (суб-окно) — контракт, защищающий M2 от M16.
- **Вырожденные/короткие повороты.** Поворот, где машина не достигает `EndPosition` до пересечения старт-линии, не срабатывает — как и сегодня (backstop уже был `pos>End`). Нового failure-mode нет.
- **Unused-field build break.** `_resumeThrottlePct` assigned-not-read → удалить в том же коммите.

### Тесты
- **CornerTracker:** `DriveCorner` (`CornerTrackerTests.cs:45-50`) сейчас ждёт fire на throttle-stab кадре; обновить на span-to-`EndPosition`. Обновить два `new CornerTracker(_corner, resumeThrottlePct: 0.5f)` (`:25,37`) под дропнутый param. Сохранить re-arm-after-Reset и no-double-fire инварианты.
- **CornerEventBuilder:** flat full-throttle синтетический поворот с **DISTINCT synthetic reference (не self-кадрами)**, throttle ≥0.5 всюду → ассерт `|delta_ms| ≤ ~10ms` (one-frame tolerance @333Hz), НЕ ≈`-refDuration`; ловит endpoint-asymmetry херметично без обязательной интерполяции (residual bias sub-frame, оба endpoint перелетают вперёд и в основном сокращаются). Добавить реально-медленнее поворот → `delta_ms > 0` корректной величины. Ассерт `min_speed_diff_kmh` отражает истинный min по под-окну (M24).
- **ComputeKernels:** `ThrottleSpeedKernels` по `[Start,End]` окну возвращает истинный min-speed индекс/позицию и resume-позицию.
- **Exit gate (срез этой задачи):** оракул считает per-corner time-at-position по `[Start,End]` независимо; ассерт: `t03` в coaching-tolerance от истины, `abs != ~3929`; «3929мс» никогда не эмитится. Честь reference-audit caveat (§4): assert **relative**, не bit-exact ref absolutes.

### Acceptance
- Self-delta по под-окну `[Start,End]` (никогда raw buffer endpoints); throttle-resume trigger управляет только `throttle_resume_diff_m`.
- На CSV: `t03` в tolerance от истины (~0, не −3929); нет «3929мс» ни в одной из 5 сессий.
- M24: все self-derived кернелы считаются по `[Start,End]` под-окну; ни один не работает на 2-кадровом огрызке для `t03/t09/t10/t11`.
- Build green под `TreatWarningsAsErrors`; `dotnet format` чисто; no proto change; счётчики corner/sector-событий неизменны (один event на поворот на круг).

**Blocked-pending:** Q2. Содержит M24 — не разбивать.

---

## TASK 4 — M25: median/best sector-delta вместо mean-of-crossings

**commit:** `fix(reference): aggregate sector deltas by median of clean laps, not mean`

### Цель
`SessionEvent.sector_avg_delta_ms` (proto field 14) — сейчас арифметическое **среднее по каждому пересечению сектора**, эстиматор, породивший инвертированный «−14.8s S1» (out-lap S1=66535ms усреднён с flying S1=35994ms → ~14800ms). M1 убирает out-lap *сэмпл*; M25 дополнительно заменяет *эстиматор*, чтобы мультикруговые сессии репортили представительную (median) clean-lap sector-delta. `[СИС#4; §2.3]`

### Файлы (file:line)
- `src/SimCoach.Reference/ComputeSession.cs:35` — декларация `_sectorDeltaAccum` (сейчас `Dictionary<int,(long Sum,int Count)>`).
- `:220-221` — накопление внутри `EmitSector`.
- `:411-414` — `SectorAvgDeltas()` (возвращает `Sum/Count`).
- Downstream (read-only): `GoldArtifactBuilder.cs:91`.

### Подход
- Сменить `_sectorDeltaAccum` на `Dictionary<int, List<int>>` (per-sector список per-crossing `deltaMs`).
- В `EmitSector` (`:220-221`) добавлять `deltaMs` в список сектора вместо суммирования. Т.к. M1's gate обёртывает `EmitSector`, в список попадают только coachable-lap пересечения — M25 наследует фильтр.
- Заменить `SectorAvgDeltas()` (`:411-414`) на агрегацию над списком: **median** (рекомендация) или **best = min-by-absolute-value** — статистика есть Q2b (escalate). Детерминированный порядок по sector-key (`OrderBy(pair => pair.Key)`). Median чётного списка → в один helper с документированным rounding (lower-median или mean-of-two).
- Выбор агрегации не magic-number-литералом; если нужен toggle — enum-typed опция в `ComputeOptions` с `EnsureValid`.

### Риски
- **Soft-contract semantic change:** field 14 назван `sector_avg_delta_ms`, но понесёт median/best — smыsl изменён (wire-type неизменен). Rename proto — MAJOR, вне scope; молчаливое изменение смысла требует sign-off (Q2b). Не переименовывать.
- `_sectorDeltaAccum` — **session-level** аккумулятор (не очищается в `ResetForNextLap`) — список спанит всю сессию, что корректно для per-session агрегата. Не добавлять в per-lap reset.

### Тесты
- `ComputeSessionTests.cs` (+`ComputeTestHarness`): сессия с двумя clean flying-кругами, S1-deltas {-473, -20}; ассерт `SectorAvgDeltaMs[0]` равен выбранной статистике и negative (gain), не mean-poisoned.
- Проверить, что `GoldSessionArtifactTests.cs`/`GoldTestData.cs` (ставят `SectorAvgDeltaMs` прямо на proto) — green.
- Exit gate: S1 session-delta — gain (negative) или опущен, никогда +14799.

### Acceptance
1. `SectorAvgDeltas()` возвращает median (или согласованную статистику) per-sector coachable-lap deltas.
2. На эталонной сессии S1-аггрегат ≈ flying-lap S1-delta (~−473ms sign/order), не +14799.
3. No proto/wire change; build + format чисто.

**Depends:** M1 (список содержит только coachable-lap пересечения). **Blocked-pending:** Q2b (median↔best — дизайн: список + один helper агрегации, своп в одну строку).

---

## TASK 5 — M16: расширить brake-окно ~200m вверх по трассе

**commit:** `fix(reference): extend brake window upstream to the real braking zone`

### Цель
Реальное ACC-торможение начинается за 41–290m **до** геометрического начала поворота (steer-in). Поскольку self/ref brake-окна начинаются на `Corner.StartPosition`, `brake >= 0.15` часто не встречается в окне → `BrakeOnPosition` = null → `brake_point_diff_m` откатывается к `corner.StartPosition` для обеих сторон и **схлопывается в 0** (хуже всего на Parabolica). Расширить оба окна ~200m вверх, чтобы brake-onset реально наблюдался. `[СИС#5; §3]`

### ВАЖНО (Judge revision — устранение tension с M2): M16 ВЛАДЕЕТ upstream-arming
Verified: `CornerTracker.cs:39` arming на `pos >= StartPosition` → буфер содержит **ноль** кадров upstream от Start. «Расширенный self-буфер, который M2/M24 якобы производят» **не существует** — M2 меняет только точку fire, не arming. Поэтому:
- **M16, а не M2, владеет upstream-armingом** (M16 держит `BrakeWindowUpstreamM`).
- M16 сдвигает arming-точку вверх для **обеих** сторон симметрично; в `BrakeKernels` подаётся **только** pre-roll срез для `BrakeOnPosition`.
- **Инвариант M2 сохраняется:** M2's delta/min-speed/kinematic под-окно остаётся строго `[Start,End]` (TASK 3 шаг 2) — upstream-кадры туда **не** попадают.
- Удалить из M16 ложное утверждение «M2/M24 уже производят upstream-буфер».

### Файлы (file:line)
- `src/SimCoach.Reference/ComputeOptions.cs:10` — добавить `BrakeWindowUpstreamM` (float, метры) рядом с `ResumeThrottlePct`, с `EnsureValid >= 0`.
- `src/SimCoach.Reference/CornerTracker.cs:39` — arming-условие: буферизовать с `pos >= StartPosition - upstreamNormalized` (обе стороны трекеров arm-ятся раньше). `Fire()` и primary-close (M2) неизменны.
- `src/SimCoach.Reference/CornerEventBuilder.cs:62-89` — ref-side grid-slice: для brake-scan использовать `k0Brake = max(0, k0 - upstreamGrid)`; **delta/min-speed slice остаётся `[k0,k1]`**. Brake-fallback (`:83-85`) неизменен.
- `src/SimCoach.Pipeline/Kernels/BrakeKernels.cs:47-51` — логика не меняется; получает pre-roll кадры → `onPosition` (`:50`) становится non-null.

### Подход
- Добавить `BrakeWindowUpstreamM` с `EnsureValid >= 0`. **Значение = Q4** — placeholder default `200f`, tagged `// TODO(Q4)`.
- **Ref-сторона:** `upstreamGrid = round(BrakeWindowUpstreamM / lapLengthM * gridLength)` (`lapLengthM` доступен `ComputeSession.cs:44`, передаётся в `Build`), `k0Brake = max(0, k0 - upstreamGrid)`, гнать `BrakeKernels.Analyze` по `SliceToFrames(refLap, k0Brake, k1)` **только для brake-scan**.
- **Self-сторона:** взять brake-scan срез из расширенного (M16-arming) `CornerTracker`-буфера, фильтруя `pos >= StartPosition - (BrakeWindowUpstreamM / lapLengthM)`. Two-window дисциплина: brake-scan срез локален для `BrakeOnPosition`; `TrailBrakePct`/off-track/racing-line/min-speed/delta остаются на `[Start,End]` под-окне (M2's контракт).
- `brake_point_diff_m` (`:83-85`) использует теперь-non-null `BrakeOnPosition` с обеих сторон; сохранить `?? corner.StartPosition` fallback для genuinely brake-free кейса.

### Риски
- **Симметричное расширение обязательно:** одностороннее → систематический offset в `brake_point_diff_m` (мини-M2). Расширять self и ref на ту же метрическую дистанцию.
- **Не рутить widened slice в другие кернелы.** `TrailBrakePct` (`BrakeKernels.cs:73`) считается по corner-окну — держать widened slice локальным для `BrakeOnPosition`.
- Метры↔normalized требует `lapLengthM > 0`; guard за тем же `hasReference`-путём (`CornerEventBuilder.cs:52-60`).
- **Coupling с M2 (dependency-graph, rollback):** M16 мутирует `CornerTracker`-arming, из буфера которого M2 читает. **M2 и M16 не независимо-revertible** — реверт любого требует ре-проверки, что M2's delta-математика по-прежнему берёт строго `[Start,End]` под-окно, а не расширенный буфер.

### Тесты
- `CornerEventBuilderTests.cs`: поворот с торможением upstream от Start → `brake_point_diff_m` non-zero, matches метрический offset self/ref onset; brake-free kink → fallback ~0. **Ассерт-регрессия M2:** `delta_ms`/`min_speed_diff_kmh` НЕ сдвинулись от расширения arming (доказывает изоляцию под-окна).
- `ComputeKernelsTests.cs` (`BrakeKernels`): `BrakeOnPosition` найден, когда onset-кадры предшествуют Start в pre-roll.
- `ComputeOptions.EnsureValid` negative-value тест.
- Exit gate: Parabolica `brake_point_diff_m` non-zero и matches true onset-vs-reference offset.

### Acceptance
1. Self и ref arming расширены на ту же конфигурируемую метрическую дистанцию вверх от `corner.StartPosition`.
2. Parabolica (и t05/t09/t10/t11) репортят non-zero `brake_point_diff_m` matching истину в tolerance.
3. Upstream-расширение затрагивает **только** brake-onset scan; `delta_ms`/`min_speed_diff`/`TrailBrakePct` остаются на `[Start,End]` под-окне (M2's контракт не нарушен).
4. Distance `IOptions`-driven (без magic-number); build + format чисто; no proto change.

**Depends:** M2 + M24 (жёсткая — делят `CornerTracker`-буфер; M16 arm-ит, M2 slice-ит). **Blocked-pending:** Q4.

---

## TASK 6 — M3: Plausibility-guard перед render/phrasing

**commit:** `feat(reference): plausibility-guard implausible corner/sector losses before phrasing`

### Цель
Алгоритмический guard **между детекцией и phrasing**, подавляющий (drop) любую эмитированную corner/sector-потерю, чей **знак или величина противоречат дельте круга** — последняя линия обороны, чтобы сфабрикованное число не достигло русского текста, даже если M1/M2 регрессируют. Конкретно предотвращает corner-cadence `«3929мс»` Curva Grande (*gain*, отрендеренный как loss) и дебриф `«Сектор 1: 14799мс»` (positive sector-loss на круге, выигравшем −1381ms). Guard сравнивает с **дефицитом круга / знаком дельты**, **никогда** с sector-absolute time (ловушка `14799 < 35994`, §7 item 4). `[СИС#3][ПД#D]`

### Зачем guard нужен даже после M1/M2 (defence-in-depth)
M2 чинит *число*; M1 убирает out-lap *сэмплы*; M3 — belt-and-suspenders, предполагающий что оба могут отказать. Acceptance требует: *с M1+M2, откачёнными в тест-харнесе*, M3 в одиночку всё ещё подавляет sign-inverted S1-loss и невозможный Curva Grande loss.

### Load-bearing факты из кода (ограничивают, где guard может жить)
1. **Рендеренные числа достигают текста ДВУМЯ путями, TipValidator не guard-ит надёжно ни один.** LLM-путь валидируется только по *структуре* (`TipValidator.cs:15-133`, **без числовой проверки**). Template-путь **обходит TipValidator целиком**: realtime fallback `ComposeRealtimeTip(..., TipSource.Template, ...)` (`CoachService.cs:270`), дебриф `DebriefTemplate.BuildJson(...)` (`CoachService.cs:383`). ⇒ Validator-only guard структурно недостаточен. Guard должен сидеть upstream обеих веток.
2. **Механизм corner «3929мс» подтверждён.** `corner_catch_all` (`Data/actionRegistry.json:237-251`) fire на `delta_ms abs_gt 150`, рендерит `loss = abs_round0(delta_ms)` (`ParamTransform.AbsRound0` дропает знак). `GoldCornerEvent.DeltaMs = -3929` (gain) fire-ит catch_all и печатает `«3929мс»` как *loss*.
3. **Механизм дебриф «14799мс» подтверждён.** `SectorAvgDeltas()` (`:411-414`) → field 14 → `GoldSessionPayload.SectorAvgDeltaMs` (`GoldArtifactBuilder.cs:91`) → prompt/дебриф. `AggregatedLosses` (sign-filtered `DeltaMs>0`) → `DebriefTemplate.cs:22-30`. Оба feed phrasing без magnitude/sign cross-check.
4. **Comparand дефицита круга: capture в HandleLap, НЕ derive в Complete (Judge revision — критично).** Verified: `deltaMs` (`ComputeSession.cs:262-263`) измеряется против *pre-update* `_reference`, но `_reference = self` (`:297`) **перезаписывается** PB-кругом когда PB бьёт reference (`MaybeUpdate`). На эталоне −1381ms PB-круг **перезаписал** reference → к `Complete()` `_reference` = PB-круг и `_pbTimeMs - _reference.TMsFromLapStart[^1] ≈ 0`, что **сломало бы** Tier-B ровно на `+14799` S1-кейсе. **Фикс:** захватить дефицит в `HandleLap` внутри isPb-ветки (`:265-271`), где deltaMs ещё против pre-update reference: `private int? _bestLapDeficitMs;` set при `deltaMs is not null`. Feed сохранённое поле в Tier B в `Complete()`; degrade до Tier-A-only при null.
5. **На corner/sector-cadence дефицит круга неизвестен** (эмит mid-lap, тот же constraint что M1). Corner- и realtime-sector-guard применяют только **absolute plausibility ceiling** (Tier A); lap-relative (Tier B) — где дефицит в scope: `HandleLap` (lap `top_losses` vs `LapEvent.DeltaMs`) и `Complete` (session-аггрегаты vs `_bestLapDeficitMs`).

### Дизайн (two-tier, pure helper)
Ввести **pure static helper** `LossPlausibility` в `SimCoach.Reference` (mutation-free, golden-testable):
- **Tier A — absolute ceiling:** `bool WithinCeiling(int deltaMs, int ceilingMs)` ⇒ `Math.Abs(deltaMs) <= ceilingMs`. `|−3929| > ceiling` fail независимо от знака.
- **Tier B — deficit-relative filter:** `FilterAgainstDeficit(losses, lapDeficitMs, options)` дропает потерю, чья величина превышает `ratio × |lapDeficitMs|` (с absolute-floor против near-zero дефицита) и чей **знак противоречит** envelope дефицита (`14799` vs `−1381` круг ⇒ дропнут).

Wire-точки (рекомендованный сайт = **compute**, pending Q3a) — **ЧЕТЫРЕ**, не три:
- `EmitCorner` (`:195-205`): Tier A; если `!WithinCeiling` → нейтрализовать reference-relative loss (Q3c).
- **`EmitSector` (`:207-235`): Tier-A-only (Judge revision — четвёртый wire-point).** Per-crossing `SectorEvent.DeltaMs` feed-ит `sector_catch_all` (`actionRegistry.json:309`, `abs_gt 200`) через template-путь, который M3 иначе не инспектирует. С M1 off, out-lap-пересечение эмитит ~+30000ms `SectorEvent` → realtime tip. Lap deficit mid-lap неизвестен → Tier-A-only, зеркаля EmitCorner-нейтрализацию.
- `HandleLap` (`:291`): фильтровать `lapEvent.TopLosses` через `FilterAgainstDeficit(..., lapEvent.DeltaMs, ...)` перед `Publish`.
- `Complete` (`:156-158`): Tier B на `AggregatedLosses` и `SectorAvgDeltaMs`, используя **`_bestLapDeficitMs`** (захвачен в HandleLap, п.4 выше), degrade до inert при null.
- Sign-фильтры `TopLosses` (`:348-354`) и `SessionLossAccumulator.cs:27` (`DeltaMs>0`) остаются; M3 добавляет magnitude/deficit-измерение.

Config (без magic numbers — `ComputeOptions.cs`): `MaxPlausibleCornerLossMs` (Tier A), `LapDeficitLossRatio` + `LapDeficitFloorMs` (Tier B), каждый в `EnsureValid()`. **Дефолты coaching-material → Q3b.** Задача компилируется и проходит тесты с placeholder-дефолтами `// TODO(Q3b)`.

### Файлы (file:line)
- **New:** `src/SimCoach.Reference/LossPlausibility.cs` — pure Tier-A/Tier-B helper.
- `src/SimCoach.Reference/ComputeOptions.cs:10-34` — три knob + `EnsureValid`.
- `src/SimCoach.Reference/ComputeSession.cs`: `:195-205` (EmitCorner Tier A), `:207-235` (**EmitSector Tier A**), `:291` (HandleLap Tier B on `TopLosses`), `:265-271` (**capture `_bestLapDeficitMs`**), `:137-158` (Complete Tier B via `_bestLapDeficitMs`). Новое поле `private int? _bestLapDeficitMs;`.
- **Reference-only (НЕ править, если Q3a не выберет иначе):** `GoldArtifactBuilder.cs`, `TipValidator.cs` — документированные альтернативы; `Data/actionRegistry.json:237-251,309` — смежный `abs_gt` sign-bug (M21 scope, флаг, не фикс).

### Подход
1. `LossPlausibility` как `internal static` (или `public static` если Coach-wiring выбран в Q3a) с двумя pure-методами; пороги параметрами (helper не читает config).
2. Расширить `ComputeOptions` тремя knob + `EnsureValid` (`MaxPlausibleCornerLossMs > 0`, `LapDeficitLossRatio > 0`, `LapDeficitFloorMs >= 0`), placeholder-дефолты Q3b.
3. Захватить `_bestLapDeficitMs` в HandleLap isPb-ветке (п.4). В `Complete`: Tier B только при `_bestLapDeficitMs is not null`.
4. Corner/sector-нейтрализация по Q3c: рекомендованная форма — оставить событие опубликованным, но выставить reference-relative loss-поля так, что `corner_catch_all`/`sector_catch_all` не fire (silent fallback), non-reference кернелы intact.
5. Structured debug-логирование при drop (зеркаля M23 observability-стиль `CoachService.cs:276-283`), без новых DB-колонок.

### Риски
- **Over-suppression:** тесные пороги глушат легитимный coaching. Mitigated floor + ratio + Q3b sign-off; валидируется «unchanged на clean-PB» регрессией.
- **Sign-семантика:** круг может выиграть в целом, но потерять в одном повороте — Tier B key-ит на **magnitude vs |deficit| budget**, не наивно «loss forbidden when lap gained». `14799` ловится, т.к. одна sector-потеря dwarf-ит цельно-круговую выгоду.
- **Comparand availability:** `_bestLapDeficitMs` null без reference/PB → Tier-A-only, никогда throw.
- **Double-neutralisation с M1/M2:** после M1/M2 большинство входов корректны, guard — no-op. Тесты ассертят inert на truthful-входах.

### Тесты
- **Unit `LossPlausibilityTests` (new):** Tier A дропает `|−3929| > ceiling` (оба знака); Tier B дропает `+14799` против `−1381` дефицита; Tier B **inert** на plausible-потере в бюджете; empty/near-zero дефицит использует floor, не divide-by-zero. **Явно покрыть ловушку `14799 < 35994`** — ловится lap-deficit-сравнением, НЕ sector-absolute.
- **Unit `ComputeOptionsTests`:** `EnsureValid` отвергает non-positive ceiling/ratio и negative floor.
- **Regression (Judge revision):** сессия, где PB-круг перезаписывает reference, **всё равно** даёт `_bestLapDeficitMs ≈ -1381ms` как session-tier budget, НЕ ~0.
- **ComputeSession replay/golden:** `delta_ms=-3929` ⇒ `corner_catch_all` не fire / поворот молчит; `SessionEvent` с poisoned `sector_avg_delta_ms=+14799` и дефицитом `−1381` ⇒ sector-delta дропнут. Обновить `GoldSessionArtifactTests`, `DebriefTemplateTests`.
- **Defence-in-depth:** тест с M1/M2-логикой simulated-off (raw poisoned артефакты инжектированы) ассертит suppression — И для corner, И **для oversized out-lap-style `SectorEvent` с M1 off** (доказывает четвёртый wire-point).
- **Exit gate (общий):** нет `«3929мс»`, нет positive S1-loss.

### Acceptance
1. Pure config-driven guard; **no magic numbers** (всё в `ComputeOptions`, `EnsureValid`-checked).
2. На CSV/replay `«3929мс»` Curva Grande и `«Сектор 1: 14799мс»` подавлены, через **обе** LLM и template ветки, включая realtime per-sector cadence.
3. С M1/M2-логикой disabled в харнесе M3 в одиночку подавляет corner И sector — доказано defence-in-depth тестом.
4. Guard **inert** на truthful-входах.
5. `_bestLapDeficitMs` захвачен pre-overwrite, session-tier budget корректен даже когда PB перезаписал reference. No proto change.

**Depends:** M1 + M2 (нужна trustworthy дельта круга). **Blocked-pending:** Q3a/Q3b/Q3c — pure helper + тесты можно merge за рекомендованными дефолтами; wiring-сайт, финальные пороги и нейтрализация-форма требуют sign-off. (Нейтрализация-форма для sector идентична corner — свёрнута в Q3c.)

---

## TASK 7 — Exit gate: ground-truth ре-валидация

**commit:** `test(reference): add ground-truth revalidation exit gate for detection truthfulness`

### Цель
Доказать снятие NO-GO: после M1/M2/M3/M16/M24/M25/M27 ре-декодировать эталонную MCAP-сессию, посчитать **независимый** truth-oracle из raw-кадров, ре-прогнать реальный compute-pipeline по тем же кадрам и ассертить, что эмитированные числа matches истину в coaching-tolerance. Две headline-лжи должны исчезнуть: **«3929мс Curva Grande» (`monza_t03`)** и **«−14.8s / +14799ms S1 loss»**.

Это **verification/test-only** задача — она НЕ меняет detection-код. Если gate падает — сбой репортится владеющей M-задаче, не патчится здесь.

### Deliverables (один commit)
1. **`tools/SimCoach.GroundTruthDump`** — console-`Exe` (зеркаля `tools/SimCoach.Bake/SimCoach.Bake.csproj`) с `ProjectReference` на `SimCoach.Storage` + `SimCoach.Contracts`. Итерирует `McapSegmentEnumerator.Read(sessionDir)` (`src/SimCoach.Storage/Mcap/McapSegmentEnumerator.cs:56`) и пишет per-frame CSV.
2. **`scripts/groundtruth_oracle.py`** — pandas truth-oracle над CSV, независимый от pipeline-кода. Per-corner (min speed, brake-onset position, time-at-position по `[Start,End]`) и per-sector truth. Emit `truth.json`.
3. **`tests/SimCoach.Reference.Tests/GroundTruthRevalidationTests.cs`** — env-gated xUnit: декодирует fixture через `McapSegmentEnumerator.Read`, гонит реальный `ComputeSession` через `ComputeTestHarness.RunAsync`, ассертит proto-поля против `truth.json` + render-path smoke. Skip, если `SIMCOACH_GROUNDTRUTH_FIXTURE` unset (Q-EG1).
4. **`docs/05-implementation/ground-truth-revalidation.md`** — run-book (env, dumper, oracle, test) и pass/fail таблица.

### Sub-deliverable 0 — Monza track-model в `ComputeTestHarness` (Judge revision — блокирующее)
Verified: `ComputeTestHarness` конструктор жёстко ставит `BakedGeometryFixture.Spa()` и `FakeTrackLengths.Spa()`; `ComputeSession` строит трекеры из `_trackModel.Corners` (`:416-419`). Monza-кадры → ноль `monza_t01..t11` окон → per-corner/Parabolica ассерты стреляли бы по пустоте (vacuous pass).
- Сделать `ComputeTestHarness` **track-model-configurable**: инжектировать `TrackModelStore`/geometry dataset вместе с `ITrackLengthProvider`.
- Wire-ить gate с **реальной vendored Monza-геометрией** (`CornerGeometryDataset.Load`) + Monza track length.
- Добавить **positive guard**: ассертить, что эмитирован **непустой** набор `monza_t*` `CornerEvent` — mis-wired empty-corner harness падает громко, не проходит vacuously.

### Файлы (file:line)
- Reuse decode: `McapSegmentEnumerator.cs:56` (`Read`), `:24` (`ResolveSegmentPaths`).
- Reuse compute-driver: `ComputeTestHarness.cs:80` (`RunAsync`); **править конструктор `:34` для track-model injection**.
- Модель console-tool: `tools/SimCoach.Bake/SimCoach.Bake.csproj`, `Program.cs`.
- Emit-path under test: `ComputeSession.cs:96-115, :195-205, :207-235 (:220-221), :348-354, :411-414`.
- Number-origin: `CornerEventBuilder.cs:79-81` (3929-баг), `:83-85` (M16 zero-collapse).
- Proto-поля gate читает: `telemetry.proto` — `CornerEvent`/`delta_ms`, `SectorEvent`/`delta_ms`, `aggregated_losses`, `sector_avg_delta_ms`.
- Render-path (Judge revision, для string-ассертов): `DebriefTemplate.BuildJson`, corner-cadence `PhraseRenderer`.
- Fixture (НЕ коммитится — privacy): `/mnt/c/Users/koba9/AppData/Local/SimCoach/recordings/20260701-171602-738/segment-000[0-4].mcap` (5 сегментов) + `laps.parquet`. Target **105201 кадров**.

### Подход
**Step 1 — Dumper (`Program.cs`).** Arg1 = session-dir, arg2 = output CSV. `McapSegmentEnumerator.Read`; для каждого `TelemetryFrame` писать: `t_ms` (из `T.ToDateTimeOffset()` epoch-ms — единственные надёжные часы; **не** `lap_number`), `normalized_car_position`, `speed_kmh`, `brake`, `throttle`, `gear`, `steer_angle`, `is_in_pit_lane`, `is_valid_lap`, `tyres_out`, `current_sector_index`, `lap_number` (только debug). `System.Text.Json`/ручной CSV. Sanity-print: frame-count (105201), `is_in_pit_lane==1` (~11806).

**Step 2 — Truth oracle (`groundtruth_oracle.py`).** Load CSV. **Dedup по `t_ms`** (~50% SHM over-poll дубликатов; keep first per distinct `t_ms`). Сегментировать 3 прохода по wrap `normalized_car_position` (0.98→0.02), НЕ по `lap_number`. Идентифицировать flying/PB круг (средний bounded-проход). Per corner (`t01,t02,t03,t06,t09,t11`, Parabolica): `min_speed_kmh`, `brake_onset_position` (первый `brake >= 0.15` в widened upstream-окне), `time_at_position(Start→End)` линейной интерполяцией. Per sector: `sector_time_ms` для out-lap И flying (воспроизвести poisoning, подтвердить уход). Corner `[Start,End]`: **inline vendored Monza landmark-константы** — oracle НЕ импортирует pipeline-geometry (Q-EG3). Emit `truth.json`.

**Step 3 — Revalidation test.** `[Fact]` guarded: `SIMCOACH_GROUNDTRUTH_FIXTURE`; unset → `return` (skip). Декодировать через `McapSegmentEnumerator.Read`, гнать через `ComputeTestHarness.RunAsync` c **Monza track-model** (sub-deliverable 0). **Duplicate-frame handling:** гнать raw-поток (с дубликатами) через `ComputeSession`, т.к. production тоже их видит; oracle dedup-ит только для своей математики.

**КЛЮЧЕВОЙ caveat реальности (Judge revision, §4): fixture-reference был перезаписан in-place этим же PB-кругом**, поэтому harness seed-ит self==ref и каждый ref-relative DIFF схлопывается к ~0. Формулировки ассертов приведены в соответствие:

Ассерты (каждый mapped к M-item):
- **M2/M24 (t03) — НЕ vacuous:** span-mismatch баг даёт ~−3928 **даже self-vs-self**, поэтому per-corner `delta_ms`-ассерты дискриминируют fixed от broken. `CornerEvent` `monza_t03`: `delta_ms` в tolerance от oracle self-time-at-position по `[Start,End]`, `abs(delta_ms) != ~3929`. Повторить t01/t02/t06/t09/t11.
- **M1/M25 (S1):** `SessionEvent.sector_avg_delta_ms[0]` — gain (negative) или absent, НЕ ~+14799; out/in-lap внесли **ноль** пересечений.
- **M27/M1:** out/in-lap не появляются как clean/coachable.
- **M24 (Parabolica) — ABSOLUTE, не diff:** ассертить self **абсолютное** min-speed **VALUE** ~=127.3 km/h (oracle-grounded) **И** что `min_speed_diff_kmh` **схлопывается к ~0** (баг давал +15.1). НЕ формулировать «`min_speed_diff ~= 127.3`».
- **M16 (Parabolica) — ONSET-position, не diff:** ассертить, что brake **onset position** найдена (non-null, нет StartPosition-fallback-collapse) на oracle-true onset. НЕ ассертить non-zero self-ref diff (self==ref → diff ~0).
- **Sigma-of-corner-deltas:** ~0 self-vs-self, **НЕ** −1381. Валидировать саму `−1381ms` lap-delta и `refS1 ≈ 36467ms` против **DB-recorded runtime values + back-calc**, НЕ против fresh self-referenced harness-run.
- **M3 (defence-in-depth):** второй `[Fact]` гоняет guard изолированно на синтетической sign-inverted/oversized corner- И sector-потере, ассертит drop.
- **Render-path smoke (Judge revision):** прогнать `DebriefTemplate.BuildJson` + corner-cadence `PhraseRenderer` над эмитированными артефактами; ассертить: строка `«3929»` **никогда** не появляется для Curva Grande, ни одна S1-loss строка не рендерится. (Две лжи — *строки*; proto-field ассертов недостаточно.)

**Per-fix unit-test чеклист (каждый в своей M-задаче; gate ассертит агрегат):** M27 `CleanLapPredicateTests` · M1 `ComputeSessionTests`+`SessionLossAccumulatorTests` · M2 `CornerEventBuilderTests` · M24 `ThrottleSpeedKernels`/`CornerTrackerTests` · M25 `ComputeSessionTests` · M16 `BrakeKernels`/`CornerEventBuilderTests` · M3 `LossPlausibilityTests` (явно ловушка `14799 < 35994`).

### Риски
- **Fixture нельзя коммитить** (`.mcap` gitignored; privacy). Gate env-gated / dev-machine-only. Mitigation: env-gated skip + committed dumper/oracle/doc; per-fix unit-тесты — CI regression net. (Q-EG1.)
- **Oracle/pipeline geometry coupling** — inline landmark-константы (Q-EG3).
- **Reference-audit caveat** — reference-parquet перезаписан in-place (`references.source_session_id = 20260701-171602-738`). Gate ассертит **relative** correctness (sign, order-of-magnitude, self-vs-self span consistency) + oracle-grounded **absolute** self-only метрики; back-calc `refS1 ≈ 36467ms` через DB-recorded runtime values.
- **Tolerance bands — coaching-material** (Q-EG2).
- **Duplicate-frame handling** — прогон raw-потока (не deduped CSV) через `ComputeSession` зеркалит production.
- **Empty-corner vacuous pass** — mitigated positive guard (sub-deliverable 0).

### Тесты / валидация
- Build: `dotnet build SimCoach.sln` (WSL: `"/mnt/c/Program Files/dotnet/dotnet.exe" build SimCoach.sln`). Новый tool + test под `TreatWarningsAsErrors`, `var` IDE0007/0008, `_camelCase`.
- Прогон gate: `SIMCOACH_GROUNDTRUTH_FIXTURE=/mnt/c/Users/koba9/AppData/Local/SimCoach/recordings/20260701-171602-738`, `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SimCoach.Reference.Tests --filter GroundTruthRevalidation`.
- Dumper smoke: 105201 кадров, ~11806 пит-кадров.
- `dotnet format SimCoach.sln --verify-no-changes` чисто.

### Acceptance (декларирует NO-GO lifted)
1. Gate проходит с fixture: **нет** `abs(delta_ms) ≈ 3929` для `monza_t03`; шесть phantom-gain поворотов `delta_ms` в tolerance от oracle self-time-at-position по `[Start,End]`.
2. `sector_avg_delta_ms[0]` (S1) — gain (negative) или опущен, никогда ~+14799; out/in/pit вносят ноль.
3. Parabolica: self абсолютное min-speed value ≈ 127.3 km/h И `min_speed_diff_kmh` схлопнут к ~0; brake **onset position** найдена на true onset (нет StartPosition-fallback).
4. Render-path smoke: строка `«3929»` не рендерится для Curva Grande; ни одной S1-loss строки.
5. M3-guard тест проходит изолированно (defence-in-depth), для corner И sector, через lap-deficit-сравнение.
6. Positive guard: непустой набор `monza_t*` `CornerEvent` эмитирован (harness track-model wired корректно).
7. `−1381ms` lap-delta и `refS1 ≈ 36467ms` валидированы против DB-recorded runtime + back-calc, не self-referenced run.
8. Dumper, oracle, doc закоммичены и воспроизводимы; env-gated тест skip-ает чисто (green) без fixture.
9. Все per-fix committed unit-тесты (M1/M2/M3/M16/M24/M25/M27) проходят в CI.

**Depends:** ВСЕ из M27/M1/M2/M24/M25/M16/M3. Scaffold (dumper + oracle + skipping-shell + harness track-model injection) можно авторить параллельно раньше; passing-ассерты требуют всех фиксов. **Blocked-pending:** Q-EG1/Q-EG2/Q-EG3.

---

## Граф зависимостей и sequencing

```
M27 ─┐ (sharpens clean predicate; no deps)
     ├─► M1 (coachable gate) ──┬─► M25 (aggregation; needs M1's list)
     │   [Q1, Q5]              │
     │                         ├─► M3 (plausibility guard) ──┐
M2+M24 (span+kinematics) ══════╪══► M16 (brake window) ══════�┤
   [Q2]                        │   [Q4]                       │
   ▲                           │                              │
   ╚══ HARD-COUPLED: M16 arms   the shared CornerTracker       │
       buffer; M2 slices [Start,End] from it. NOT             │
       independently revertible (see rollback).               ▼
                                       EXIT GATE (ground-truth revalidation) — LAST
                                       [Q-EG1/2/3]
```

**Рекомендованный порядок коммитов:** `M27 → M1 → M2+M24 → M25 → M16 → M3 → Exit gate`.
- **M16 идёт строго ПОСЛЕ M2/M24** (не параллельно) — они делят один `CornerTracker`-буфер: M16 владеет upstream-armingом, M2 владеет `[Start,End]`-slice-инвариантом. Порядок гарантирует, что M2's контракт под-окна установлен до того, как M16 сдвинет arming вверх.
- Exit-gate scaffold (dumper/oracle/skipping-test/harness-track-model) можно авторить рано параллельно; passing-ассерты требуют всех фиксов.

**Явные doc-зависимости:** «M24, M25 — следствия/добивка M2, M1»; M3 — последняя линия, нужен корректный lap-delta от M1+M2; M27 feed-ит M1-предикат; M16 нужен upstream-arming общего буфера, `[Start,End]`-инвариант M2.

---

## Стратегия rollback / verification

- **Standalone reverts:** M27 revert только релаксирует clean-предикат; M1 revert снимает gate; M25 revert восстанавливает mean; M3 revert снимает guard. **Ни одна не трогает proto → contract-rollback никогда не нужен.**
- **ИСКЛЮЧЕНИЕ — M2 и M16 НЕ независимо-revertible (Judge revision).** M16 мутирует ровно тот `CornerTracker`-буфер, из которого M2's delta-математика читает. Реверт любого из двух **требует** ре-проверки инварианта: M2's delta/min-speed/kinematics берут строго под-окно `[Start,End]`, brake-scan берёт pre-roll. Реверт M16 без ре-проверки может оставить M2 читающим расширенный буфер → паразитная дельта в каждом повороте.
- **Per-commit verification:** `dotnet build SimCoach.sln` (`TreatWarningsAsErrors`) + `dotnet format --verify-no-changes` + unit-тесты задачи зелёные. Golden-fixtures (understeer_trend, sector_avg_delta, corner delta_ms) re-based с объяснением сдвига в теле коммита.
- **CI regression net:** committed hermetic per-fix unit-тесты — постоянная защита; env-gated ground-truth gate — dev-machine ритуал one-command.
- **Exit-gate как финальная проверка:** прогон против `20260701-171602-738`; при провале — сбой репортится владеющей M-задаче, не патчится в gate.
- **Caveats честить всегда:** никогда не key-ить на ACC `lap_number` (мусор, 104403/105201 = «1»); dedup на `t_ms` (~50% SHM-дубликатов); `is_in_pit_lane=1` = out-lap **+** in-lap (11806 кадров); reference-parquet перезаписан in-place → self==ref, assert **relative** + oracle-grounded absolute self-only, back-calc `refS1 ≈ 36467ms` из DB-recorded runtime.

---

## Consolidated Open Questions (все MAJOR — эскалировать, НЕ решать автономно)

### Q1 — Архитектура emit-пути (блокирует TASK 2 / M1)
Как coachable-lap gate обрабатывает mid-lap emission — corner/sector эмитится потоково, до того как известна цельно-круговая чистота?
- **(a) Frame-level latch:** `_lapPoisoned` set на первом pit/invalid/unbounded кадре, подавить все дальнейшие emit круга. Ловит все три измеренных контаминанта, простейший, сохраняет real-time cadence; не un-emit-ит события, отправленные до late-dirtying круга.
- **(b) Buffer-and-flush в HandleLap:** держать события круга, публиковать только при чистом закрытии. Полностью корректно для late-dirty, но откладывает coaching до lap-cadence и реструктурирует emit-путь.
- **(c) Pure per-frame gate (без latch):** подавлять только пока текущий кадр pit/invalid; поворот, straddle-ящий pit-entry, эмитит чистую часть.

**Рекомендация:** (a) frame-level latch + boundedness (`HasStartedLap`). Чинит 100% измеренных сбоев, без magic numbers, сохраняет real-time; структурировать код так, чтобы поздний своп на (b) локализовался в `ComputeSession`.

### Q5 — Один shared `IsCoachableLap` предикат vs три distinct (блокирует TASK 2 / M1; координируется с TASK 1 / M27)
Verified три расходящихся трактовки: `CleanLapPredicate.cs:29` опускает pit, fuel-gate `ComputeSession.cs:243` использует `!IsInPitLane`, M1 добавляет frame-level. Консолидация молча переопределяет `LapEvent.is_clean` (proto field 6) semantics и reference-seeding/fuel-averaging — contract-semantics решение.
- **(a)** Один shared `IsCoachableLap` для M1 + M27-clean + fuel — единое определение, но folds pit в `is_clean` app-wide.
- **(b)** Три distinct — нет cross-contract redefinition, но «racing lap» в 2-3 местах.
- **(c)** Два: shared frame-level `IsCoachableFrame` (M1 + fuel) + whole-lap `CleanLapPredicate` (reference seeding).

**Рекомендация:** (c) — делить frame-level coachable между M1 и fuel-gate, держать whole-lap clean отдельно для reference-seeding. Держать M27 минимальным (просто `IsInPitLane` в `CleanLapPredicate`); M1 владеет решением. Не решать без sign-off.

### Q2 — Rewrite CornerTracker-окна + aggregation-policy (блокирует TASK 3 / M2+M24 и TASK 4 / M25)
**Q2a (M2/M24):** Переписка окна с throttle-resume на full-`[Start,End]` меняет *когда* эмитит каждый поворот и КАЖДОЕ self-derived поле (min_speed, trail_brake, balance, wheelspin, brake_overlap, steering_jitter). Coaching-materiality и emission-latency.
- Full-span + two-window split (рек.): fire на `EndPosition`; кернелы анализируют `[Start,End]` под-окно; throttle-resume-точка внутри кернела только для `throttle_resume_diff_m`.
- Full-span только для `delta_ms`, старое окно прочим кернелам (defers M24) — оставляет min_speed_diff неверным.
- Buffer-and-replay: сохраняет старую latency, больше plumbing.
- Defer до recorded before/after per-corner diff.

**Рекомендация:** full-span + two-window split — минимальный корректный change, чинит M2+M24 в одном коммите, без proto-change; gate-ить merge на review exit-gate before/after per-corner diff.

**Q2b (M25):** Статистика агрегации sector-delta: median vs best (min-|delta|), и допустимо ли молча менять смысл `sector_avg_delta_ms` (field 14) с mean на median/best.
- Median (рек., robust, без константы).
- Best (min-abs, «theoretical best»).
- Keep-mean-rely-on-M1.
- Median/best + rename field 14 (MAJOR proto-change — отдельный sign-off).

**Рекомендация:** median, field 14 без rename, семантический change документирован; аккумулятор как per-sector список + один helper (median↔best своп в одну строку).

### Q3 — Plausibility-guard: сайт, пороги, нейтрализация (блокирует TASK 6 / M3)
**Q3a (сайт):** compute (рек.) / Gold / TipValidator-only (отвергнуто — пропускает template-fallback `CoachService.cs:270,383`) / hybrid. **Рекомендация:** compute (`SimCoach.Reference`) в `EmitCorner`/`EmitSector`/`HandleLap`/`Complete` — единственный сайт с best-lap-дефицит comparand без proto-change, защищает LLM и template phrasing (включая realtime per-sector cadence).

**Q3b (пороги + policy):** `MaxPlausibleCornerLossMs` (Tier A), `LapDeficitLossRatio` + `LapDeficitFloorMs` (Tier B); drop-silently vs down-rank.
- Drop silently — Tier-A ceiling ~2000ms/corner, Tier-B ratio ~1.0–1.2× |lap deficit| + floor ~300ms.
- Down-rank — ниже over-suppression risk, выше риск озвучить ложь.
- Оба: hard drop ceiling + soft down-rank band.
- Parameter-free ratio 1.0 + только Tier-A ceiling knob.

**Рекомендация:** drop silently (пары с «silent fallback» минимум-бара), пороги как `IOptions`-backed placeholder-дефолты `// TODO(Q3b)`; user подтверждает финал. Drop над down-rank, т.к. down-ranked-ложь может быть озвучена при пустом subset.

**Q3c (нейтрализация + scope):** Как нейтрализовать implausible `CornerEvent`/`SectorEvent`, чтобы `corner_catch_all`/`sector_catch_all` (`actionRegistry.json:245,309`) не рендерили `abs(delta_ms)`; и «silent fallback» — в scope этого pack или defer в M21/P2?
- Zero/neutralise reference-relative loss → corner/sector молчит (рек., минимум-бар silent-fallback через M3, без registry-edit).
- Retune catch_all на `gt` (= M21, вне scope).
- Compute-internal «suppressed» флаг для Gold.
- Do-nothing (rely M2, defer в M21).

**Рекомендация:** zero/neutralise в compute (option A), покрывая EmitCorner И realtime EmitSector. Минимум-бар silent-fallback без touch registry, proto без изменений. Подтвердить scope vs defer (пересекается с Q5). Нейтрализация-форма для sector идентична corner.

### Q4 — Upstream brake-window distance (блокирует TASK 5 / M16)
Какая метрическая дистанция вверх от corner-start? Измеренные onset span 41–290m.
- ~200m fixed config-default `ComputeOptions.BrakeWindowUpstreamM`.
- ~300m (покрывает глубочайший Parabolica ~290m + margin).
- Per-corner value bound к baked corner-geometry.

**Рекомендация:** config-driven `BrakeWindowUpstreamM` с 200m placeholder-дефолтом, финал user-owned; если truth-oracle покажет что 200m миссит Parabolica onset — поднять до ~300m. Per-corner geometry-binding — defer.

### Q-EG1 — Размещение/gating ground-truth gate (блокирует TASK 7)
Fixture (105201 raw-кадров) нельзя коммитить (`.gitignore *.mcap`; privacy).
- Env-gated xUnit в `SimCoach.Reference.Tests` (skip без `SIMCOACH_GROUNDTRUTH_FIXTURE`) + committed dumper/oracle/doc.
- Pure out-of-repo ритуал (no xUnit).
- Commit tiny redacted synthetic fixture.
- **Both env-gated test + tool/oracle/doc.**

**Рекомендация:** option 4 (env-gated test + committed dumper/oracle/doc). CI green + privacy intact, gate one-command локально; per-fix hermetic unit-тесты — regression net.

### Q-EG2 — Tolerance bands (блокирует TASK 7 ассерты)
С учётом self==ref реальности (reference перезаписан in-place):
- Tight: ±100ms/corner, ±2km/h, ±15m brake, Σ ±20%.
- Moderate: ±150ms/corner, ±3km/h, ±25m brake, Σ ±30%.
- Loose: ±250ms/corner, ±5km/h, ±40m brake, Σ ±50%.
- Sign + order-of-magnitude only для всего derived-от-overwritten-reference.

**Рекомендация:** moderate absolute bands для oracle-grounded self-only метрик (per-corner self time-at-position, абсолютное min-speed value) + sign/order-of-magnitude для всего derived-от-overwritten-reference; reject при неверном знаке независимо от величины.

### Q-EG3 — Независимость геометрии oracle (блокирует TASK 7 oracle)
- Inline Monza landmark Start/End константы в Python-oracle (fully independent, рек.).
- Oracle читает ту же baked-geometry (coupled, не ловит geometry-errors).
- Hybrid (inline + doc-note о pipeline-geometry для видимости drift).

**Рекомендация:** option 1 (inline independent constants), опционально с doc-note (option 3). Независимость — причина существования oracle.

---

## Non-blocking caveats (документировать, НЕ тянуть в этот pack)
- Residual `corner_catch_all` `abs_round0` sign-drop (`ParamTransform.cs`) — plausible small gain всё ещё рендерится как loss после M3-ceiling пропускает малую величину; знаково осознанно deferred в **M21/P2**. Не тянуть registry-fix в этот pack.
- Reference-versioning (M37), полный `corner_catch_all` rework (M21), RU-eval gate (M18) — вне scope; M18 — опциональный параллельный трек.