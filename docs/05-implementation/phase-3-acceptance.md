# SimCoach — Приёмочный отчёт по Фазе 3

*Дата: 2026-07-01 · Ветка `feat/phase-3-pr8` · Авторитетный прогон: сессия `20260701-171602-738` (live: gemini-2.5-flash-lite corner + claude-sonnet-4.6 debrief)*

> **Ревью Фазы 3 — сводный план:** [`phase-3-master-backlog.md`](phase-3-master-backlog.md) — единый приоритизированный бэклог (M1…M43) со сквозной трассируемостью на все три документа ниже.
>
> **Триптих ревью Фазы 3:**
> 1. Этот документ — дефекты продукта/UX/полноты (по логам, БД, коду).
> 2. [`phase-3-acceptance-addendum.md`](phase-3-acceptance-addendum.md) — системная валидация детекции против ground-truth (декодировано 105201 кадр из MCAP авторитетной сессии; измерено «эмитировано vs реальность»). Содержит **вердикт готовности к Фазе 4 (NO-GO)** и уточняет корневые причины находок #A/#B ниже реальными числами.
> 3. [`phase-3-llm-strengthening.md`](phase-3-llm-strengthening.md) — усиление LLM-слоя: промпты/few-shot, output-схема/abstain, роутинг и конфигурация моделей (с ценами), повышение роли LLM до bounded-аналитика, обогащение Gold-контекста.

---

## 1. Резюме / вердикт приёмки

**Вердикт: Фаза 3 условно принимается на уровне ИНЖЕНЕРНОГО СПАЙНА, но НЕ принимается на уровне КАЧЕСТВА КОУЧИНГА.**

Технический каркас Фазы 3 построен добросовестно и подключён в живой хост: все work-items D0–D9 реализованы и работают на реальной телеметрии, стоп-порядок соблюдён, миграции 002–004 на месте, все 6 пунктов `ValidateOnStart` присутствуют, LLM-seam с circuit breaker / cost meter / fake-провайдером функционирует. Заявление PR-H «every item lands clean, no half-wired features» на уровне контрактов в основном правдиво. Продукт реально проходит цикл «телеметрия → domain-event → Gold → действие → LLM → SQLite» вживую.

Однако **приёмка Фазы 3 «clean» — оптимистична**, и вот пять главных выводов:

1. **Единственный «настоящий» debrief авторитетного прогона фактически неверен.** На круге, который был личным рекордом (−1.4 с к эталону, лучший S1 дня), debrief сообщает о «потере 14.8 с в секторе 1». Это прямой подрыв доверия — коуч противоречит очевидным для пилота данным. Корень — session-агрегаты копят out/in-круги и незачётные сегменты (находки #1/#21/#30).

2. **Сырые вычислительные артефакты утекают в озвучку дословно.** Curva Grande стабильно получает «отклонение около 3929мс» во ВСЕХ пяти прогонах (даже на PB-круге) — это баг сравнения разных участков трассы в compute, а не реальная потеря (находка #29). Плюс «тормози позже на 77 метров», «34 секунды в секторе». Ни один слой не проверяет правдоподобие величин.

3. **Все жалобы владельца подтверждены в коде и данных** — и это не случайные сбои, а системные пробелы дизайна: итальянские имена поворотов + разнобой транслитерации (#2), инфомусор/многословность против TTS (#6), нерелевантные советы на out-круге (#31), контекстно-неверная «шикана: не тормози» (#5/#12), отсутствие совета при широкой траектории (#4/#15).

4. **Обещанные планом гейты качества НЕ построены.** RU-eval-гейт (m5) и real-HTTP schema-acceptance фикстура отсутствуют (#23/#24). Именно из-за них жалобы №3/№4/№6 не имеют регрессионного барьера, а debrief HTTP 400 просочился в прод. Учитывая, что ВСЕ претензии владельца — про качество RU-подсказок, отсутствие RU-eval — самый существенный пробел полноты.

5. **Рынок уже решил ключевые проблемы SimCoach иначе.** Лидеры (trophi.ai, Coach Dave Delta, Full Grip Vision, Track Titan) единодушны: номера поворотов вместо имён, жёсткая приоритизация «одна вещь за раз, остальное подождёт», подача на СЛЕДУЮЩЕМ круге (а не в повороте), фазовая декомпозиция Braking/Entry/Apex/Exit, и cadence-governor с cooldown. SimCoach отстаёт именно по подаче/каденции — при том, что RU-голос остаётся реальным незанятым дифференциатором.

**Итоговая оценка качества коучинга для реального пользователя: низкая — на грани «нельзя показывать пилоту как есть».** Спайн готов; слой, который пилот реально слышит, выдаёт неверные, неясные и нерелевантные подсказки достаточно часто, чтобы разрушить доверие с первых сессий.

---

## 2. Полнота относительно плана Фазы 3

### Сделано по-настоящему (D0–D9)
Все девять work-items реализованы и подключены в живой хост (`CoachComposition.cs`, `TelemetryComposition.cs:54-63`):
- **D0** kernels + `SessionLossAccumulator` + strategy-plumb; **D1** ActionRegistry (25 действий, catch-all на каждую cadence, RU-шаблоны); **D2** corner-name map + PromptBuilder; **D3** GoldArtifactBuilder per cadence + privacy-serializer; **D4** output-schema с enum=subset; **D5** LlmRouter → CircuitBreaker → CostMeter → provider (+4 family-транслятора); **D6** CostMeter → `llm_usage` (миграция 002); **D7** CircuitBreaker + RuleEngine quiet-zones; **D8** CoachService + tip-sink; **D9** wiring + persistence + settings + миграции 003/004.

### Легитимно отложено (записано в `mvp-deferrals.md` — честно)
- **FR-014** provisional best-of-session reference → Phase 6.
- **FR-035** recent-contact quiet-zone структурно есть, но инертна (`Contact` всегда `false`, `LiveCoachAmbientState.cs:108`) — нет канала в `TelemetryFrame`. Задокументировано.
- **FR-060** tyre-degradation на ACC → honest-zero → Phase 6.
- **FR-037/FR-072** UI-баннер circuit breaker + cost-панель → P5/P6/P7.
- Built-but-not-wired (по плану «UI-контракты designed in P3, not built»): `IReferenceQueryRepository`, `ISessionHistoryRepository`, `IRateCardQuery`-потребитель, write-side `ISettingsStore`, зарезервированные столбцы миграции 004, `ILlmClient.StreamAsync`. Это НЕ скрытый долг, но должно называться явно.

### Молча пропущено (план обещал — в коде нет)
| Пробел | Доказательство | Серьёзность |
|---|---|---|
| **RU-eval gate (m5)** — реальный per-release гейт (LLM-судья + рубрика + фикстуры no-PB/debrief + числовой порог) | `phase-3-detailed-plan.md:1108-1132`; grep tests/docs — пусто | **высокая** (#23) |
| **Per-family schema-acceptance real-HTTP фикстура** (pre-pin gate) | план D5 ~503-505/713-714; есть только unit `SchemaTranslatorTests.cs` | средняя (#24) |
| **Месячный бюджет = 0** вместо плановых $5.00 → FR-072 alert мёртв из коробки | `appsettings.json:52` vs план 1145/1171 | низкая (#25) |
| **DeepSeek «registered but gated off»** — фактически отсутствует полностью | grep `deepseek` по src/ пуст | низкая (#27) |
| **Doc-drift**: FR-061 всё ещё `deepseek-chat-v3.2` вместо Sonnet 4.6 | `functional-requirements.md:81` | низкая (#28) |

### Честна ли формулировка «closes Phase 3 clean»?
**Частично.** Спайн — да, добросовестно. Но формулировка умалчивает о трёх вещах: (1) два «мягких» гейта качества не построены (и именно они ловили бы жалобы владельца); (2) один config-дефолт тихо расходится с планом (месячный бюджет); (3) контрактный FR-документ рассинхронизирован с кодом. Дефолтный опыт из коробки — офлайн fake-провайдер («Тормози позже.»), а условие плана для включения Live (прохождение RU-eval + schema-acceptance) физически невыполнимо, т.к. гейтов нет. Флип в Live делался вручную в обход отсутствующих гейтов (что технически возможно — `Llm:Live` это settings-флаг, не code-gate).

---

## 3. Дефекты и проблемы

Отсортировано по серьёзности внутри категорий. Серьёзность откалибрована с учётом того, что в P3 вывод — только console/log + SQLite (TTS ещё нет), поэтому часть «ударов по TTS» пока латентна и реализуется в Phase 4.

### 3.1. Качество данных (data-quality) — корневой блок, питает большинство жалоб

| # | Дефект | Серьёзность | Доказательство | Куда смотреть |
|---|---|---|---|---|
| A | **Corner delta_ms сравнивает разные участки трассы** → Curva Grande ≈3929мс во всех 5 прогонах даже на PB-круге (физически невозможно) | **высокая** | `coach_tips`: monza_t03 `rendered_param='3929мс'` ×5 сессий; в 171602 круг = PB −1381мс | `CornerEventBuilder.cs:79-92`, `CornerTracker.cs:29-71`, `cornerGeometry.monza.json` (span monza_t03=238м) |
| | *Измерено (аддендум §2.2):* корень — коллапс **SELF-окна** (триггер возврата газа схлопывает окно до 2 кадров на полногазовом повороте), а не ошибка ref-члена. `3929мс` = время прохождения ЭТАЛОНОМ Curva Grande, поданное как «потеря». Гейт out/in-lap это НЕ чинит — нужно выравнивание self и ref на одном `[Start,End]`. | | | |
| B | **Debrief авторитетного прогона фактически неверен**: 14799мс «потеря S1» на PB-круге с лучшим S1 дня | **высокая** | 171602 `top_losses_json` vs `laps`: s1=35994, is_pb=1, delta −1381 | `ComputeSession.cs:195-235` (EmitCorner/EmitSector без гейта валидности круга), `:421-430` (ResetForNextLap не чистит session-аккумуляторы), `GoldArtifactBuilder.cs:87-88/125-133` |
| C | **Realtime типы фаерятся на out/in-кругах** → «34 секунды в секторе», «77 метров» при 0 завершённых кругов | **высокая** | сессии 162041/165856 `lap_count=0`, но sector_summary_loss `param='33958мс'`, brake_later `'77м'` | `ComputeSession.cs:207-218`, `CornerEventBuilder.cs:83-85` (brake fallback), `RuleEngine.cs` (нет гейта на out-lap) |
| D | **Нет проверки правдоподобия величин** перед выдачей в озвучку/LLM/debrief — любой апстрим-артефакт выходит verbatim | средняя | 3929мс/77м/33958мс/14799 — все дошли до текста | `PhraseRenderer.cs:52-63`, `TipValidator.cs:15-67` и `:70-133` (нет magnitude-clamp) |
| E | **Clean-lap предикат считает pit/out-круги «чистыми»** → засорение вторичных session-агрегатов (average, consistency) | средняя | 151452 lap4: s1=63254мс, is_clean=1; clean_lap_count=2 | `CleanLapPredicate.cs:29` (нет `IsInPitLane`, хотя fuel-путь его использует, `ComputeSession.cs:243`) |

**Общий корень A–D:** session/corner-аккумуляторы (в `SimCoach.Reference`) не исключают out/in-круги и незачётные сегменты, а corner-delta меряет self и ref на РАЗНЫХ окнах. **Shallow-fix:** (1) гейтить `EmitCorner`/`EmitSector` и session-аккумуляторы по зачётным (bounded, non-pit, clean) кругам; (2) мерить corner self и ref на ОДНОМ span; (3) добавить sanity-clamp/подавление типа при явно артефактных величинах; (4) добавить `IsInPitLane` в clean-предикат. E затрагивает только вторичные метрики (PB/reference защищён), поэтому средняя.

### 3.2. Дизайн коучинга (coaching-design)

| # | Дефект | Серьёзность | Доказательство | Куда смотреть |
|---|---|---|---|---|
| F | **`straighter_braking` без фазового контекста** — «Не тормози, выпрямляй руль» в шикане (жалоба №6) | **высокая** | `actionRegistry.json:52-66`, clause `brake_overlap_steer_pct>0.3`; live 5×, «Не тормози в Variante del Rettifilo (1)» (165856) | `actionRegistry.json:52-66`, `BrakeOverlapSteerKernels.cs`, `CornerPhaseResolver.cs`, сегментация в `SimCoach.Reference` |
| G | **`corner_catch_all` — недискриминирующий escape-hatch, ставший самым частым советом** (8 Llm+1 Tmpl), зачитывает сырое число вместо совета (жалоба №7) | **высокая** | `actionRegistry.json:222-236`; частота №1 в `coach_tips` | таксономия `actionRegistry.json`, `ClauseEvaluator.cs` (почему специфичные clause часто молчат) |
| H | **Пробел таксономии: нет reference-free действия «широкая траектория/мимо апекса»** (жалоба №5) | средняя | `tighten_apex` (`:162-176`) двойне-загейчено: `requires_reference:true` + `racing_line_deviation_m>0.5` + `min_speed_diff_kmh<0` | `actionRegistry.json` (`tighten_apex`/`wider_entry`), `racing_line_deviation_m` в `SimCoach.Reference`, `CornerGoldView.cs:29` |
| I | **Нет фильтра релевантности / LLM не может «промолчать»** — салинс решают только пороги + 4s cooldown | средняя | `OutputSchema.RealTime` требует action_id∈subset; нет abstain-пути | `OutputSchema.cs`, `CoachService.ProcessRealtimeAsync`, `RuleEngine.cs`, few-shot negatives |
| J | **Нет межкругового арбитража / памяти** — дословный повтор совета на соседних кругах; разные советы на один поворот | низкая | 171602: «В Лесмо 1…» дважды; monza_t03 catch_all + tighten_apex | `CoachService.cs:200-235`, `RuleEngine.cs:87` (cooldown только по каденсу) |

> **Уточнение по H (жалоба №5):** «полная тишина» — не общий случай. При наличии эталона `corner_catch_all` (`delta_ms>150`) обычно всё же выдаёт расплывчатый тип. Настоящее молчание — когда (а) эталона нет вовсе, либо (б) широкая линия стоила <150мс (в т.ч. «шире, но не медленнее», где `min_speed_diff>=0` глушит `tighten_apex`). Это product-gap, частично маскируемый vague-катч-олом.

> **Уточнение по F:** в БД `straighter_braking` 10× = 5 fake-provider строк (сессия 154931) + 5 live. Корень — фазово-слепой скалярный overlap-check с плоским порогом 0.3: одновременное торможение-с-рулением НОРМАЛЬНО в шикане/trail-brake, а действие всё равно советует «не тормози».

### 3.3. Технические баги (technical-bug)

| # | Дефект | Серьёзность | Доказательство | Куда смотреть |
|---|---|---|---|---|
| K | **Corner no-retry: провал пост-валидации при HTTP 200 → сразу template** (нет второй попытки) | **высокая** | `CoachService.cs:244` `allowRetry = cadence != Corner`; 171602 corner-типы source=Template при успешных LLM | `CoachService.cs:244-262` |
| L | **Word-cap считает имя поворота как слова** → многословные итальянские имена сжигают бюджет фразы | **высокая** | `PhraseWordCount.cs:13` split всей фразы; `InCornerMaxWords=8`; «Variante della Roggia (2)» = 4 «слова» | `PhraseWordCount.cs`, `TipValidator.cs:57`, `CoachOptions.cs:15` |
| M | **Асимметрия контракта: template-fallback эмитится БЕЗ word-cap**, тогда как LLM режут на 8 словах | средняя | 171602: template «…в Curva di Lesmo 2.» = 10 слов при лимите 8 | `CoachService.cs:262` (обходит `TipValidator`) |
| N | **`lap_pb` пустой плейсхолдер: «Личный рекорд! Главная зона — .»** | средняя | 171602 `coach_tips`: lap_pb, source=Template | `actionRegistry.json:310`, `GoldArtifactBuilder.cs:117-118`, `PhraseRenderer.cs:22/42-44` |
| O | **Нет наблюдаемости accept/fallback** — причина отбраковки `TipValidator` теряется в `out _` | низкая | `CoachService.cs:404`; логи только silent/budget | `CoachService.cs:246-262`, `:391-405` |

> **Связка K+L+M — механизм 30–56% template-fallback на живой телеметрии** (session 162041: 4/9 accept; 171602: часть corner ушла в Template при HTTP 200). ВАЖНО: атрибуция «это именно word-cap» частично инференциальна — нет логирования причины отбраковки, а часть fallback = таймауты (171602 имел 2 corner/lap-таймаута). LLM часто обходит стоимость имени, транслитерируя в короткие формы (Лесмо 1, Аскари). Самый доказуемый и легко-чинимый суб-дефект — **асимметрия M** (template 9-10 слов при лимите LLM 8). **Shallow-fix:** (a) не включать имя в подсчёт или бюджетировать отдельно; (b) применять тот же word-cap к template-пути; (c) кормить LLM/шаблоны короткую RU-форму (`CornerNameShort`/`CornerNameSpokenRu` — уже вычисляются, но выбрасываются, не персистятся).

### 3.4. Продуктовый UX (product-ux)

| # | Дефект | Серьёзность | Доказательство | Куда смотреть |
|---|---|---|---|---|
| P | **Зоопарк имён: итальянские имена + разнобой транслитерации** — бесполезно для пилота (жалоба №2) | **высокая** | corner_name всегда итал.; phrase_ru: «Курва Гранде»/«Curva Grande», «Роджа 2»/«из Роджи», «Параболика»/«Parabolica»/«Параболике» | `coach.system.v1.ru.txt` (правило 2), `CoachService.cs:299-311`, `CornerNameMap.cs`, `cornerNames.json` |
| Q | **Перегруз/многословность против TTS: два императива в одной фразе** (жалоба №1) | средняя | 171602: «На Аскари добавь газ раньше, держи скорость выше», «В Параболике чуть раньше сбрось газ, больше скорости» | `RuleEngineOptions.cs` (cooldown 4s), `CoachOptions.cs` (word-cap), `coach.system.v1.ru.txt` (нет «one action per phrase») |

> **P — прямое подтверждение рынком:** ВСЕ конкуренты используют номера поворотов, никто не транслитерирует имена. phrase_ru — свободный текст LLM без constraint на написание; вычисленные spoken-формы не персистятся и в озвучку не идут. **Shallow-fix:** для corner-каденса по умолчанию НЕ называть поворот (это последний) либо давать LLM только каноничную RU spoken-форму и запрещать её менять.

> **Q — лучше рамить как product-UX, не code-bug.** Даже 8-словная фраза упаковывает два совета — значит word-cap/cooldown не единственные рычаги, нужен constraint промпта «один императив на фразу». Sub-claim про плотность каденции стоит переякорить на live-таймстемпы (текущие частично из fake-сессии, но corner-события telemetry-driven).

---

## 4. Ответ на вопрос про роль LLM (сомнение владельца №7)

**Вопрос владельца обоснован, и вот честный разбор.**

**Как устроено сейчас (по коду):** ВСЕ телеметрические решения — «что не так, какой порог» — принимаются алгоритмически в `ClauseEvaluator`/`ActionRegistry.ValidSubset` (`ActionRegistry.cs:96-104`) ДО вызова LLM. LLM получает не сырую телеметрию, а Gold-артефакт (только скалярные баллы, `GoldArtifactBuilder.cs:25-48`) + готовое меню из ≤5 предвыбранных действий, и по `OutputSchema` обязан вернуть `{action_id из subset, phrase_ru}`. Его степени свободы: (а) выбрать одно из ≤5 действий, (б) написать ≤N слов RU. Это **селектор + фразер, не аналитик.**

**Почему это по дизайну (и не «ошибка»):** архитектура намеренная и следует privacy-правилу (только Gold JSON покидает машину — сырая телеметрия никогда) + целям стоимости/детерминизма. Это ровно то, что подразумевают конкуренты: «LLM добавляет natural-language подачу, а все телеметрические решения остаются алгоритмическими» (см. PitGPT/RACEMAKE, Garage 61 Agent).

**Но у текущего баланса есть реальные издержки, которые владелец верно чувствует:**
1. **LLM структурно не может поймать контекстные ошибки** (шикана «не тормози», PB-противоречие в debrief). Промпт велит брать числа verbatim, а Gold не содержит фазы/временного ряда/траектории — даже при желании LLM нечем рассуждать (#13). Строковое поле `Reason` (`GoldCornerEvent.cs:28`) несёт минимум, но этого мало.
2. **«Творчество» LLM в свободном `phrase_ru` создаёт зоопарк имён и связки-фразы** — это издержка свободы, ортогональная тому, что он «не анализирует».
3. **Доминирование `corner_catch_all`** доказывает под-дискриминирующую таксономию: когда специфичные clause молчат, остаётся catch_all, и LLM зачитывает «около 3929мс» вместо совета.

**Где реальная ценность LLM (рекомендация):** не в парафразе одного шаблона, а в трёх ролях, которые сейчас не используются:
- **(1) Арбитраж** — из нескольких сработавших действий выбрать/слить ОДНУ самую важную вещь (прямо отвечает на жалобу №1 и рыночный принцип «one thing at a time»).
- **(2) Право промолчать** — abstain-путь, если советовать нечего/незначимо (жалобы №1/№4).
- **(3) Кросс-корнер / кросс-круг синтез и объяснение «почему»** — это то, что делает Garage 61 Agent (ask-the-data) и что trophi.ai/конкуренты называют главной слабостью, когда её нет («советует тормозить раньше, но не объясняет почему» — ровно жалоба №3).

**Стоит ли менять баланс:** да, но осторожно. Либо (A) усилить таксономию + фазовую модель, снизив долю catch_all, и оставить LLM фразером (дёшево, детерминированно); либо (B) обогатить Gold компактным фазовым срезом (фаза, тип поворота, профиль тормоз/газ/руль, дельты соседних поворотов) и дать LLM права арбитра/abstain (дороже, но отвечает на #3/#4/#6). **Это дизайн-решение владельца, не баг.** Минимум — построить RU-eval-гейт (#23), иначе любой сдвиг баланса будет «на глаз».

---

## 5. Конкурентный анализ

Рынок делится на два лагеря: **post-session аналитика** (Coach Dave Delta, Track Titan, VRS, Garage 61, iRacing/Cosworth, SRT, Second Monitor, Race Element) и **real-time голосовые коучи** (trophi.ai, Full Grip Vision, TrackPro/APEX, Crew Chief). Прямые аналоги амбиции SimCoach — **trophi.ai** и **Full Grip Vision**.

### Сравнительная таблица (по релевантным для SimCoach осям)

| Продукт | Real-time | Голос | AI/LLM | Ссылка на поворот | Каденция/фильтр | Цена/мес |
|---|---|---|---|---|---|---|
| **SimCoach (P3)** | Да (corner+debrief) | RU (Phase 4) | Action-registry + LLM | **Итал. имена (разнобой)** | **Слабая (нет priority/cooldown-governor)** | — |
| **trophi.ai** | Да (следующий круг + перед поворотом) | Да, 59 языков | Да | corner-by-corner | **explicit, impact-ranked** | $7.5–20 |
| **Full Grip Vision** | Да (<150ms, 60Hz) | Да | on-PC | «твои имена/номера» | **сильная** (priority+cooldown+driver-state) | €4–10 |
| **Coach Dave Delta** | На завершении круга (читается в гараже) | Roadmap | «AI», LLM не раскрыт | **Только номера + 4 фазы** | **Да — top 1-2 корнера** | £11.99 |
| **Track Titan** | Нет (post) | Нет | Да | не акцентирует | **«one biggest fix»** | Free/paid |
| **Crew Chief** | Да | Да (сэмплы) | Нет (rules) | номера/имена | **сильная** (per-message toggles) | Free/OSS |
| **Garage 61** | Нет (post) | Нет | Agent (ask-only) | iRacing номера | user-driven | Free/~$7 |
| **Race Element** | Только данные | Нет | Нет | n/a | n/a | **Free/OSS, 100% local** |

### Чего SimCoach не хватает (со ссылками)

- **Номера поворотов вместо имён.** Delta использует ТОЛЬКО «turn 5/8/11», никогда итальянские имена ([how-to](https://coachdaveacademy.com/documentation/how-to-use-auto-insights-ai-coaching-in-delta/)). Rally-практика: «direction + severity number» кодирует ДЕЙСТВИЕ, не географию ([slashgear](https://www.slashgear.com/1636667/what-do-numbers-rally-mean-understanding-co-driver-pace-notes/)). Прямое подтверждение жалобы №2.
- **Cadence-governor «одна вещь за раз».** trophi.ai: «ранжирует по влиянию на время круга… остальное подождёт» ([trophi.ai](https://www.trophi.ai/sim-racing-coaching)). Full Grip: «priority queue + cooldown, говорит только когда ценно, отступает по driver-state» ([fullgripmotorsport.com](https://www.fullgripmotorsport.com/about/fullgripvision)). У SimCoach нет глобального «это самая большая потеря, стоит ли говорить?» — прямой ответ на жалобу №1.
- **Подача на СЛЕДУЮЩЕМ круге, не в повороте.** Модель trophi «услышишь на следующем круге» обходит баг контекста шиканы (№6): батчинг к границе круга даёт чистый фазовый контекст.
- **Фазовая декомпозиция Braking/Entry/Apex/Exit** — стандарт индустрии ([Delta tutorial](https://coachdaveacademy.com/tutorials/ai-sim-racing-coaching-unlock-faster-lap-times-with-delta-ai-auto-insights/)). Даёт чистую дискриминирующую таксономию, снижает зависимость от catch_all (#7), закрывает mid-corner/apex-miss (#5), и избегает имён поворотов (#2/#3).
- **Никогда не озвучивать сырые числа.** Delta фразирует чисто («braking 6m later»), не «3930мс». Подтверждает находку про leak сырых величин.
- **Детекция missed-apex/wrong-line как first-class fault** — жалоба №5, которую reference-line tools ловят, а SimCoach — нет.
- **Говорить «почему», а не только «раньше/позже».** Документированная слабость trophi («не объясняет почему теряешь grip») — ровно жалоба №3 ([dontwastemy.energy](https://dontwastemy.energy/2026/01/17/sim-racing-with-ai-coach/)). Это естественная роль LLM.
- **Наблюдаемость accept/fallback** — ни один конкурент не работает вслепую; SimCoach молчаливо теряет 56% на template.

**Незанятый дифференциатор:** **real-time RU-голос** — ни trophi (есть 59 языков, но vague-advice + cognitive load), ни Delta (голос только в roadmap) не закрывают нишу качественного русскоязычного live-коуча. Но это преимущество реализуется ТОЛЬКО если сначала взять под контроль каденцию и качество. Плюс **local-only privacy** (как Race Element) — уже правило SimCoach, стоит явно позиционировать против cloud/subscription-конкурентов.

---

## 6. Приоритизированный бэклог — что чинить/добавлять

P0 = грубые баги, которые видит/услышит пользователь и которые подрывают доверие. P1 = существенные качество/полнота. P2 = гигиена/долг.

| Приор. | Пункт | Тип | Куда смотреть |
|---|---|---|---|
| **P0** | Исключить out/in/незачётные круги из session-агрегатов и debrief; убрать инверсию debrief на PB-круге (#B/#30/#21) | данные | `ComputeSession.cs:195-235/421-430`, `SessionLossAccumulator.cs`, `GoldArtifactBuilder.cs:87-88/125-133` |
| **P0** | Чинить corner delta_ms (self и ref на одном span) — константа 3929мс Curva Grande (#A/#29) | данные | `CornerEventBuilder.cs:79-92`, `CornerTracker.cs:29-71`, `cornerGeometry.monza.json` |
| **P0** | Подавлять realtime corner/sector-типы на out/in-круге (гейт «зачётный flying lap») (#C/#31) | данные | `ComputeSession.cs:207-218`, `RuleEngine.cs`, `CornerEventBuilder.cs:83-85` |
| **P0** | Убрать сырые мс/метры из озвучки + magnitude/sanity-clamp перед эмиссией (#D/#20/#33) | данные | `PhraseRenderer.cs:52-63`, `actionRegistry.json:234/17/249/293`, `TipValidator.cs` |
| **P0** | Отказаться от итальянских имён в озвучке: номер поворота ИЛИ опустить (это последний поворот) (#P/#2) | продукт | `coach.system.v1.ru.txt` (правило 2), `CoachService.cs:299-311`, `CornerNameMap.cs`, `cornerNames.json` |
| **P0** | Чинить `lap_pb` пустой плейсхолдер «Главная зона — .» (политика отсутствующего параметра) (#N/#7/#16) | баг | `actionRegistry.json:310`, `GoldArtifactBuilder.cs:117`, `PhraseRenderer.cs:22/42-44` |
| **P1** | Фазовый контекст для `straighter_braking` (overlap только в turn-in/apex, не brake-на-прямой) (#F/#5/#12) | дизайн | `actionRegistry.json:52-66`, `BrakeOverlapSteerKernels.cs`, `CornerPhaseResolver.cs`, `SimCoach.Reference` |
| **P1** | Cadence-governor: приоритет по потере времени, cooldown, «одна вещь за раз», право промолчать (#I/#Q/#J) | продукт | `RuleEngine.cs`, `RuleEngineOptions.cs`, `CoachService.cs:200-235`, `coach.system.v1.ru.txt`, `OutputSchema.cs` |
| **P1** | Единый word-cap для template и LLM; не считать имя поворота как слова; кормить короткую RU-форму (#K/#L/#M/#17/#18/#19) | баг | `CoachService.cs:244-262`, `PhraseWordCount.cs`, `TipValidator.cs:57`, `CoachOptions.cs:15` |
| **P1** | Снизить долю `corner_catch_all`: усилить таксономию (4-фазная модель) / убрать числовой catch_all из озвучки (#G/#10) | дизайн | `actionRegistry.json`, `ClauseEvaluator.cs`, `coach.fewshot.v1.ru.json` |
| **P1** | Reference-free действие «широкая линия / мимо апекса» на базе абсолютной геометрии; ослабить AND-min_speed в `tighten_apex` (#H/#4/#15) | дизайн | `actionRegistry.json:162-176`, `racing_line_deviation_m` в `SimCoach.Reference` |
| **P1** | Построить RU-eval-гейт (m5): LLM-судья + рубрика + фикстуры no-PB/corner/debrief + числовой порог (#23) | полнота | `tests/` (новый eval-проект), `phase-3-detailed-plan.md:1108-1132` |
| **P2** | Per-family schema-acceptance real-HTTP фикстура (pre-pin gate) (#24) | полнота | `tests/SimCoach.LLM.Tests/SchemaTranslatorTests.cs`, `Providers/*` |
| **P2** | Наблюдаемость accept/fallback: структурный лог/счётчик source+cadence+причина отбраковки (#O/#22) | баг | `CoachService.cs:246-262`, `:391-405` (сейчас failure теряется в `out _`) |
| **P2** | `IsInPitLane` в clean-предикат (согласовать с fuel-гейтом) (#E/#32) | данные | `CleanLapPredicate.cs:29`, `ComputeSession.cs:249-256` |
| **P2** | Дедуп per corner_id+lap + межкруговая память (пересекается с cadence-governor) (#J/#11) | дизайн | `CoachService.cs`, `RuleEngine.cs` |
| **P2** | Месячный бюджет: выставить $5.00 или явно задокументировать «выключен по умолчанию» (#25) | полнота | `appsettings.json:52`, `RuleEngine.cs:102-104` |
| **P2** | Doc-drift: FR-061 → claude-sonnet-4.6; DeepSeek «not yet added»; сверить FR-014/060/072 (#27/#28) | полнота | `functional-requirements.md:81`, `phase-3-detailed-plan.md:1166-1172` |
| **P2** | Решить роль LLM: арбитраж/abstain/объяснение «почему» vs честно оставить фразером (#8/#9/#13/#14) | дизайн (владелец) | `PromptBuilder.cs`, `GoldArtifactBuilder.cs`, `OutputSchema.cs`, `ClauseEvaluator.cs` |

---

*Примечание по калибровке: все находки заземлены на файл:строку, строку лога или строку БД. Уже-исправленные баги (debrief HTTP 400 → 28dd7b6, timeout 8→20с → 3974249, cost undercount → f2502b4, shutdown drain → 8397ba0, non-monotonic-lap crash → b788fbe) и артефакты fake-провайдера (сессия 154931 «Тормози позже.») НЕ включены как живые дефекты. Авторитетный прогон текущего main — `20260701-171602-738`.*
