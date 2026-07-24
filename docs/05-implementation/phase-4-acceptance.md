# SimCoach — Приёмочные критерии Фазы 4 (Voice/TTS)

> **Статус документа:** UP-FRONT приёмочная спецификация (GO/NO-GO планка), а не пост-фактум ревью как `docs/05-implementation/phase-3-acceptance.md`. Фаза 4 считается сданной только когда пройдены все блокирующие критерии ниже. Каждый критерий привязан к реальному FR / типу / файлу.
>
> **Ссылки на срезы:** Slice 1 (TTS-бэкенды + Silero V0 + Yandex + RuPhonetics), Slice 2 (`SimCoach.Audio` — `PriorityAudioQueue`, fade/preempt, WASAPI/платформенный сплит), Slice 3 (`VoiceTipSink`, mute, settings read-path), Slice 4 (host wiring, stop-order, `VoiceComposition`), Slice 5 (озвученный дебриф — M40 `StreamAsync` + `debrief_prose`), Slice 6 (TTS-eval гейт).

---

## 1. Резюме — что значит «done» для Фазы 4

Фаза 4 сдана, когда Coach-движок (Фаза 3) умеет **говорить**. Озвучка — **общая способность, не только дебриф** (owner D1): (i) короткие структурные реплики — `VoiceTipSink : ICoachTipSink` проговаривает **каждый** `CoachTip` синтезом готового `phrase_ru` напрямую, **без стрима**; (ii) стримовая длинная проза — **route-agnostic** `IStreamedProseVoicer` (интерфейс в Coach, `StreamedProseVoicer` в Voice) потребляет `ILlmClient.StreamAsync` → `SentenceChunker` → `ITtsBackend` → `PriorityAudioQueue`; дебриф — **первый потребитель**, но seam **не** привязан к дебрифу. Всё в рамках бюджетов FR-040..046, FR-033, FR-060..062. Конкретно: (1) спайк Silero **V0** разрешён (Silero primary, либо задокументированный fallback на Yandex primary); (2) `VoiceTipSink` неблокирующе (`EmitTipAsync` возвращает завершённый `Task` до синтеза) кладёт реплики в `PriorityAudioQueue` с приоритетом/вытеснением/fade/stale-drop/mute согласно FR-042/043/044/045 (**mute — только STATE; глобальный хоткей Ctrl+Alt+M → Phase 5**, у headless-хоста нет HWND/message-pump); (3) две реализации `ITtsBackend` (`SileroOnnxSynthesizer` in-proc и `YandexSpeechKitClient` за флагом) выбираются live через `IOptionsMonitor<VoiceOptions>`; (4) дебриф стримится через `ILlmClient.StreamAsync` на **втором billable plain-text `debrief_prose`-маршруте** (реальный **family-aware** OpenRouter SSE-декод; короткий `top_priority`-тип по-прежнему эмитится+персистится), режется `SentenceChunker`'ом по предложениям, проходит `RuPhonetics` и звучит на низшем приоритете; (5) вся non-Windows-сборка (`net9.0`, не `net9.0-windows`) под `TreatWarningsAsErrors=true` зелёная и полностью offline (fake-устройство + fake-провайдер, без сети и железа); (6) блокирующий **TTS-eval** гейт (`tests/SimCoach.TtsEval/`, зеркало `tests/SimCoach.RuEval/`) зелёный: hermetic DSP/logic-леги + **3 реальных лега (D4)** — real-hardware Windows-only FR-040 perf-smoke, golden-audio stress-регрессия, скриптованный **блокирующий** manual-протокол. Аудио **дренится на disposal контейнера** (`IAudioDevice : IAsyncDisposable`, **после** каждого `StopAsync` включая дебриф-drain — **не** `ApplicationStopping`). Голос никогда не стопит 333 Гц ingest; наружу уходит только короткий русский текст фразы (NFR-004); load-bearing stop-order не нарушен.

---

## 2. Приёмочные критерии по FR

Легенда типа: **A** = автоматический тест/CI-гейт; **M** = ручная проверка (in-game / sign-off).

### 2.1 Реальное время: голос, приоритет, вытеснение (FR-040..043)

| FR | Проверяемый критерий | Как проверить | А/Р |
|---|---|---|---|
| **FR-040** first-audio ≤ 200 мс после парса LLM-ответа | Валидируется **реальным-железом Windows-only perf-тестом (D4)**: `SileroOnnxSynthesizer` на CPU EP + timestamp NAudio buffer-fill над N≈100 репликами, assert **p100 ≤ 200 мс**, в **perf-smoke-тире ВНЕ детерминированного macOS-лейна**. Fake-clock-«латентность» на `FakeTimeProvider` **переименована в queue-plumbing-ассерт** (логическое время, не стоимость синтеза) и **никогда** не помечается FR-040. | perf-smoke `SileroOnnxSynthesizer` p100 ≤200мс (Windows-only); queue-plumbing hermetic-ассерт отдельно; **advisory** через `EnforceFirstAudioBudget=false` до фиксации реального распределения V0, затем flips blocking | A (blocking* после V0; real-hw) |
| **FR-041** 20–40 мс 16-бит PCM кадры | Каждый `ReadOnlyMemory<byte>` из `ITtsBackend.StreamAsync` — 16-бит PCM длительностью 20–40 мс на объявленном `SampleRateHz` (`PcmFrameMinMs=20`/`PcmFrameMaxMs=40`); пустой текст → пустой стрим без throw. | `SileroOnnxSynthesizer`/`FakeTtsBackend` chunk-shape unit; TtsEval PCM-framing | A |
| **FR-042** глубина 1 in-flight + 1 queued; вытеснение с 10–20 мс линейным fade | `PriorityAudioQueue`: 3-я реплика вытесняет queued-слот если старше по `(IsCornerCritical, CoachPriority)` иначе дропается (`DropReason.Superseded`); при strict-outrank in-flight — линейный fade `round(FadeOut.TotalSeconds*rate)` сэмплов (default 15 мс, диапазон 10–20), монотонно невозрастающий, достигает gain 0 **до** первого ненулевого сэмпла новичка (без щелчка); ни один смикшированный сэмпл не > \|1.0\|. Fade — per-channel над interleaved stereo. | `SimCoach.Audio.Tests` depth + `LinearFadeEnvelope` sample-by-sample; TtsEval V-E3 `FadeAnalyzer` (монотонность, длина окна кадрами, seam \|Δ\| < `ClickThreshold`) | A |
| **FR-043** stale-drop ≥ 1 с corner-critical / ≥ 2 с general | `age = TimeProvider.GetUtcNow() - GeneratedAtUtc`; порог `StaleCornerCritical=1s` если `IsCornerCritical` (`Cadence==CoachCadence.Corner`) иначе `StaleGeneral=2s`. Проверяется **и на enqueue, и на promotion** (stale-on-cancel): реплика, состарившаяся в pending-слоте, дропается при промоушене (`DropReason.Stale`), а не проговаривается поздно. Свежая реплика в том же слоте не дропается. | `SimCoach.Audio.Tests` stale-drop на `FakeTimeProvider` (без sleep); TtsEval V-E6 | A |

`*` FR-040 advisory до тех пор, пока V0 не зафиксирует реальное распределение синтез-латентности; затем `EnforceFirstAudioBudget=true` (по образцу `RuEvalOptions.EnforceGoodFixtureBar` advisory→blocking после калибровки 2026-07-22).

### 2.2 Mute, громкость, cloud-бэкенд (FR-044..046)

| FR | Проверяемый критерий | Как проверить | А/Р |
|---|---|---|---|
| **FR-044** mute STATE (глобальный хоткей Ctrl+Alt+M → **Phase 5**), персист | В P4 — **только STATE**: `IMuteState.SetMuted(true)` (через `voice.mute`-toggle) даёт тишину в `Read`/dequeue, **но стрим-оффсет продолжает продвигаться** (unmute посреди реплики резюмит на правильной позиции) и enqueue продолжает приниматься (глубина FR-042 цела); mute перекрывает in-flight fade. `voice.mute_on_startup=true` сидит `IMuteState.IsMuted==true` до первой реплики. Выбор персистится через `ISettingsStore`. **Глобальный WM_HOTKEY-биндинг** переносится в P5 (headless-хосту негде принимать WM_HOTKEY — overlay-окно даёт HWND + message-pump). | headless unit `IMuteState` toggle + `mute_on_startup` seed; settings round-trip `voice.mute`/`voice.mute_on_startup`; **никакого hotkey-hosted-service в P4** | A |
| **FR-045** громкость независима от игры | `SetVolume(0..100)` масштабирует каждый выходной сэмпл внутри `Read` мэпленным gain, независимо от любого игрового состояния; `SetVolume(0)` — отдельный код-путь от mute (не путать). На Windows — device-level аттенюация на захваченном PCM. | attenuation unit на `FakeAudioDevice`; `WasapiAudioDevice` integration (Windows) | A + M (Windows) |
| **FR-046** опциональный Yandex за флагом | `voice.engine=Yandex` → `SelectingTtsBackend` делегирует в `YandexSpeechKitClient`; полностью offline-тестируется через `FakeSpeechKitChannel` (заскриптованный LINEAR16, **без сети, без ключа, без `Grpc.Net.Client`-дозвона**); флаг-офф → не активен по умолчанию (default Silero). | `SimCoach.Voice.Tests` с `FakeSpeechKitChannel`; env-gated live-контракт (non-blocking) | A (offline blocking; live env-gated non-blocking) |

### 2.3 Приватность и границы cloud-egress (NFR-004, FR-046)

| Критерий | Проверяемый критерий | Как проверить | А/Р |
|---|---|---|---|
| **Только короткий RU-текст покидает машину** | При `Engine=Yandex` через seam проходит **ровно `PhraseRu`** (или собранная `SpokenTextMapper`-ом реплика) — никакой телеметрии, никакого Gold-артефакта. `FakeSpeechKitChannel` записывает точный текст, тест сверяет его с ожидаемым. Для дебриф-пути (Slice 5) — единственный дополнительный текст, пересекающий seam, это дебриф-проза (≤200 слов, FR-033). `VoiceStartupValidator` логирует privacy-нотис при `Engine=Yandex`. | `SimCoach.Voice.Tests` privacy-of-text assert; TtsEval hermetic fake-`CallInvoker` wire-shape assert; e2e `NetworkCallCount==0` в offline-лейне | A |

### 2.4 Длина фраз как проговариваемых (FR-033)

| FR | Проверяемый критерий | Как проверить | А/Р |
|---|---|---|---|
| **FR-033** phrase-length caps соблюдены как проговариваемые | `SpokenTextMapper.Map(tip)` = `tip.PhraseRu` verbatim + опциональный префикс `CornerNameSpokenRu` (со strip хвостового `(N)`), **без ре-рендера чисел** — реплики уже уложены в cap коучем (`Coach:InCornerMaxWords=8`); `VoiceOptions.MaxPhraseChars=400` — guardrail против runaway-фразы, ` > 0` в `EnsureValid()`. Дебриф-проза стримится, но `MaxSentences`/`RouteOptions.MaxOutputTokens` ограничивают её ~200 словами (FR-062). | `SpokenTextMapper` golden-фикстуры; `VoiceOptions.EnsureValid` unit; `DebriefProseOptions.EnsureValid` (`MaxSentences>0`) | A |

### 2.5 Озвученный дебриф — граница scope P4 vs P6 (FR-060/061/062)

| FR | Проверяемый критерий | Как проверить | А/Р |
|---|---|---|---|
| **FR-060/062 — AUDIO дебрифа приземляется в P4** | `StreamedProseVoicer` (в `SimCoach.Voice`, реализует route-agnostic `IStreamedProseVoicer`; дебриф — первый потребитель) вызывает `ILlmClient.StreamAsync` на **втором billable plain-text** маршруте `debrief_prose` (Sonnet 4.6, Reasoning=Low, `Stream=true`, ~1.8¢/сессия, negligible под NFR-007; короткий `top_priority`-тип по-прежнему эмитится+персистится), режет `SentenceChunker`'ом, прогоняет `RuPhonetics` пер-предложение, синтезирует `ITtsBackend.StreamAsync` и кладёт в очередь на **низший band** (эквивалент `CoachPriority(Exit,int.MaxValue)` → сортируется последним; `AudioPriority.Debrief`-enum **удалён** как избыточный). **Не вытесняет** реплики реального времени. Structured `debrief`-маршрут (JSON `top_losses`/`top_priority`/`setup_hint`) **байт-в-байт неизменён** от P3. | `SimCoach.Voice.Tests` voicer chain; **синтетический** queue-unit «corner вытесняет debrief» (F12: на shutdown `IngestService` уже стоплен → не real-world-инвариант); byte-identity structured-output regression | A |
| **FR-062 — WINDOW дебрифа остаётся P6** | P4 **не трогает** `debrief_prose`/`checklist_json`/`per_sector_deltas_json`/`balance_verdict` колонки (P6-reserved). P4 **пишет** только `audio_artifact_ref` (WAV relpath) + WAV на диск под data-root; P6 **читает** их. **F9-фикс расы:** у `(session_id, cadence='Session')` нет уникального констрейнта → `UPDATE … WHERE session_id=?` **гонится** с async-INSERT на replay (переиспользование session_id → multi-row/zero-row UPDATE). Фикс: **UPDATE по row-id** (`InsertAsync` возвращает id) **ИЛИ** новая миграция с UNIQUE-индексом на `(session_id) WHERE cadence='Session'`; voicer **AWAIT'ит** INSERT дебриф-строки **до** UPDATE; template-fallback (нет LLM-стрима → синтез `PhraseRu` напрямую, всё равно пишет WAV + UPDATE); детерминированный WAV-путь под data-root. `mvp-deferrals.md:41-46` + XML-doc `ILlmClient.StreamAsync`/`LlmDelta` + три `NotSupportedException`-сообщения обновлены (P6-carry-фразировка убрана из mvp-deferrals). | `CoachTipRepository.UpdateAudioArtifactRefAsync` тест (UPDATE **по id** после awaited-INSERT; race-guard); template-fallback тест; docs-edit присутствует в PR; e2e проверяет `audio_artifact_ref` записан после INSERT | A |
| **FR-061 — StreamAsync consumption path** | `LlmRouter.StreamAsync`, `OpenRouterProvider.StreamAsync` (реальный **family-aware** SSE-декод: `data:`-строки → аккумулирует **и** `delta.content` **и** `delta.tool_calls[].function.arguments` — зеркало `ExtractContent` ~`OpenRouterProvider.cs:247`, так что будущий forced-tool streamed-маршрут работает без переписывания; дроп empty-content thinking-дельт, терминальный `usage`, стоп на `[DONE]`), `FakeProvider.StreamAsync` (детерминированный RU-прозы-эхо) — все три больше **не** бросают `NotSupportedException`. Терминальный usage несёт явный `LlmStreamResult { Deltas; TerminalUsage }`; `CircuitBreakerProvider`/`CostMeterProvider` `StreamAsync` — **re-yielding async-итераторы (НЕ pass-through)**, метерят в `finally`. Fallback-once на **open** стрима в `debrief_prose_fallback` (буферизованный). | PR unit через mocked `HttpMessageHandler` (multi-chunk RU, keep-alive comment, thinking-only empty deltas, content+tool_calls-ветки, terminal usage, `[DONE]`, mid-stream `429`); **mid-stream cancel → ровно ОДНА `llm_usage`-строка `status=cancelled`; fallback-once → ровно ОДНА строка** | A |
| **Дебриф double-spend осознанный, без drift с P6 (D1)** | `debrief_prose`-озвучка — это **второй billable-вызов** (~1.8¢/сессия, negligible под NFR-007) **в дополнение** к уже эмитированному+персистированному `top_priority`-типу; оба grounded в тот же Gold. `RefreshBudgetAsync` покрывает оба. P6-окно и аудио читают одну persisted-structured-строку + WAV → нет drift. | `CoachService.ProcessDebriefAsync` тест (headline emit → voicer UPDATE по id после awaited-INSERT → бюджет по 2 вызовам); shutdown-drain e2e | A |
| **Дебриф переживает shutdown (D3)** | Дебриф эмитится **во время** drain `CoachService` на `CancellationToken.None`; `StreamedProseVoicer` крутит content-стрим на `CancellationToken.None`, ограниченный `RouteOptions.Timeout` + `DebriefProseOptions.ShutdownPlaybackCeiling` (≤ **сконфигурированный** host `ShutdownTimeout` — framework-default 30 с, выставляется явно в PR-M). Если playback выходит за ceiling — voicer всё равно коммитит полный WAV для P6-реплея, деградируя gracefully без hang'а teardown. Аудио дренится **на disposal контейнера** (`IAudioDevice : IAsyncDisposable`), **после** каждого `StopAsync` — **не** `ApplicationStopping` (та файрит **до** `StopAsync` → срезала бы дебриф). | e2e **реальный host-shutdown** (`StopApplication` → полный `StopAsync`-sweep → disposal, не голый `ApplicationStopping`-токен; Session-реплика доигрывает через `NullAudioDevice`/`FakeAudioDevice`); stop-order assert `audio drains after Coach` | A |

---

## 3. Гейт качества (TTS-eval) — точная блокирующая спецификация

Гейт живёт в **`tests/SimCoach.TtsEval/`**, зеркалит `tests/SimCoach.RuEval/`: env-gated network `[Fact]` (`TtsEvalGateTests`, ранний return при `!EnvGate.IsEnabled()`) + всегда-включённый hermetic-suite (`TtsEvalHermeticTests`) над чистыми анализаторами и фикстурами, загружаемыми с манифеста ассембли (`PhoneticFixtureLoader`, `EmbeddedResource Include="Fixtures\*.json"`). Все пороги — в `TtsEvalOptions` с `EnsureValid()` (test-scoped record, **не** `IOptions`-bound; проверяется `[Fact]`-ом как `RuEvalOptions`). `dotnet test tests/SimCoach.TtsEval` зелёный на macOS/CI по умолчанию, без ключа, без железа, без сети.

### 3.1 Пороги (`TtsEvalOptions`, без магических чисел)

| Поле | Значение | Источник |
|---|---|---|
| `FirstAudioBudgetMs` | `200` | FR-040 |
| `FadeMinMs` / `FadeMaxMs` | `10` / `20` | FR-042 |
| `CancelLatencyMs` | `50` | `testing-strategy.md §5` (cancel убирает аудио ≤50 мс) |
| `StaleCornerCriticalMs` / `StaleGeneralMs` | `1000` / `2000` | FR-043 |
| `PcmFrameMinMs` / `PcmFrameMaxMs` | `20` / `40` | FR-041 |
| `ClickThreshold` | `0.02` (макс нормализованный \|Δsample\| на A→B-шве) | FR-042 «без щелчков» |
| `RmsFloor` / `RmsCeil` | `0.01` / `0.99` | `testing-strategy.md §5` (не тишина / не клиппинг) |
| `SampleRateHz` / `Channels` | `48000` / `2` | architecture.md §3.7 (WASAPI shared 48 кГц stereo) |
| `EnforceFirstAudioBudget` | `false` (advisory до V0) | staging по образцу `EnforceGoodFixtureBar` |

`EnsureValid()` отвергает: `FirstAudioBudgetMs<=0`; `FadeMinMs<=0 || FadeMaxMs<FadeMinMs`; `CancelLatencyMs<=0`; `StaleGeneralMs<StaleCornerCriticalMs`; `ClickThreshold∉(0,1]`; `PcmFrameMinMs<=0 || PcmFrameMaxMs<PcmFrameMinMs`; `RmsFloor<0 || RmsCeil<=RmsFloor || RmsCeil>1`; `SampleRateHz<=0 || Channels∉{1,2}`. Асёртится всегда-включённым `[Fact]` (как `RuEvalHermeticTests.EnsureValid_rejects_out_of_range_config`).

### 3.2 Легенды гейта — что каждая проверяет и когда блокирует

**Latency — ДВА раздельных лега (D4/F6).** Fake-clock-«латентность» на `FakeTimeProvider` — это **queue-plumbing-ассерт логического времени (DSP/logic-гейт: монотонность огибающей, drop-политика, priority-порядок), НЕ real-time-audio-гейт**; **никогда** не помечается FR-040. Реальный FR-040-SLA (device-buffer + GC + jitter) проверяется **только** real-hardware Windows-only perf-smoke-тиром: `SileroOnnxSynthesizer` на CPU EP + timestamp NAudio buffer-fill над N≈100 репликами, **p100 ≤ `FirstAudioBudgetMs=200`**, вне детерминированного macOS-лейна. Fake-hermetic-гейт (`FakeAudioDevice`) — DSP/LOGIC-гейт, не real-time-audio-гейт.

**Fade/preempt-непрерывность (V-E3) — hard с первого дня.** Над захваченным `short[]` fake-устройства (**stereo/interleaved**, `FadeAnalyzer` де-интерливит по `Channels`): (a) длина fade кадрами == `FadeMs × SR/1000` (±1 кадр); (b) огибающая монотонно невозрастающая пер-канал; (c) достигает ≈0 до старта B; (d) макс \|Δsample\| на шве < `ClickThreshold`. `FadeAnalyzer` — чистый, с self-тестом своей математики.

**Cancel-латентность ≤ 50 мс (V-E5) — hard, именованный owner-deliverable.** Отмена реплики посреди синтеза на fake-часах → аудио останавливается в ≤ `CancelLatencyMs=50` мс. (Восстановлено из `testing-strategy.md §5` / `implementation-plan.md:84` — в черновике отсутствовало.)

**Stale-drop (V-E6) — hard.** Реплики старше `StaleCornerCriticalMs`/`StaleGeneralMs` по cadence дропаются, не проговариваются; 3-й enqueue вытесняет queued-слот. Детерминированно на fake-часах.

**RMS-энергия (V-E7) — hard.** Захваченный PCM RMS в `[RmsFloor, RmsCeil]` — ловит all-silence-баг и клиппинг. (Восстановлено из `testing-strategy.md §5`.)

**RU-произношение (V-E2) — hard, с known-bad-анкерами (дисциплина 3 анкеров RuEval).** Фикстуры живут в `tests/SimCoach.TtsEval/Fixtures/*.json` (embedded). Т.к. знаки ударения **пре-бейкнуты в шаблоны действий** (`prompt-style-guide.md:54`), а `PhraseRu`/`CornerNameSpokenRu` приходят в sink уже гуманизированными (commit 712d557, `CarLengthGloss.cs`, `CornerNameForms.cs`), `RuPhonetics` — **нормализатор/gap-filler**, не первичный автор ударений. Гейт:
- **Сохранение:** `RuPhonetics(PhraseRu)` **сохраняет** пре-бейкнутые `+`-марки (`торм+оз`, `тр+ейл-бр+ейкинг`) — регрессия, **срезающая** существующий `+`, отвергается (known-bad).
- **Вставка:** голый иностранный термин без марки (`апекс`) получает `ап+екс` — незастрессенный термин фейлит (known-bad).
- **Числа как корпуса, не метры:** дистанции читаются как car-length-множественные с верным RU-согласованием (1→корпус, 2–4→корпуса, 5–20/11–14→корпусов); **сырой** unit-leak (`4 метра` / `+4м` / `мс` / `км/ч`, проговоренный числом) **отвергается** (known-bad — ровно тот класс утечки, что поймала калибровка RuEval, `RuEvalOptions.cs`: «leaking raw «мс» into top_priority»). Форма `"четыре метра"` — **запрещённая**, не эталонная.
- Сравнение нормализует обе стороны (trim, схлопывание пробелов) и асёртит позиции ударений, не сырые байты; манифест содержит ≥1 known-bad-анкер **каждого** класса (срезанный-`+`, unit-leak), иначе шкала не заякорена.

**Golden-audio stress-регрессия (D4) — blocking, per-release.** Reference-аудио (WAV **или** phoneme/duration-stress-векторы), запечённое из V0-валидированной модели; per-release assert что знаки ударения **реально проговариваются** и нет gross-mispronunciation-регрессии. (Текстовые RuPhonetics-фикстуры не ловят TTS, который **игнорирует** марку — только golden-аудио ловит.)

**Скриптованный manual-протокол (A-Manual, D4) — BLOCKING, не advisory.** Именованный, фиксированный скрипт ~15–20 реплик: **каждый** `racingLexicon`-термин + car-length-множественные 1/2/5 + длиннейшие имена поворотов + реальная **3-corner preempt-последовательность**, проигрываются на реальном Windows-аудио. Явный pass-чеклист (ударение верно / согласование чисел / нет слышимого щелчка на preempt / громкость независима / нет cross-corner-оверлапа) + **owner sign-off**. Человеческий аналог Phase-3 RU-eval-судьи — как и он, **блокирующий**.

**Yandex privacy/wire-shape — hermetic, blocking.** `YandexSpeechKitClient` против ручного in-memory fake gRPC `CallInvoker` (без сети, без ключа): проверяет форму request-body (**только короткий RU-текст на проводе**, NFR-004) и PCM-декод. **Отдельная легенда** от env-gated live-контракта (тот non-blocking, только реальный endpoint).

### 3.3 Где лежат фикстуры и что закоммичено

- `tests/SimCoach.TtsEval/Fixtures/*.json` — `PhoneticFixture{ Id, InputPhraseRu, ExpectedNormalized, KnownBad }`, good + known-bad (срезанный-`+`, unit-leak), off-manifest.
- `tests/SimCoach.TtsEval/FadeAnalyzer.cs`, `RmsAnalyzer.cs` — чистые анализаторы с self-тестами (queue-plumbing-latency-ассерт живёт в hermetic-suite, не как FR-040).
- Общие фейки (`FakeAudioDevice` stereo/sample-recording, `FakeTtsBackend` с delay+cancel-honouring, fake gRPC `CallInvoker`) — в `tests/SimCoach.TestKit/` **или** обособленном `tests/SimCoach.Audio.Testing` (решается в PR-time; NAudio 2.2.1 = `netstandard2.0` без `[SupportedOSPlatform]`, так что транзитив компилится cross-OS — риск не CA1416, а конструирование Windows-типа off-Windows → `PlatformNotSupportedException`).
- Инструменты: `Moq` только для `HttpMessageHandler`/`ITtsBackend`; ручные фейки везде ещё; `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, pinned `Directory.Packages.props:33`) вместо любого sleep.

---

## 4. Silero V0 — спайк-гейт ПЕРЕД остальной фазой

**V0 — work-item ZERO, гейтит всю фазу.** Никакой production-`SileroOnnxSynthesizer` не мержится, пока V0 не разрешён. Throwaway-консоль `tools/SimCoach.SileroSpike` грузит кандидатный `v5_ru.onnx` через `Microsoft.ML.OnnxRuntime` (CPU EP; проверено — `libonnxruntime.dylib` присутствует в `bin/.../osx-arm64/`, спайк runnable на macOS, пишет WAV, не живое аудио), синтезирует 3 RU-фразы (plain; со знаком `торм+оз`; с иностранным `ап+екс`), печатает first-frame-латентность, RTF, размер модели, гистограмму чанков, и выдаёт **бинарный вердикт PASS/FAIL**.

### 4.1 Критерии PASS (все должны держаться)

| # | Критерий | Бюджет | Замер |
|---|---|---|---|
| C1 | `v5_ru` ONNX-экспорт существует и грузится в CPU EP с документированными именами тензоров | ADR-0005 «CPU-only» | `InferenceSession` конструируется; inputs перечисляемы |
| C2 | Выход конвертируем в 16-бит PCM, режется на 20–40 мс кадры на **реальной измеренной** native-частоте (не предполагаемой) | FR-041 | float32→int16, frame-sliceable |
| C3 | Первый кадр ≤ 200 мс после готовности текста на ≤4-thread-конфиге (incremental decode ИЛИ whole-utterance для ≤8-слов достаточно быстро) | FR-040 (**hard**, не p50); ADR-0005 «≤150 мс typical» | измеренная first-frame-латентность |
| C4 | Знаки ударения (`торм+оз`) меняют акустический выход (уважаются) | ADR-0005:25 | A/B waveform-diff vs unstressed |
| C5a | Payload модели ≤ ~50 МБ | ADR-0005:27 | размер файла модели |
| C5b | Проецируемый installer ≤ 200 МБ с моделью — **оценка, не замер спайком** | NFR-005 | текущий размер выхода + размер модели, записан как projection с нотисом |
| C6 | RTF в полосе ADR-0005 **0.06–0.3** на ≤4 thread'ах | ADR-0005:19 | wall-clock/audio-duration; PASS если ≤0.3, WARN 0.3–0.5, FAIL >0.5 (потолок голодания 333 Гц, записан в ADR-0023) |

### 4.2 Ветвление (записано в ADR-0023)

- **PASS** → V2 строит реальный `SileroOnnxSynthesizer`; Yandex остаётся flag-off-альтернативой (V3); default `Voice:Runtime:Engine=Silero`. Разблокирует: калибровку `EnforceFirstAudioBudget=true` реальным-железом perf-smoke-легом (**A-Perf-HW**, не fake-clock).
- **FAIL** (нет годного экспорта, ИЛИ C3/C4 не выполнены, ИЛИ C6 > 0.5) → **Yandex SpeechKit становится primary**: default `Voice:Runtime:Engine=Yandex`, Silero-seam остаётся stub'ом, бросающим внятный «no validated model», V2 ре-скоупится. Добавляется note «Superseded in part» к ADR-0005. Разблокирует: перекалибровку `FirstAudioBudgetMs` против gRPC-mock-модели латентности перед flip'ом latency-легенды в blocking.
- **PyTorch python-sidecar** (Risk Register) — оценить, но **отклонить для MVP** (нарушает in-proc ADR-0005, single-binary ≤200 МБ NFR-005, тянет Python-рантайм). Задокументировано last-resort только если Silero фейлит **и** Yandex неприемлем (нет Yandex Cloud квоты).

**Регистрация ADR:** независимо от исхода V0 приземляется **ADR-0023** (выбор TTS-бэкенда + Silero-гейт: V0 pass/fail-контракт, monitor-aware-селекция, Silero-only phonetics, fallback-ветка, реконсиляция ключа `voice.engine`↔`Voice:Runtime:Engine`).

---

## 5. Оффлайн / платформенные критерии

| # | Критерий | Как проверить | А/Р |
|---|---|---|---|
| **O-1** macOS `net9.0` build зелёный + Windows-типы не конструируются off-Windows | Все проекты — plain `net9.0` (`Directory.Build.props:4`), не `net9.0-windows`. **Рационал = RUNTIME-SAFETY, не CA1416 (F1):** NAudio 2.2.1 = `netstandard2.0` без `[SupportedOSPlatform]`-атрибутов, поэтому `WasapiOut`/`MediaFoundationResampler` **не** трогают CA1416 — компилятся cross-OS и бросают `PlatformNotSupportedException` в рантайме на macOS. Гейт = **DI-construction-тест**: `WasapiAudioDevice`/`MediaFoundationPcmResampler` **никогда не регистрируются и не конструируются** off-Windows (изолированный `[SupportedOSPlatform("windows")]`-метод на composition-edge, зеркало `AddAccSource`; OS-бранч `OperatingSystem.IsWindows()` для консистентности с `TelemetryComposition.cs:166`, **не** потому что `RuntimeInformation.IsOSPlatform` не распознаётся — он **распознаётся** на repo-SDK). | macOS CI-лейн: `dotnet build SimCoach.sln` зелёный; **DI-construction-тест**: Windows audio/device-типы **не** зарегистрированы/сконструированы off-Windows | A |
| **O-2** Replay-сессия e2e с fake-устройством, без сети | Зеркало `HostCompositionTests.NewBuilder` (форсит `Telemetry:Source=replay`, live-ACC Windows-only): `StartAsync` → replay drains → `StopApplication` → **полный `StopAsync`-sweep → disposal** (реальный shutdown, не голый `ApplicationStopping`). Sink-цепочка на фейках: `FakeAudioDevice`+`NullAudioDevice`+`FakeProvider`+`FakeTtsBackend` (без ONNX-native, без Yandex-сети). **F10:** App дефолтит `RuntimeIdentifier=win-x64` → macOS-лейн **оверрайдит RID** (`-r osx-arm64` или unset), а `SileroOnnxSynthesizer` конструируется **только под `Voice:Live`/Windows**, чтобы macOS-лейн не dlopen'ил onnx. Асёрт: `FakeAudioDevice` получил PCM по эмитированным репликам; `NetworkCallCount==0`; чистый shutdown; **каждый `SpokenUtterance.SpokenText`, доходящий до очереди, проходит raw-unit-leak-regex** (нет `3929мс`/`4 метра`/`км/ч` — F13 voice-side-backstop против Phase-3-класса raw-number-дефектов). **Локальный** preempt-инвариант (не глобальный rank-sort — реплики приходят по времени сессии): `PreemptEvents.Contain(p => p.Incoming.Priority < p.Interrupted.Priority)`. | `SimCoach.App.Tests` replay-e2e (V-E9) на macOS/CI, без железа, без сети | A |
| **O-3** Offline debrief-стрим работает | `Llm:Live=false` резолвит все маршруты в `FakeProvider`; `FakeProvider.StreamAsync` отдаёт детерминированный multi-sentence RU-прозы-стрим (с терминальной `FinishReason`+usage-shape) — озвученный дебриф-путь прогоняется без сети. | e2e: replay→SessionEvent→fake prose stream→sentences→fake TTS PCM→headless device→WAV + `audio_artifact_ref` записан после INSERT + headline эмитится | A |
| **O-4** Non-Windows device-селекция | На non-Windows composition-edge регистрирует `NullAudioDevice`+`WdlPcmResampler`, **никогда** `WasapiAudioDevice`/`MediaFoundationPcmResampler` (**наши** `IPcmResampler`-обёртки, не NAudio-типы), с `PlatformNotSupportedException`-runtime-safety-гардом (паритет с `AddTelemetrySource`). Хоткей-сервиса в P4 нет (глобальный Ctrl+Alt+M → P5). | DI-construction off-Windows-тест | A |
| **O-5** Coverage (NFR-009 честно) | **NFR-009** (`functional-requirements.md:114`) называет `Pipeline.*`/`Coach.*`/`Reference.*` ≥80% и `Overlay.*` ≥50% — **не** называет `Audio.*`/`Voice.*`/`LLM.*`. `testing-strategy.md` line 3: `Audio.* ≥ 50%` (существующий doc-floor). Честные цели: **`Audio.* ≥ 50%`** (doc-floor); **`Voice.* ≥ 50%`** (предложение по аналогии — **нет** doc-floor, требует sign-off owner'а + правку `testing-strategy.md`, не односторонний assert гейта); **`VoiceTipSink ≥ 80%`** т.к. реализует `ICoachTipSink` в `Coach.*`-scope (подтвердить ассембли-принадлежность перед пиннингом 80%). Чистая математика очереди/fade/drop/latency держит ≥80% локально как quality-gate, питающий TtsEval. FFI-shim-файлы (WASAPI/ONNX/Win32) исключаются из знаменателя coverlet-фильтрами. | `reportgenerator → assert coverage ≥ threshold` CI-степ (уже в `testing-strategy.md §CI`); любой floor выше — правка `testing-strategy.md` | A (+ docs-edit для повышения) |
| **O-6** Non-blocking sink под нагрузкой | С fake-`ITtsBackend`, чей `StreamAsync` блокируется бесконечно, `EmitTipAsync`/`EnqueueAsync` всё равно завершается **синхронно** (`Task.IsCompletedSuccessfully`/`ValueTask.IsCompleted` == true, без `await`) — доказывает, что sink не может застопить 333 Гц ingest. | `SlowFakeQueue`/blocking-fake-TTS unit | A |

---

## 6. GO/NO-GO чеклист

Фаза 4 — **GO** только когда каждый бокс отмечен. Стиль зеркалит вердикт-аддендум Phase-3. `[B]` = BLOCKING, `[S]` = sign-off (non-blocking).

- [ ] **[B] A-V0 — Silero-спайк разрешён.** Либо `v5_ru` крутится in-proc через `Microsoft.ML.OnnxRuntime` с chunked-PCM-стримом + знаками ударения (Silero primary), либо спайк зафейлил и Yandex SpeechKit промоутнут в primary с задокументированной fallback-веткой (ADR-0023; note «Superseded in part» в ADR-0005). Первичный бэкенд зарегистрирован в composition.
- [ ] **[B] A-Perf-HW — first-audio ≤ 200 мс на РЕАЛЬНОМ железе (FR-040)** — `SileroOnnxSynthesizer` на CPU EP + timestamp NAudio buffer-fill над N≈100 репликами, **p100 ≤ 200 мс**, Windows-only perf-smoke **вне** детерминированного macOS-лейна. Advisory (`EnforceFirstAudioBudget=false`) пока V0 не зафиксирует реальное распределение; затем flips blocking. **Fake-clock — отдельный queue-plumbing DSP/logic-ассерт, НЕ FR-040** (логическое время, не стоимость синтеза).
- [ ] **[B] A-041 — TTS стримит 20–40 мс 16-бит PCM** кадры на `SampleRateHz` (chunk-shape unit).
- [ ] **[B] A-042 — глубина 1 in-flight + 1 queued;** preempt = 10–20 мс линейный fade, монотонно невозрастающий, seam \|Δsample\| < `ClickThreshold`, без клиппинга (sample-level V-E3, stereo-aware).
- [ ] **[B] A-043 — stale-drop:** corner-critical ≥ 1 с, general ≥ 2 с, на enqueue И promotion, на fake-часах (V-E6).
- [ ] **[B] A-Cancel — отмена посреди синтеза убирает аудио ≤ 50 мс** (V-E5, именованный owner-deliverable).
- [ ] **[B] A-RMS — RMS синтезированного PCM в полосе** (не тишина / не клиппинг, V-E7).
- [ ] **[B] A-044 — `voice.mute` тоглит mute STATE** (`IMuteState`): in-flight+queued молчат, стрим-оффсет продвигается, enqueue продолжается (глубина цела); `mute_on_startup` уважается; персист через `ISettingsStore`. (Глобальный Ctrl+Alt+M-хоткей-биндинг → **Phase 5**.)
- [ ] **[B] A-045 — громкость независима от игры** (`SetVolume` масштабирует выход; отдельный код-путь от mute); WASAPI-интеграция на Windows.
- [ ] **[B] A-046 — Yandex за флагом,** offline через fake-gRPC, **только короткий RU-текст на проводе** (privacy-assert NFR-004); env-gated live-лег non-blocking.
- [ ] **[B] A-M40 — дебриф стримится токен-за-токеном** через `ILlmClient.StreamAsync` (реальный OpenRouter SSE-декод) на маршруте `debrief_prose` (`Stream=true`) и проигрывается через TTS; **никаких `NotSupportedException`** на дебриф-маршруте; `CircuitBreaker`/`CostMeter` метерят стрим; fallback-once на open в `debrief_prose_fallback`.
- [ ] **[B] A-Debrief-Scope — structured `debrief`-маршрут байт-в-байт неизменён;** P4 пишет только `audio_artifact_ref` + WAV, P6 читает; P6-reserved-колонки не тронуты; `mvp-deferrals.md:41-46` + XML-doc + три `NotSupportedException`-сообщения обновлены.
- [ ] **[B] A-Debrief-Shutdown — дебриф переживает shutdown-drain** на `CancellationToken.None`, ограничен `RouteOptions.Timeout` + `ShutdownPlaybackCeiling` (≤ host `ShutdownTimeout`), деградирует до WAV-only без hang'а; audio останавливается **после** `CoachService`.
- [ ] **[B] A-NonBlock — `VoiceTipSink.EmitTipAsync` возвращается до синтеза** (не стопит 333 Гц coach-pipeline; no-clock-advance/blocking-fake unit).
- [ ] **[B] A-Fanout — `CoachService` фанит каждый `CoachTip` во все `IEnumerable<ICoachTipSink>`** (`ConsoleTipSink`+`VoiceTipSink`) с per-sink fault-isolation (бросающий sink изолирован, console-persist происходит, host не падает под `StopHost`).
- [ ] **[B] A-Gate — TTS-eval зелёный offline** (без ключа): RuPhonetics preserve/insert-фикстуры проходят; known-bad-анкеры (срезанный `+`, unit-leak) отвергаются; fade/latency/cancel/stale/rms hermetic-легенды проходят; `TtsEvalOptions.EnsureValid` self-тест зелёный.
- [ ] **[B] A-Golden — golden-audio stress-регрессия** (D4, per-release): reference-аудио (WAV / phoneme-duration-stress-векторы), запечённое из V0-валидированной модели; assert что знаки ударения **реально проговариваются** + нет gross-mispronunciation-регрессии (текстовые RuPhonetics-фикстуры этого класса не ловят).
- [ ] **[B] A-Manual — скриптованный BLOCKING manual-протокол** (D4, §3.2): фикс-скрипт ~15–20 реплик (каждый `racingLexicon`-термин + car-length 1/2/5 + длиннейшие имена поворотов + реальная 3-corner preempt-последовательность) на реальном Windows-аудио; явный pass-чеклист (ударение / согласование чисел / нет щелчка на preempt / громкость независима / нет cross-corner-оверлапа) + **owner sign-off**.
- [ ] **[B] A-Offline — non-Windows build + полный тест-suite зелёный** без аудио-железа и без сети (только фейки); CA1416-чисто; V-E9 e2e `NetworkCallCount==0`.
- [ ] **[B] A-StopOrder — load-bearing reversed stop-order сохранён:** голосовые hosted-services/pumps слотятся так, что audio-playout **переживает** дебриф-drain `CoachService` (не тор­дается раньше), и не задерживает `SessionManager`-finalize; расширенный `HostCompositionTests`-assert.
- [ ] **[B] A-ValidateOnStart — host падает на старте (не в рантайме)** на плохих `VoiceOptions`/`AudioOptions`/`DebriefProseOptions` (bad Volume/Engine/fade-band/stale-order/`ShutdownDrainTimeout`/`ShutdownPlaybackCeiling > сконфигурированный HostOptions.ShutdownTimeout`/отсутствующий prompt-resource/unrated `debrief_prose`/`debrief_prose.Stream!=true`); privacy-нотис логируется при `Engine=Yandex`.
- [ ] **[B] A-Cov — `Audio.* ≥ 50%` (doc-floor), `VoiceTipSink ≥ 80%` (Coach-scope);** любая цель выше для `Voice.*`/`Audio.*` требует правки `testing-strategy.md` + sign-off owner'а (не односторонний assert гейта). `reportgenerator` CI-assert.
- [ ] **[S] A-Listen — неформальный in-game listen:** RU-голос естественен на ~10-мин стинте; racing-термины (`апекс`, `трейл-брейкинг`) и car-lengths произносятся верно; никаких слышимых щелчков на реальных preempt'ах; громкость реально независима от игры. Аудио-качественная половина, которую гейт не может заасёртить.

---

### Открытые решения к закрытию до финального GO

1. **`SimCoach.Audio → SimCoach.Coach`-референс** (нужны `CoachPriority`/`CoachCadence`): либо ProjectReference (расширяет coupling), либо `VoiceTipSink` (Slice 3) мэпит `CoachTip` в slim `SpokenUtterance` с Audio-owned `readonly record struct` (копия `(Phase,Rank,IsCornerCritical)`, без bit-packing). Рекомендация: вариант (b) — сохраняет текущий граф. Приоритет **никогда** не ре-энкодится в flattened-int (`CoachPriority.cs` doc это запрещает); порядок = `UtterancePriority.Compare` через `CoachPriority.CompareTo` напрямую.
2. **`Voice:Backend`→`Voice:Engine`-реконсиляция** (PR-C): appsettings `Voice:Backend` (string) → `Voice:Engine` (enum), `Voice:Silero:Voice`→`Voice:Silero:Speaker` (коллизия с именем секции), `Voice:Yandex.Enabled` свёрнут в `Engine==Yandex`; + `MapKey`-строки `voice.engine`/`voice.enabled`/`voice.volume`/`voice.mute`/`voice.mute_on_startup` → `Voice:Runtime:*` (иначе settings-write не ре-биндит live — `SqliteSettingsConfigurationProvider.MapKey` сегодня возвращает `null` вне allowlist'а). `hotkey.mute` — **P5-reserved** (глобальный хоткей в P5).
3. **Механизм терминального `usage` через `IAsyncEnumerable<LlmDelta>`-границу** (PR-J): либо `LlmStreamResult`-side-channel, либо `CostMeterProvider.StreamAsync` буферит терминальную дельту. Выбрать явно.
4. **Yandex auth** (PR-E): API-ключ из `YANDEX_SPEECHKIT_API_KEY` env (никогда settings, NFR-004) в `authorization`-metadata-header; `FolderId` в settings (non-secret); IAM-токены истекают ~12 ч (MVP предпочитает API-ключ, чтобы избежать refresh-loop). Подтвердить key-vs-IAM с owner'ом.
