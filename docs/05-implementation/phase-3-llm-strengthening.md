# Усиление LLM-слоя SimCoach (Phase 3) — план доработок

*Третья часть ревью Phase 3. Сессия-эталон `20260701-171602-738` (Monza / BMW M4 GT3 / dry-warm / reference=true). Все ссылки — на файлы в состоянии этой сессии. Ценовые/модельные факты проверены 2026-07-01.*

> **Сводный план:** [`phase-3-master-backlog.md`](phase-3-master-backlog.md) — единый приоритизированный бэклог всех трёх ревью. Пункты n1…n39 ниже вошли туда как M1…M43. Также: [`phase-3-acceptance.md`](phase-3-acceptance.md) (дефекты продукта/UX), [`phase-3-acceptance-addendum.md`](phase-3-acceptance-addendum.md) (ground-truth валидация детекции).

---

## 1. Резюме

**Стоит ли усиливать LLM сейчас — да, но с честной оговоркой.** LLM в SimCoach — это селектор+фразер: все телеметрические решения алгоритмические (`ClauseEvaluator`/`ActionRegistry.ValidSubset`), а модель лишь выбирает один `action_id` из готового меню (≤5) и пишет русскую фразу. Поэтому **усиление LLM НЕ чинит неверные числа из детекции** — параллельное ground-truth-ревью доказало, что kernels отдают фактически неверные скаляры, и это лечится только алгоритмическим guard'ом, а не промптом. Любой рычаг, где LLM «объясняет причину числом», упирается в корректность этого числа.

Но значительная часть улучшений LLM **независима от детекции и даёт быстрый выигрыш** по трём болям владельца: «info-garbage / TTS не успевает», «зоопарк транслитераций итальянских имён», «иногда неуместный/не-в-контексте совет».

**5 главных рычагов (по убыванию отдачи на единицу усилий):**

1. **Промпт-дисциплина краткости + один совет на фразу** (n1, n2) — прямой ответ на «TTS не успевает». Чистое изменение промпта/few-shot, нулевая цена.
2. **Русская политика имён + канонический `corner_name_ru` в Gold** (n3, n34, n10) — убивает зоопарк «Curva Grande» ↔ «Роджи» на корню, одним полем и одним правилом.
3. **Право промолчать (abstain) + привязка фразы к смыслу выбранного `action_id`** (n29, n30, n8) — снимает ungrounded-эмбеллишмент (tip 46/54) и «банальности» на catch-all.
4. **Роутинг/конфиг: `temperature=0`, явное thinking-off, свап corner-модели на `gemini-3.1-flash-lite`** (n21, n22, n15, n16) — детерминизм фраз + сокращение хвоста таймаутов (2/10 corner-вызовов в эталоне ушли в template).
5. **Bounded-аналитик на debrief** (n32, n38, n31) — единственный каденс с бюджетом (Sonnet 4.6 / effort=low / 20s), где LLM может делать реальный (ограниченный) анализ: sanity-check противоречивых потерь и заземление имён поворотов. Это конкретный ответ на «зачем вообще LLM».

**Цена — не связывающее ограничение:** вся сессия ≈ $0.009 при `SessionBudgetUsd=0.50`. Связывающие ограничения — латентность/таймауты corner-каденса и качество фраз.

---

## 2. Промпты и few-shot

Текущий real-time-промпт (`coach.system.v1.ru.txt`, 5 правил) разрешает компаундные советы, разрешает числа, велит называть место «по `corner_name` дословно» (то есть по сырому итальянскому) и нигде не даёт cold-start-ветки, права промолчать или привязки фразы к смыслу действия. Few-shot активно **учат** двум идеям во фразе. Ниже — что переписать.

### 2.1. Один совет на фразу, по каденсам (n1, high, quick-win)

**Проблема.** `coach.system.v1.ru.txt:5` говорит лишь «Одна короткая подсказка… не больше слов». Few-shot моделируют два императива: `coach.fewshot.v1.ru.json:18` «Шире вход в Eau Rouge, неси скорость.», `:44` «Чисто, но на 260 мс медленнее. Поработай над Bus Stop.». В эталоне это воспроизвелось: tip 46 «Чуть раньше газ в Curva Grande, выровняй траекторию.», tip 54 «…добавь газ раньше, держи скорость выше.» — оба компаундные.

**Скоуп по каденсам (важно — не запрещать союзы/запятые глобально, это сломает пунктуацию имён):**
- **Corner (8 слов):** добавить в правило 2 —
  > «РОВНО одна команда — самое важное действие. Без второго совета, без «и/а/но», соединяющих два совета.»
  Переписать corner/no-pb few-shot в один императив: «Шире заходи в О-Руж.», «Плавнее руль в Роджа1.».
- **Sector/lap (25 слов):** разрешить максимум «оценка потери + один приоритет» (существующие sector/lap few-shot уже подходят), но запретить второй корректирующий совет.

Это главный рычаг против «info-garbage»: ≤8-словная одноклаузная фраза — один TTS-чанк.

### 2.2. Запрет произносимых чисел в real-time, числа только в debrief (n2, medium, quick-win)

`coach.system.v1.ru.txt:7-8` (правило 3) сейчас **разрешает** числа; few-shot учат («Теряешь 180 мс…» `:31`, «на 260 мс медленнее» `:44`). Цифры на слух в повороте бесполезны.

Разбить правило по каденсу. Real-time:
> «В голосовых подсказках НЕ называй числа и единицы (метры, км/ч, мс) — на слух в повороте они бесполезны. Говори качественно: «чуть позже», «немного шире».»

Числа оставить **только** в `coach.system.debrief.v1.ru.txt` (водитель в боксах слушает спокойно). Убрать «180 мс»/«260 мс» из sector/lap few-shot. Числа по-прежнему доступны overlay/DB — просто не голосу. При желании вынести в config-toggle (кто-то любит «тормози на 5 м позже»).

### 2.3. Жёсткая русская политика имён поворотов (n3, high, medium)

**Корень зоопарка.** `GoldArtifactBuilder.cs:30` кладёт `CornerName = _names.ResolveName(...)` — длинное, часто сырое итальянское имя. Русские формы существуют (`CornerNameMap.GetShort` → «Гранде»/«Роджа1»/«Лезмо1»/«Параб.», используется для template в `CoachService.cs:305`), но **в Gold не попадают**. Правило 2 велит называть место `corner_name` дословно → итальянское проходит насквозь. В одной сессии: «Curva Grande»/«Curva di Lesmo 2» (сырой IT, template) ↔ «Роджи»/«Лесмо 1»/«Параболике»/«Аскари» (ad-hoc translit, LLM).

**Фикс (совмещён с n34/n10):**
1. Добавить в Gold поле `corner_name_ru`, **источник — `CornerNameMap.GetShort`** (это и есть авторская русская форма). ⚠️ **НЕ `GetSpokenRu`** — он возвращает итальянскую базу с русским порядковым «(N)», то есть латиницу оставит.
2. Переписать правило 2:
   > «Называй поворот ТОЛЬКО русской формой из `corner_name_ru`, дословно. НИКОГДА не произноси итальянское/английское имя и не транслитерируй сам. Если `corner_name_ru` нет — скажи «здесь»/«на входе» без имени.»
3. Переписать ВСЕ few-shot на русскую short-форму («О-Руж», «Комб1», «Роджа1»).
4. **Также переключить template-путь** (`CoachService.cs:305`) на `GetShort`, иначе tips 49/53/55 останутся сырым итальянским.

Долгосрочно (P4/TTS): `GetShort` даёт куцые формы («Параб.», «Ретт.1»), плохо читаемые голосом — стоит завести отдельное поле `spoken_ru` в `cornerNames.json`. Приватность OK: имена трасс — публичные факты.

### 2.4. Привязка `phrase_ru` к смыслу выбранного `action_id` (n4/n30, medium, quick-win)

Никакое правило не требует, чтобы фраза выражала выбранное действие; хинты доходят до модели только по-английски (`PromptBuilder.cs:130`, `action.HintEn` = «understeer + low min speed»). Дрейф в эталоне: tip 54 `action_id=higher_min_speed`, но фраза «добавь газ раньше» (это другое действие); tip 46 `corner_catch_all`, но LLM сочинил «выровняй траекторию» вне действия и вне Gold.

- Заменить в меню `valid_actions` английский `HintEn` на **русский** интент — добавить `HintRu`/`hint_ru` в `CoachAction`/`actionRegistry.json`, отдавать в `PromptBuilder`. +несколько токенов на действие (≤5).
- Добавить правило:
  > «`phrase_ru` обязана выражать смысл выбранного `action_id` (см. его hint) — не другого действия и не выдуманного совета. Если для действия нет конкретики в Gold — дай общий совет строго в русле этого действия.»
- **Не занейтралить `corner_catch_all`:** явно разрешить катч-оллу оставаться простым «здесь теряешь время / сосредоточься на этом повороте» вместо выдуманной brake/throttle-конкретики.
- Добавить corner anti-example: `valid_actions=[higher_min_speed]`, фраза про торможение → «Неверно: фраза не про выбранное действие».

Impact medium (не high): enum в схеме уже гарантирует правильный `action_id`; это улучшает **релевантность фразы**, не корректность. Мягкое prompt-enforcement на `gemini-2.5-flash-lite`; для жёсткой гарантии — лёгкий post-parse-чек (фраза упоминает канал/глагол действия) как follow-up.

### 2.5. Cold-start (нет эталона) ветка (n7, medium, quick-win)

При `has_reference=false` builder опускает все reference-поля (`GoldArtifactBuilder.cs:31-38`), но промпт про это молчит — правило 2 всё ещё велит называть место, ничего не запрещает сравнительные слова. Добавить:
> «Если `session.has_reference=false` — эталона нет. Не используй «медленнее», «быстрее», «теряешь», «отклонение». Давай совет по абсолютному поведению машины (снос, занос, пробуксовка, вылет).»

Существующий `no-pb` few-shot (`coach.fewshot.v1.ru.json:46-58`) оставить парным позитивом.

### 2.6. Активировать мёртвое поле `reason` + русский глосс (n35, medium, quick-win)

`reason` уже вычисляется (`CornerEventBuilder.cs:150-173`, закрытый набор: `off_track`/`late_throttle`/`early_brake`/`low_min_speed`/`slower`) и уже едет в corner-JSON (`CornerGoldView.cs:59`), но ни промпт, ни few-shot им не пользуются, и это сырой английский токен.

- Лучше — эмитить `reason_ru` через статическую карту (LLM/TTS никогда не видят английского). Карта должна покрыть ВСЕ пять меток: `late_throttle`=поздний газ, `early_brake`=раннее торможение, `low_min_speed`=низкая скорость в апексе, `off_track`=вылет, `slower`=общая потеря темпа.
- ⚠️ **`reason` — грубая эвристика со смешанными единицами** (`CornerEventBuilder.cs:144-148`), считается отдельно от `action_id` и может ему противоречить. Поэтому: `action_id` — главное, `reason` — совещательная подсказка.
  > «`action_id` — главное, чему должна соответствовать фраза. Поле `reason` — подсказка о причине; используй, только если не противоречит `action_id`; при расхождении следуй `action_id`. Число из `reason` не называй.»

### 2.7. Few-shot как реальные user/assistant-ходы + per-cadence негативы (n5, low, medium)

`PromptBuilder.cs:87-112` вклеивает примеры простым текстом в **system**-роль («Примеры:»/«Запрос:»/«Ответ:»), не структурными ходами → слабее fidelity. `PromptBuilder.cs:79` добавляет ВСЕ негативы в КАЖДЫЙ real-time-каденс, так что corner-негатив (Pouhon/«5 м», `:85-98`) показывается sector/lap, где он не к месту.

- Строить few-shot как чередующиеся user/assistant-сообщения в `LlmRequest` (OpenRouter/Anthropic/Gemini все принимают multi-turn), оставив system-текст единым кэшируемым префиксом.
- Фильтровать негативы по ключу каденса (зеркалить позитивный фильтр `PromptBuilder.cs:75`).
- Добавить по одному sector- и lap-специфичному негативу. Также разблокирует чистую расстановку cache-breakpoint'ов.

### 2.8. Retry-промпт должен называть причину отказа (n6/n14, low, quick-win)

`coach.retry.v1.ru.txt` — общий («не прошёл проверку, верни валидный JSON»), причину не называет; `CoachService.cs:404` (real-time) и `:417` (debrief) выбрасывают диагностику `TipValidator` через `out _`. Прокинуть `failure`-строку в retry-append: «Причина отказа: превышен лимит слов (было N, лимит M).» / «Причина: action_id «X» не входит в valid_actions.».

Честно: выигрыш узкий. Retry срабатывает только по `IsRetryable` (`CoachService.cs:428-433`) = Success-провал-валидации или `SchemaViolation`, и только sector/lap/debrief (не corner — 8/10 вызовов эталона). Не помогает наблюдавшемуся кейсу (lap-retry истёк по таймауту — это transport). Чистый плюс, но маргинальный salvage-rate, не medium-рычаг.

---

## 3. Output-схема и право промолчать (abstain)

Сейчас abstain **не существует**: `RuleEngine.ShouldSpeak` (`CoachService.cs:205-211`) — единственный гейт тишины ДО LLM, пустой subset роняет `PromptBuilder` (`:40-44`), значит LLM обязан вернуть `action_id` из enum, а любой промах уходит в детерминированный template `subset[0]` (`:262`), никогда в тишину.

### 3.1. Первоклассный abstain (n8) / sentinel `"none"` для слабого catch-all (n29) (medium, medium)

Рекомендуемый механизм — **sentinel-член enum** (устойчив на всех трёх schema-family без nullable-гимнастики): добавлять `"none"` в `action_id.enum` **только когда** `subset[0].Priority.Rank >= CatchAllRank` (конфиг, напр. 900 — `corner_catch_all` rank 900, `sector_catch_all` 910, `lap_catch_all` 905 в `actionRegistry.json`). Правило:
> «Если ни одно из valid_actions не даёт полезного совета (только общее отклонение) — верни `action_id="none"` и пустую `phrase_ru`. Лучше промолчать, чем сказать банальность.»

В `CoachService` трактовать `"none"` как тишину (не `EmitTip`), эквивалент `RuleOutcome.Silent`. **Жёсткие границы:** `none` предлагается только когда единственный/слабый сигнал — catch-all (диагностированное действие rank<900 задавить нельзя); High-severity (Entry-фаза, `CoachOptions.SeverityFor`) никогда не абстейнит; lap/sector-майлстоуны abstain не получают. Наблюдать счётчиком silent-tips против over-silence. Цена/латентность — ноль (тот же вызов). Прямо бьёт по tip 46.

### 3.2. Word-cap не должен считать имя поворота (n9, medium, medium)

`PhraseWordCount.Count` (`PhraseWordCount.cs:10-13`) — сырой whitespace-split по всей фразе; corner-бюджет 8 слов (`CoachOptions.cs:15`). «Чуть сбрось газ на входе в Curva di Lesmo 2.» = 10 токенов, 4 из которых — имя; корректная 4-словная инструкция была бы отклонена → retry (которого у corner нет) → template.

**Фикс = канонический 1–2-словный `corner_name_ru` в Gold (см. §2.3)** — имя становится 1–2 токена, реальная инструкция укладывается в 8. **НЕ** ослаблять валидатор вычитанием имени (ломает TTS-цель кэпа и мискаунтит склонения). Если нужен запас — поднять `InCornerMaxWords` 8→9, но канонический-имя-фикс делает и это ненужным. Пункт во многом поглощён §2.3.

### 3.3. Guard «без выдуманных чисел» на уровне валидации (n11, low, medium)

Правило 3 — только prompt; `TipValidator` фразу на числа не смотрит. Надёжный low-risk guard: **на corner-каденсе — по умолчанию без чисел**: отклонять `phrase_ru`, если есть цифра, не входящая в токен имени/порядкового (сначала вычесть токены имени, потом флагить остаток). Если числа всё же разрешать — заземлять **с допуском**, не точным substring: для каждого числа требовать Gold-значение в пределах шага округления поля (±0.5 для 1-dp метров/км/ч), иначе легитимное округление (Gold 4.7 → «5») отклоняется. Самоотчётный `numbers_used`-массив чище, но требует того же допуска + токены/моделирование.

Поправка к evidence: tip 46 — это дрейф семантики действия, **не** галлюцинация числа; ни одна emitted-фраза эталона не содержала выдуманного числа. Это закрытие латентной дыры (low); более ценный guard для тех же строк — привязка фразы к `action_id` (§2.4).

### 3.4. Bounded confidence-поле (n12/n33, low, quick-win)

Добавить `"confidence":{"type":"string","enum":["high","low"]}` в real-time `OutputSchema` (enum, не float — Gemini срезает numeric-bounds, `GeminiSchemaTranslator.cs:12-22`). Промпт: «`confidence=low`, если совет очевиден, дублирует прошлый или слабо обоснован Gold; иначе `high`.» При `confidence=low` И `severity != High` — задавить TTS (через abstain-путь) или terser-template.

Честно: сигнал **эмитится после вызова**, значит не снижает ни цену, ни 2/10 corner-таймаутов — только info-garbage к TTS. Действия уже валидированы `ClauseEvaluator`, LLM видит только Gold-скаляры → сигнал слабый/избыточный, склонен всегда быть «high». Деплоить **только вместе с abstain-гейтом** и после замера калибровки (логировать confidence vs `TipSource` + ручные relevance-метки на реальных `coach_tips`).

### 3.5. Робастность per-family трансляции (n13, low, quick-win)

Real-time enum (`OutputSchema.cs:40`) — сильнейший guard — корректно сохраняется всеми тремя трансляторами (Gemini не срезает `enum`). Вторичные дыры:
- `GeminiSchemaTranslator` срезает `maxItems` (`:21`) → debrief `maxItems:5` до Gemini не доходит, ≤5 держится только post-parse `TipValidator.cs:98`. Приемлемо для текущего anthropic-debrief, но латентная ловушка при перероутинге на google/*. Добавить config-guard: debrief не роутить на Gemini без подтверждённого post-parse-enforcement + комментарий/тест, что срез намеренный.
- `TryValidateDebrief` (`:111-117`) не проверяет, что у каждой потери непустой `corner` и числовой `ms` — ужесточить, чтобы malformed-but-parseable ловился и ретраился.
- Логировать сырые `finish_reason`/`content`, когда `tool_calls` пуст (`OpenRouterProvider.cs:211-222`), чтобы отличать refusal от transport-fail.

---

## 4. Выбор и роутинг моделей

Текущее: все каденсы на `google/gemini-2.5-flash-lite` кроме debrief=`anthropic/claude-sonnet-4.6` (`appsettings.json:59-65`). Проблема эталона: corner p50≈1.0s, **2/10 вызовов — таймаут** на 2.0s-кэпе (rows 50/56, 0 токенов) → template; corner **без retry** by design (`CoachService.cs:244`), так что таймаут = немедленная потеря качества.

### Рекомендуемая таблица «каденс → модель → почему»

| Каденс | Рекоменд. модель | $/Mtok in→out (OpenRouter) | Лимиты | Почему | Источник |
|---|---|---|---|---|---|
| **corner** | `google/gemini-3.1-flash-lite` (свап с 2.5) | **$0.25 / $1.50** | context (docs), thinking off | 2.5× быстрее TTFT, +45% output-speed, sub-second p95 для tool/classifier — режет хвост таймаутов на 2.0s-кэпе; ~$0.0003/corner, sub-cent/сессия | [Google Cloud blog GA](https://cloud.google.com/blog/products/ai-machine-learning/gemini-3-1-flash-lite-is-now-generally-available); [OpenRouter](https://openrouter.ai/google/gemini-3.1-flash-lite) |
| **sector/lap/strategy** | `google/gemini-2.5-flash-lite` (без изменений) | **$0.10 / $0.40** | 1M / 65 535 | ценовой пол; бюджеты 2.5/3.0s имеют запас латентности | [ai.google.dev pricing](https://ai.google.dev/gemini-api/docs/pricing) |
| **debrief** | `anthropic/claude-sonnet-4.6` (без изменений) | **$3.00 / $15.00** | 1M / 64K, effort low/med/high | правильный выбор: единственное место, где богатое reasoning оправдано; эталон 4361ms/$0.0079 — глубоко в 20s/$0.50 | `claude-api` skill catalog; [OpenRouter](https://openrouter.ai/anthropic/claude-sonnet-4.6) |

### 4.1. Свап corner-модели (n15/n16/n20, medium, quick-win)

Действие — однострочное: `Llm:Routes:corner:ModelId` с `google/gemini-2.5-flash-lite` на `google/gemini-3.1-flash-lite` + добавить rate-card в `Llm:Providers:openrouter-google:Rates`. Кэп 2.0s **не поднимать** (совет после поворота бесполезен). Thinking явно off (`Reasoning=Off` → `reasoning.enabled=false`), подтвердить, что 3.1 это чтит.

⚠️ **Честная оговорка:** оба таймаута логировали `latency_ms=0` и 0 токенов против ~1s-медианы — это **бимодальный stall** (TTFT/routing/connection), а не хвост генерации, ползущий к кэпу. Быстрая модель улучшает медианный TTFT, но stall-таймауты может не убрать полностью. Свап — **необходим-но-возможно-недостаточен**; пары: prompt-caching статического префикса (§5), явный thinking-off, и решение по >1 сессии (20% — из одного 10-поворотного прогона).

### 4.2. RU-качество one-liner'а — A/B перед коммитом (n19, medium, medium)

Ни один бенчмарк не изолирует качество русской ≤8-словной фразы для Gemini flash-lite / DeepSeek / Qwen / Haiku. Кандидаты дешевле: **DeepSeek V4 Flash** ($0.098/$0.196 OR — самый дешёвый, сильный RU), **Qwen3.6 Flash** ($0.1875/$1.125 OR, 29+ языков). Прогнать shadow-A/B на реальных Gold-событиях: `gemini-2.5-flash-lite` vs `gemini-3.1-flash-lite` vs `deepseek-v4-flash` vs `qwen3.6-flash` по: adherence `action_id`, compliance ≤max_words, RU-натуральность. Логировать в `coach_tips`-подобные строки, руками оценить ~50 поворотов. Выбрать по данным, не по дефолту.

### 4.3. Реальная fallback-цепочка (n17/n18/n23, medium, medium)

`FallbackRouteKey` — **мёртвый конфиг**: `LlmRouter.cs:52-59` переключается на fallback **только по `CircuitOpen`**, и ни один route его не задаёт. Debrief имеет реальные кросс-сессионные фейлы без fallback (log row 46 `server_error`, row 37 `timeout`) → сразу `DebriefTemplate`.

Две связанные части (порядок важен):
1. **Сначала расширить триггер роутера:** переключаться на `FallbackRouteKey` также на transient (Timeout, ServerError/Transport), не только CircuitOpen. Иначе изолированный таймаут/500 (rows 37/46) не фолбэкнется, а debrief (раз в сессию) свой breaker почти никогда не откроет.
2. **Потом задать `FallbackRouteKey`:** debrief → `anthropic/claude-haiku-4.5` ($1/$5 OR, 200K, structured output; `Reasoning=Off` — Haiku 4.5 **отвергает `effort`-параметр**), сохраняет аналитику против плоского template; sector/lap → альтернативная каденс-модель. Кросс-провайдерный fallback (`deepseek-v4-flash`) требует регистрации provider+rate-card в `Llm:Providers` (голый `FallbackRouteKey` на незарегистрированный provider бросает в `ProviderFor`, `LlmRouter.cs:89-92`) + A/B RU-качества.
3. **Corner fallback остаётся Template** (2s-бюджет не даёт второй попытки).

**Circuit breaker** (`appsettings.json:89-93`: 3 фейла/60s/60s-break): 3 corner-таймаута в одном 60s-окне правдоподобны на хот-лэпе; открытие глушит все каденсы на `openrouter-google` на 60s (деградация к template, не полная тишина; debrief на отдельном провайдере не затронут). Либо (a) поднять `FailureThreshold`~6 и сократить `BreakDuration`~20-30s, либо (b) задействовать реальный fallback. Раз фейлы тут — таймауты (не auth/500), склониться к пермиссивности верно.

`IsRetryable` (`CoachService.cs:428-433`) расширить, чтобы sector/lap/debrief ретраились раз на transient transport/server_error — но это тот же **model**, помогает transient-500, не outage; retry debrief по таймауту добавляет до +20s к shutdown-drain, гейтить аккуратно.

---

## 5. Конфигурация и параметры

### Per-route целевая таблица

| Route | Model | temperature / top_p | MaxOutTok | timeout | reasoning | retry | caching | streaming |
|---|---|---|---|---|---|---|---|---|
| corner | gemini-3.1-flash-lite | **0 / 1.0** (добавить) | 96 (keep) | 2.0s (keep) | Off + explicit `thinking_budget=0` | none (keep) | авто (3.1) | нет |
| sector | gemini-2.5-flash-lite | **0 / 1.0** | 192 (keep) | 2.5s | Off + explicit | 1 | verify passthrough | нет |
| lap | gemini-2.5-flash-lite | **0 / 1.0** | 192 (keep) | 3.0s | Off + explicit | 1 | verify passthrough | нет |
| debrief | claude-sonnet-4.6 | 0 (Sonnet 4.6 принимает) | 2000 (keep) | 20s (keep) | Low (keep) | 1 | cache_control если префикс≥2048 | **P4-only** |
| strategy | gemini-2.5-flash-lite | **0 / 1.0** | 192 | 3.0s | Off | — | — | нет |

### 5.1. Задать `temperature=0`, `top_p=1` (n21, low, quick-win)

`OpenRouterProvider.BuildBody` (`:85-99`) эмитит только model/messages/max_tokens/stream/reasoning/usage; `RouteOptions` **вообще не имеет** sampling-полей. Best-practice для короткой структурной генерации: `temperature=0`, `top_p=1.0`, крутить **только один** (общий softmax); высокая температура «почти всегда вредит» структурному выводу ([SurePrompts 2026](https://sureprompts.com/blog/llm-temperature-sampling-complete-guide-2026); [raiaai](https://www.raiaai.com/blogs/optimizing-openai-configuration-temperature-json-format-and-more-for-different-use-cases)). Добавить `Temperature`/`TopP` в `RouteOptions`, эмитить в `BuildBody`. Sonnet 4.6 температуру принимает (Opus 4.8/4.7 — нет, 400; здесь не используются). Снижает run-to-run вариативность фраз и off-topic-эмбеллишмент. Риск ≈0 для селектора+фразера.

### 5.2. Reasoning-tokens: персистить + подтвердить thinking-off (n22, low, quick-win)

Реальный gap: `llm_usage` **не имеет** колонки `reasoning_tokens`, хотя `OpenRouterProvider.ReadUsage` (`:237-241`) их читает, а `CostCalculator.cs:19` биллит по output-rate. Поправка framing'а: **дollar-cost отслеживается корректно** (`cost_usd` включает reasoning и правильно кормит бюджет) — это дыра **наблюдаемости**, не недоучёт цены. Добавить колонку `reasoning_tokens INTEGER` в схему + `LlmUsageRow` (`SqliteCostMeter.cs:45-58`), чтобы reasoning был атрибутируем per-row и «thinking off» проверялся из данных. Держать `Reasoning:Off` на one-liner'ах, `Low` на debrief. Затем проверить из колонки, что Gemini-каденс реально стоит 0 reasoning-токенов; если кусает [python-genai #782](https://github.com/googleapis/python-genai/issues/782) (structured output игнорит thinking-off) — ставить `thinking_budget=0`/`thinking_level:minimal` явно, не полагаясь на `reasoning.enabled=false`. Латентный payoff (защита 2s-бюджета от stray-reasoning) — гипотеза, не доказанный дефект.

### 5.3. Prompt-caching статического ~1150-токенного префикса (n24, low, medium)

`cached_input_tokens=0` на каждой строке `llm_usage`; corner-input 1130-1205 токенов, system+few-shot байт-идентичны каждый поворот. Gemini 3.1 Flash-Lite даёт **автоматический context-caching** (без кода) — бесплатный выигрыш при свапе (§4.1). На 2.5 Flash-Lite через OpenRouter — проверить, пробрасывается ли implicit caching. Для Anthropic-debrief: `cache_control` breakpoint на префиксе, **НО** min cacheable prefix Sonnet 4.6 = 2048 токенов (`claude-api` skill), текущий debrief-префикс ниже → молча не закэшируется; стоит только если префикс вырастет или debrief гоняется многократно. Цена не связывающая (сессия $0.009), так что абсолютная экономия мала сегодня — низкий приоритет против латентности. (Экономика Anthropic: read≈0.1×, write 1.25× 5m / 2× 1h; break-even ~2 запроса.)

### 5.4. Streaming — P4-only, только debrief (n25, low, large)

`OpenRouterProvider.StreamAsync` бросает `NotSupportedException` («declared for P6»); `RouteOptions.Stream` — мёртвый конфиг. В **P3 debrief персистится как текст один раз по завершении — streaming не даёт ничего**. Переформулировать как P4-TTS-подготовку: когда придёт TTS, вайрить streaming **только для debrief** (one-liner меньше TTS-чанка — выигрыша нет). Так как debrief — структурный JSON, не стримить токены прямо в TTS: либо buffer+schema-validate потом synth-stream рендер, либо модель эмитит отдельное plain-text spoken-поле рядом с JSON. Impact low: debrief — раз-в-сессию post-session-сводка (водитель в боксах). `MaxOutputTokens=2000` держать как truncation-guard (биллятся только реальные 173 out).

### 5.5. Ненулевой `MonthlyBudgetUsd` перед Live (n26, low, quick-win)

`appsettings.json:51` `MonthlyBudgetUsd=0` (=off); активен только `SessionBudgetUsd=0.50`. При ~$0.009/сессия тяжёлый пользователь ~5 сессий/день ≈ $1.4/мес; выставить `MonthlyBudgetUsd` в safe-ceiling (напр. $5-10) как guard от runaway/misconfig (случайный debrief-луп или route на Sonnet) **перед** `Llm:Live=true`. Слишком низкий кэп молча деградирует реальные сессии — брать с запасом.

### 5.6. `MaxOutputTokens` уже верны — не трогать (n27, low)

Corner-output 31-42 токена против кэпа 96; sector/lap=192; debrief-факт 173 против 2000. `max_tokens` — потолок, не биллится на неиспользованном. Держать corner=96 (запас против mid-JSON-truncation, которого corner-retry не переживёт). Флаг, чтобы будущий «cost-trim» не ужал corner в truncation-риск (<~64 опасно).

---

## 6. Повышение роли LLM (ответ на вопрос владельца)

«Зачем LLM, если детекция алгоритмическая — он только пишет текст?» Ответ: дать LLM **bounded**-аналитическую ценность там, где каденс это позволяет, не отдавая ему выбор действия. Целевой баланс по каденсам:

- **corner** = evidence-weighted селектор + фразер + bounded abstain (без доп. латентности/reasoning, Gemini 2.5/3.1 Flash-Lite, thinking off, temp 0);
- **lap** = императив + одна клауза «почему»;
- **debrief** = **реальный аналитик** (заземление «почему» + plausibility-guard) на Sonnet 4.6 effort=low.

### 6.1. Арбитраж: выбор по доказательствам, не blind-first-pick (n28, medium, quick-win)

Gold уже везёт все диагностические каналы на КАЖДОМ corner-вызове (`GoldCornerEvent.cs:11-28`: understeer/oversteer/wheelspin_score, trail_brake_diff_pct, brake_overlap_steer_pct, min_speed_diff_kmh, brake_point_diff_m…), и pick LLM = то, что произносится. Но правило 1 велит лишь «выбери один», без взвешивания. Добавить:
> «Среди `valid_actions` выбери то, чью причину лучше всего подтверждают числа в Gold (`understeer_score`/`oversteer_score`/`wheelspin_score`>0.6, `|trail_brake_diff_pct|`, `min_speed_diff_kmh`, `brake_point_diff_m`). Если несколько действий co-fire — назови самое влияющее на потерю времени.»

Нулевой доп-контекст (скоры уже есть), нулевая латентность. LLM становится bounded evidence-weighted-селектором вместо дефолта по priority-order. Риск: over-weight шумного скора — митигируется тем, что алгоритмические clause-пороги остаются гейтом (LLM только re-rank внутри уже-валидного subset, никогда не добавляет действий).

### 6.2. Abstain на слабом catch-all — см. §3.1 (n29, medium, medium)

Прямой ответ на «неуместный совет + TTS-garbage». Границы жёсткие (только catch-all-fire, High никогда не абстейнит), наблюдаемо silent-counter'ом.

### 6.3. Привязка фразы к смыслу — см. §2.4 (n30, medium, quick-win)

### 6.4. Анализ «почему» перенести на lap/debrief (n31, low, medium)

Corner-бюджет 8 слов/2s/20%-таймаутов — места для cause-клаузы нет. Распределить:
- **DEBRIEF (prompt-only, можно сейчас):** ужесточить `coach.system.debrief.v1.ru.txt` так, чтобы каждый `top_losses.why` = **именованная категориальная причина** (из `aggregated_losses.reason`/`DominantReason`, напр. «снос на входе») + потеря **в мс** («−120 мс»), НЕ значение канала («min_speed −6 км/ч»). Мс — единственное per-loss число в debrief-Gold.
- **LAP (можно сейчас):** одна качественная клауза-причина после императива, заземлённая только в lap-Gold per-loss reason + мс — без выдуманных чисел канала.
- **CORNER:** голый императив+место, не трогать.
- **Опциональный follow-up для «канал+число» на debrief:** требует изменения ДЕТЕКЦИИ/Gold — обогатить `AggregatedLoss`/`GoldAggregatedLoss` числовым diff доминантного канала рядом с `DominantReason`. Это **не** prompt-only quick-win.

### 6.5. Plausibility-guard ТОЛЬКО на debrief (n32, medium, medium)

Ground-truth-ревью доказало: детекция отдаёт фактически неверные числа. Debrief — единственный route с headroom (Sonnet 4.6 / Low / 20s) на sanity-check.

- **QUICK WIN (prompt-only, делать первым):** добавить в `coach.system.debrief.v1.ru.txt` bounded-consistency-инструкцию на полях, **уже** в Gold:
  > «Если `dominant_reason="slower"` (нет явной причины), но `total_loss_ms` велик, или `avg_loss_ms * sample_count` не согласуется с `total_loss_ms` — понизь приоритет этой потери в top_losses и коротко объясни в why. Никогда не добавляй потери, только переупорядочивай/исключай в пределах пяти.»
- **АРХИТЕКТУРНОЕ (если quick-win недостаточен):** для теста «потеря >150мс, но min_speed_diff≈0 и racing_line_deviation≈0» прокинуть диагностические скаляры: добавить в `CornerContribution` (`CornerEventBuilder.cs:13`), удерживать+усреднять per-corner в `SessionLossAccumulator` (сейчас только TotalLossMs/SampleCount/ReasonCounts), добавить поля в proto `AggregatedLoss` (contract-change + protobuf regen) и `GoldAggregatedLoss`. Это compute+contract-change с **политикой агрегации** (какое per-sample-значение представляет поворот, виденный N раз) — не «пара токенов».

Границы: OFF corner-каденса; никогда не добавлять потерю; требовать `why` для любого drop'а.

### 6.6. Машиночитаемый confidence для наблюдаемости — см. §3.4 (n33, low, quick-win)

Операционализирует всё role-elevation-множество: логировать confidence vs `TipSource`, чтобы измерить, помогает ли элевация, и позже гейтить TTS на `confidence>=med`. Самоотчётный/некалиброванный — грубый гейт, не истина; валидировать против исходов перед доверием к suppression.

---

## 7. Обогащение контекста Gold

Всё ниже — privacy-safe (только Gold-скаляры/публичная геометрия трассы, сырая телеметрия не покидает диск).

| Пункт | Что добавить | Каденс | Токены | Effort |
|---|---|---|---|---|
| **7.1 canonical RU-имя (n34)** | `corner_name_ru`=`GetShort` в `GoldCornerEvent`/`GoldCornerLoss`/`GoldAggregatedLoss` | corner/sector/lap/debrief | ~10-15/corner (<1.5%) | quick-win |
| **7.2 `reason_ru` глосс (n35)** | статическая карта 5 меток → RU | corner/debrief | ~5 | quick-win |
| **7.3 prior-lap trend (n37)** | `corner_visits`/`avg_delta_ms_session`/`trend` | corner | ~20 | medium |
| **7.4 sector→corner-membership (n38)** | `sectors[]{sector_idx, avg_delta_ms, corner_names[]}` | debrief | ~30-80 | medium |
| **7.5 session car-balance (n39)** | `avg_oversteer`/`avg_wheelspin`/`pct_corners_understeer`/`pct_corners_oversteer` | debrief | ~15 | medium |
| **7.6 per-phase brake/throttle/steer (n36)** | phase-split баланс-скоров (см. оговорку) | detection-side / sector/lap | ~40-60 | large |

**7.1 (n34, medium).** См. §2.3 — источник `GetShort` («Гранде»/«Лезмо1»/«О-Руж»), **не** `GetSpokenRu` (латиница). Также переключить template-путь (`CoachService.cs:305`). Покрытие полное для авторских трасс (Monza/Spa 100% short), деградирует к `ResolveName`. Долгосрочно — отдельное `spoken_ru`-поле в `cornerNames.json`.

**7.3 (n37, medium).** Каждый `CornerEvent` изолирован — corner-Gold без памяти; в эталоне тот же поворот бился повторно (Curva Grande tips 46&51, Lesmo 1 tips 48&52 идентичной фразой), но LLM не видит прошлых визитов. **Реализация:** новый per-corner-аккумулятор во владении `CoachService` (уже per-session-stateful), инъекция через `GoldSessionContext`. **НЕ** переиспользовать `SessionLossAccumulator` (internal к `SimCoach.Reference`, гоняется только на session-end) и **НЕ** делать `GoldArtifactBuilder` stateful (сломает golden-тестируемость). Аккумулятор должен хранить previous-visit delta для `trend`. Ценность ограничена phrasing-recurrence, не решает brevity/naming/wrong-context; гейтить против nagging и 8-словного corner-бюджета.

**7.4 (n38, medium).** `aggregated_losses` часто пуст: 3 из 4 debrief в log (lines 98/267/335) упали в Template; единственный LLM-debrief (tip 57) **сгаллюцинировал** имена («Variante del Rettifilo/Variante della Roggia»), которых в Gold не было. ⚠️ **Membership НЕ статическая геометрия:** по ADR-0010 (`TrackModel.cs:29-30`) границы секторов — только из live `current_sector_index`, у corner-модели секторов нет. Карту нужно **выводить в compute-time**, матча статические apex-позиции (`CornerGeometryEntry.ApexPosition`) против runtime sector-cross spline-позиций (`_prevSectorCrossPos`); `ComputeSession.cs:231` уже делает apex-in-sector-window-группировку per `SectorEvent` — недостающее — аккумулировать `{sectorIdx → [имена]}` за сессию и прокинуть через `SessionEvent` в `GoldSessionPayload`. Реальный medium-effort. Плюс дешёвый interim-guard независимо: prompt-правило, запрещающее любое имя поворота, которого нет в Gold. Impact medium: один low-frequency-артефакт/сессию, но это marquee-сводка и она демонстративно галлюцинирует.

**7.5 (n39, low).** `SetupHint` хардкодится `null` (`GoldArtifactBuilder.cs:91`); единственный balance-сигнал в debrief-Gold — `understeer_trend`. Агрегировать per-corner-kernels в `{avg_oversteer, avg_wheelspin, pct_corners_understeer, pct_corners_oversteer}` (understeer уже течёт как `understeer_trend` — расширить, не дублировать). Требует: append-only `SessionEvent` proto-поля; новые аккумуляторы в `ComputeSession` (рядом с `_understeerAccum`/`_oversteerAccum`, lines 202-204); поля `GoldSessionPayload`; ужесточение guard'а промпта (hint bounded/hedged, никогда не выдумывать damper/pressure — такого Gold-поля нет). Не заявлять «ноль grounding» — `understeer_trend` уже есть; ценность — добавленная текстура.

**7.6 (n36, medium — не high, переформулировано).** Корректировка premise: phase-локализация ДОМИНАНТНОЙ вины **уже** есть — `reason` (early_brake/low_min_speed/late_throttle), три point-diff'а и phase-tagged-действия с hint_en уже говорят, в какой фазе выбранная вина. Реальный gap — только whole-corner balance-скоры (understeer/oversteer/wheelspin/steering_jitter), не атрибутируемые entry↔exit. Скоупить на phase-split **этих**: entry{understeer_score}, exit{oversteer_score, wheelspin_score}. **Главный потребитель — НЕ corner-LLM** (8 слов/2s/no-retry не унесёт, free-text эмбеллизирует, tip 46), а (a) detection-side clauses в `ClauseEvaluator`/`actionRegistry` — точнее SELECTION, P3-архитектура цела, без изменения LLM; и (b) sector/lap/debrief (25/200-словные бюджеты). Если phase-скаляр всё же даётся corner-LLM — обязательно с phrase-grounding-правилом. Зависимость от точности чисел: ground-truth-ревью нашло неверные скаляры — phase-split тех же kernels множит подозрительные числа, сначала валидировать корректность kernel'ов.

---

## 8. Приоритизированный бэклог LLM-доработок

**Быстрые победы (промпт/схема/конфиг) — отдельно от архитектурных.**

### P0 — быстрые победы, максимальная отдача

| # | Пункт | Тип | Effort | Куда смотреть |
|---|---|---|---|---|
| n1 | Один совет на фразу (по каденсам) | prompt | quick-win | `coach.system.v1.ru.txt:5`, `coach.fewshot.v1.ru.json` |
| n2 | Запрет чисел в real-time, числа только в debrief | prompt | quick-win | `coach.system.v1.ru.txt:7-8`, few-shot `:31/:44` |
| n34+n3 | `corner_name_ru`=GetShort в Gold + жёсткое RU-правило имён + template-путь | gold+prompt | quick-win | `GoldCornerEvent.cs`, `GoldArtifactBuilder.cs:30`, `coach.system.v1.ru.txt:6`, `CoachService.cs:305` |
| n21 | `temperature=0`, `top_p=1` на каденс-routes | config | quick-win | `RouteOptions.cs`, `OpenRouterProvider.cs:85-99`, `appsettings.json` |
| n15/n16 | Свап corner → `gemini-3.1-flash-lite` (+ явный thinking-off) | routing | quick-win | `appsettings.json:60` + rate-card `:70-72` |

### P1 — высокая ценность, quick-win/medium

| # | Пункт | Тип | Effort | Куда смотреть |
|---|---|---|---|---|
| n4/n30 | RU-hint в меню + привязка фразы к `action_id` + anti-example | prompt+data | quick-win | `PromptBuilder.cs:130`, `actionRegistry.json`, `coach.system.v1.ru.txt` |
| n29/n8 | Abstain (`"none"`-sentinel) на слабом catch-all | schema+code | medium | `OutputSchema.cs:25-46`, `CoachService.cs:247-262`, `CoachOptions.cs` |
| n28 | Evidence-weighted арбитраж в промпте corner | prompt | quick-win | `coach.system.v1.ru.txt` rule 1 |
| n7 | Cold-start (no-reference) ветка | prompt | quick-win | `coach.system.v1.ru.txt`, few-shot `:46-58` |
| n35 | Активировать `reason` + `reason_ru` глосс (5 меток) | gold+prompt | quick-win | `GoldArtifactBuilder.cs:45`, `coach.system*.txt` |
| n17/n23 | Расширить fallback-триггер роутера + circuit-tuning | code+config | medium | `LlmRouter.cs:52-59`, `RouteOptions.cs`, `appsettings.json:89-93` |
| n32 (QW) | Debrief plausibility-guard на существующих полях | prompt | quick-win | `coach.system.debrief.v1.ru.txt` |
| n31 (QW) | «Почему»-заземление на debrief/lap (категория+мс) | prompt | quick-win/medium | `coach.system.debrief.v1.ru.txt`, lap-ветка |

### P2 — наблюдаемость, робастность, latent-дыры

| # | Пункт | Тип | Effort | Куда смотреть |
|---|---|---|---|---|
| n22 | Персистить `reasoning_tokens` + подтвердить thinking-off | code | quick-win | `SqliteCostMeter.cs`, `OpenRouterProvider.cs:237-241` |
| n6/n14 | Retry-промпт эхом причины отказа | code+prompt | quick-win | `CoachService.cs:404/417`, `coach.retry.v1.ru.txt` |
| n13 | Per-family robustness (Gemini `maxItems`-guard, debrief-validation, refusal-log) | code | quick-win | `GeminiSchemaTranslator.cs`, `TipValidator.cs:98-117` |
| n26 | Ненулевой `MonthlyBudgetUsd` перед Live | config | quick-win | `appsettings.json:51` |
| n27 | `MaxOutputTokens` не ужимать (keep-with-rationale) | config | quick-win | `appsettings.json` |
| n5 | Few-shot как multi-turn + per-cadence негативы | code+data | medium | `PromptBuilder.cs:74-113`, `LlmRequest.cs` |
| n33/n12 | Enum-`confidence` + логирование (деплой с abstain) | schema+code | quick-win | `OutputSchema.cs:38-42`, `CoachService.cs:265-272` |
| n11 | Number-grounding guard (default number-free на corner) | code | medium | `TipValidator.cs:45-61` |
| n19 | A/B RU-качества one-liner (2.5 vs 3.1 vs DeepSeek vs Qwen) | eval | medium | shadow-harness, `coach_tips`/`llm_usage` |
| n18 | Haiku 4.5 как debrief-fallback (после расширения триггера) | config | quick-win | `appsettings.json:63` |

### P2/P3 — архитектурные (contract/compute-change, отложить/по мере надобности)

| # | Пункт | Тип | Effort | Куда смотреть |
|---|---|---|---|---|
| n37 | prior-lap/session `trend` в corner-Gold | compute+gold | medium | `CoachService` accumulator, `GoldSessionContext` |
| n38 | sector→corner-membership в debrief-Gold | compute+gold | medium | `ComputeSession.cs:231`, `SessionEvent`, `GoldSessionPayload.cs` |
| n39 | session car-balance rollup → grounded `setup_hint` | compute+gold | medium | `ComputeSession.cs:202-204`, `GoldSessionPayload.cs:16` |
| n32 (arch) | diagnostic-скаляры per-loss для строгого plausibility-теста | compute+contract | large | `CornerEventBuilder.cs:13`, proto `AggregatedLoss`, protobuf regen |
| n31 (arch) | канал+число в `AggregatedLoss` для «почему» | compute+contract | large | `GoldAggregatedLoss`, детекция |
| n36 | phase-split баланс-скоров (лучше detection-side) | compute+contract | large | `telemetry.proto:87-106`, `CornerEventBuilder.cs`, `ClauseEvaluator` |
| n25 | Streaming debrief (P4-TTS-подготовка, не P3-выигрыш) | code | large | `OpenRouterProvider.cs:73-74`, `RouteOptions.Stream` |
| n24 | Prompt-caching статического префикса | config+code | medium | `PromptBuilder.cs`, `BuildBody`, model-choice |
| n9/n10 | Word-cap исключает имя (поглощён n34) | schema | medium | `PhraseWordCount.cs`, `TipValidator.cs:57` |

**Ключевой принцип бэклога:** P0/P1-quick-win'ы (промпт/схема/конфиг) закрывают три главные боли владельца (TTS-краткость, зоопарк имён, неуместный совет) **без** зависимости от исправления детекции. Архитектурные пункты (compute/contract-change) дают LLM реальную аналитическую ценность, но упираются в корректность чисел детекции (NO-GO ground-truth-ревью) и требуют алгоритмического guard'а параллельно — их нельзя «включить промптом».