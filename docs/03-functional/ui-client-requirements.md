# UI Client Requirements (Forward-Looking Catalogue)

**Status:** Living spec. Drafted during the Phase-3 (Coach Engine + LLM) rework.
**Scope:** Everything the SimCoach desktop/overlay client needs across the whole roadmap (P3→P8+), so that **Phase 3 exposes the right contracts now** and the P4–P7 UI work does not force a re-architecture.
**Audience:** Whoever implements Coach/LLM/Storage seams in P3, and whoever builds Voice (P4), Overlay (P5), Debrief (P6), and the Beta settings/onboarding shell (P7).

> This document does **not** ask Phase 3 to build Avalonia views. It asks Phase 3 to **design the DTOs, the single tip sink, the persistence columns, the config keys, and the query-method signatures** that those views will bind to. Where a contract can ship cheaply in P3 it is marked **P3-now**; where only the *shape* must be reserved it is marked **P3-reserve**.

Ground truth: the clickable mockup `SimCoach UI.dc.html` (7 screens) + `screenshots/overlay.png`, the spine harvest (compute domain events, `llm_usage`/`settings` schema, `ICoachTipSink`/`CoachTip`, `IOptions` pattern), and the approved Phase-3 amendments (B1–B3, M1–M7, m1–m5, owner product decisions).

---

## 0. Conventions used in this doc

| Tag | Meaning |
|---|---|
| **P3-now** | Contract (DTO field / table column / interface signature / config key) must be **implemented** in Phase 3. |
| **P3-reserve** | Only the **shape** is declared in Phase 3 (enum value, nullable column, empty repeated field, seam interface); the producing/consuming logic lands in a later phase. |
| **Screen NN** | Mockup screen in `SimCoach UI.dc.html` / `overlay.png` (see §1). |
| **FR-###** | Functional requirement id (see `docs/03-functional/` requirement set). |

**Delta colour contract (load-bearing, F1 convention)** — every delta-rendering surface (overlay, dashboard, debrief, references) MUST honour this. It is a UI/theming token set, not a coach concern, but the data feeding it (sign of `delta_ms`, `is_pb`, "absolute best ever") must be derivable from the spine:

| Token | Hex | Meaning |
|---|---|---|
| `delta.best.ever` | `#b14dff` (purple) | Absolute best ever (session-independent / pinned reference). |
| `delta.ahead` | `#3ddc84` (green) | Faster than own PB. |
| `delta.behind` | `#ffd23f` (yellow) | Slower than reference. |
| `trace.brake` | `#ff5a4d` (red) | Brake trace channel. |

---

## 1. Screen inventory (mockup → phase map)

| Screen | Mockup name | Primary phase | FR tags |
|---|---|---|---|
| 01 | Игровой оверлей (in-game overlay) — Full + Race mode | P5 | FR-050…055 |
| 02 | Дашборд · LIVE-сессия (desktop live session) | P5/P7 | FR-050+, FR-012 |
| 03 | Post-session Дебриф / Итоги сессии | P6 | FR-060…063 |
| 04 | Настройки · LLM (+ 9 settings sections) | P7 | FR-070, FR-071 |
| 05 | First-run мастер / Онбординг (5-step) | P7 | FR-070 |
| 06 | Расходы / Cost meter | P7 | FR-072 |
| 07 | Референсы / Reference library | P7 | FR-012 |

Phase 3 is **non-visual** (`ConsoleTipSink` is the only sink it ships), but every screen above binds to a contract Phase 3 must define. §3 is the load-bearing section.

---

## 2. UI surfaces catalogue (by area)

### 2.1 In-game overlay — Screen 01, `overlay.png`

Transparent, topmost, click-through (`WS_EX_TRANSPARENT | WS_EX_LAYERED`, Avalonia `TransparencyLevelHint="Transparent"`). Targets `net9.0` (not `-windows`), ADR-0002. Rendering capped 30 Hz (FR-054). Auto-hide when the game loses focus (FR-055).

| Widget (RU label) | Data fields | Phase | Depends on (§3 contract) |
|---|---|---|---|
| Voice status chip `ГОЛОС ВКЛ` + pulsing dot | `voice.enabled` bool; live "speaking" flag | P5 | `ISettingsStore.GetVoiceEnabledAsync`; P4 voice-active signal |
| Mute hint `Ctrl+Alt+M` | bound hotkey string | P5 | `HotkeyOptions.MuteToggle` |
| Delta block `ДЕЛЬТА К ПБ` (`−0.182`) + centred-zero bar | live signed delta-to-PB + colour by sign | P5 | **Live delta read** (§3.4) |
| Sector chips `S1/S2/S3` (`−0.09`,`+0.14`, active `S3 ●`) | per-sector delta-to-PB + active-sector marker | P5 | **Per-sector delta read** (§3.4) |
| Coach tip card (slim): phrase + mic + param chip + short corner name | `CoachTip` DTO | P5 | **`CoachTip` + `ICoachTipSink`** (§3.1/§3.2) |
| Race-mode single widget (delta only) | live delta + bar only | P5 | `OverlayOptions.RaceMode.Enabled` + live delta |

The overlay coach card is the **canonical proof** of the `CoachTip` DTO field set (§3.1). The native ACC HUD beneath SimCoach (fuel, temps, TC/ABS/MAP, wear, gear, CONSISTENCY) is **not** rendered by SimCoach — it only confirms the raw fields exist in ACC SHM for the B1 kernels and the pit-advisor seam.

### 2.2 Desktop dashboard (live session) — Screen 02

Nav rail: `Сессия` · `Дебрифы` · `Референсы` · `Расходы` · `Настройки` + record-status card `ЗАПИСЬ · 100 Гц · MCAP`.

| Panel | Data | Phase | Depends on |
|---|---|---|---|
| `ДЕЛЬТА К ПБ · LIVE` | live delta + bar | P5 | Live delta read (§3.4) |
| `КРУГ` | lap no., `N чистых из M` | P5 | `SessionEvent.lap_count`/`clean_lap_count`; live lap counter |
| `ПОСЛЕДНИЙ` | last lap time + `+0.18 к ПБ` | P5 | `LapEvent.lap_time_ms`/`delta_ms` |
| `ПБ · СЕССИЯ` | session best (purple) + `круг N · чистый` | P5 | `SessionEvent.pb_time_ms`; `laps` table |
| `СКОРОСТЬ · ТЕКУЩИЙ vs ПБ` trace | speed-vs-distance polyline (current vs PB), sector x-labels | P5 | **Speed-vs-distance trace read** (§3.4, deferred) |
| `СЕКТОРЫ` rows (active `S3 ● в процессе`) | per-sector delta + fill | P5 | Per-sector delta read (§3.4) |
| `ПОЗИЦИЯ НА КРУГЕ` | 3-zone progress, marker at norm-pos, live speed/throttle | P5 | **normalizedCarPosition + live speed/throttle read** (§3.4, M7) |
| `КОУЧ · СЕЙЧАС` | phrase + `action_id` chip + `cadence · priority · model` chip | P5 | `CoachTip` DTO incl. `ProviderModelId`, priority, rendered param |
| `ЛЕНТА ПОДСКАЗОК` (tip log) | timestamped tip list, older faded | P5 | `coach_tips` table read (§3.5) |
| Status bar | `ACC подключена`, `100 Гц`, `LLM ok · 142 мс`, `сессия $0.018` | P5/P7 | telemetry-source status; `ICostQueryRepository.GetSessionCostAsync` (§3.6) |

### 2.3 Post-session debrief — Screen 03

| Panel | Data | Phase | Depends on |
|---|---|---|---|
| Summary header | duration + laps + clean + weather | P6 | `SessionEvent` + session row |
| Stat row | best lap, avg clean, `+0.62 к ПБ`, balance verdict (`Недостаток · лёгкий`) | P6 | `SessionEvent.pb_time_ms`/`average_lap_ms`/`understeer_trend` (§3.7) |
| `ВРЕМЯ ПО СЕКТОРАМ vs ПБ` | grouped bar you-vs-PB, per-sector deltas | P6 | per-sector aggregate deltas in debrief envelope (§3.7, M3) |
| `ТОРМОЗ / ГАЗ · 2-й СЕКТОР vs ПБ` | throttle/brake traces vs PB | P6 | trace read (deferred; reads MCAP/parquet) |
| `РАЗБОР` (prose) | LLM loss-attribution paragraph | P6 | **debrief Gold `aggregated_losses`** (§3.7, B2) + `debrief` row |
| `ЧЕК-ЛИСТ` (3 checkable items) | action items, first pre-checked | P6 | debrief checklist persistence (§3.5/§3.7) |
| TTS player `ОЗВУЧКА · Silero v5` | play/scrub, `0:14 / 0:38` | P6 | P6 audio artifact ref on `debrief` row |
| Export | **PDF**, **Markdown** (Screen 03); CSV reserved | P6 | `DebriefOptions.ExportPath` (FR-063) |

"Теряешь 0.6 с на выходах из 7/8…" in `РАЗБОР` is exactly the `aggregated_losses{corner_id, corner_name, total_loss_ms, avg_loss_ms, sample_count, dominant_reason}` consumer (B2). FR-060 is **contradicted** if that data source does not exist — see §3.7.

### 2.4 Settings — Screen 04 (LLM panel shown; 9 sections)

Nav: **Общие · Телеметрия · Голос · LLM · Оверлей · Референсы · Хоткеи · Приватность · О программе**. All bind `IOptions`-backed keys persisted in the `settings` table (§3.8). Full key list in §3.8.

LLM panel (Screen 04) specifics:

| Setting | Control | Key (provider-neutral) |
|---|---|---|
| `LLM API-ключ / провайдер` (masked, `● валиден`, "хранится локально в secrets.json") | secret input | `secrets.json` (not `settings`); provider id in `Llm:Routes` |
| `Модель · корнер / сектор` + cost est `~$0.002/круг` | dropdown | `model.corner`, `model.sector` |
| `Модель · дебриф` + cost est `~$0.01/сессия` | dropdown | `model.debrief` |
| `Месячный лимит расходов` `$5.00` (slider) | slider | `budget.monthly_usd` |
| `Circuit breaker` (state `закрыт`) | toggle | `Llm:CircuitBreaker` |

> **Conflict flag (m1/M1/M2):** the mockup hardcodes "OpenRouter" / "DeepSeek V3.2" / "Gemini 2.5 Flash". This **contradicts** the provider-agnostic LLM seam rule. The settings key MUST be provider-neutral (`LLM API-ключ / провайдер`, not `OpenRouter API-ключ`). Model defaults reconcile per §5 / §3.9.

### 2.5 Onboarding wizard — Screen 05

5-step stepper. Step 1 = **ОБНАРУЖЕНИЕ ИГРЫ** (ACC auto-detect green check; iRacing/LMU/F1 25 disabled "скоро — phase 2"). Steps 2–5 (implied): API key, voice, overlay/hotkeys, reference/first-lap — all bind the same `settings` keys. Gated by `general.first_run_completed` (P7).

### 2.6 Cost meter — Screen 06

| Widget | Data | Depends on |
|---|---|---|
| `РАСХОД · 30 ДНЕЙ` `$2.31` | rolling-30d total | `ICostQueryRepository.GetRolling30DayCostAsync` (§3.6) |
| `ЭТА СЕССИЯ` `$0.018` | per-session total | `GetSessionCostAsync` |
| Daily bar chart (14d) | per-day series | `GetCostByDayAsync` (§3.6) |
| Per-model breakdown | per-model/per-cadence | `GetCostByRouteAsync` |
| `Месячный лимит` `$2.31 / $5.00` | progress vs cap | `budget.monthly_usd` + `SessionBudgetUsd` guard |

### 2.7 Reference library — Screen 07

Header: "Reference-круги · один ПБ на (трасса · машина · погода)". Import / Export (header) + per-row PIN.
Columns: `ТРАССА · МАШИНА` (+ subline `сессия · круг`) | `ПОГОДА` chip | `КРУГ` (purple if pinned) | `PIN`.

→ `IReferenceQueryRepository` (§3.6): list keyed by (track·car·weather) → `{track, car, weather, source session+lap, lap_time, is_pinned}`; mutations pin/unpin/import/export.

---

## 3. Contracts the spine MUST expose for the UI  ← key section

### 3.1 `CoachTip` DTO (P3-now)

Lives in `SimCoach.Coach/CoachTip.cs`. Single DTO bound by the overlay coach card (Screen 01), dashboard `КОУЧ·СЕЙЧАС` + tip log (Screen 02), Voice (P4), and Debrief tip history (Screen 03). Record, `init`-only, `IReadOnly*` on any collections.

The overlay card *proves* the required field set. Base `CoachTip` from the spine harvest is missing three fields the card renders — they are **added in Phase 3**:

| Field | Type | Purpose / mockup proof | Status |
|---|---|---|---|
| `SessionId` | `string` | correlation / persistence | P3-now |
| `Cadence` | `CoachCadence` (Corner/Sector/Lap/Session **+ Strategy reserved**) | `corner · high` chip | P3-now (Strategy = P3-reserve, M-pit) |
| `CornerId` | `string?` | null for sector/lap/session | P3-now |
| `LapNumber` | `int?` | null for corner/sector | P3-now |
| `ActionId` | `string` | `brake_later_by_meters` chip | P3-now |
| **`RenderedParam`** | `string?` | the `+4м` chip — built in `PromptBuilder` but NOT currently persisted/emitted | **P3-now (NEW)** |
| **`Priority`** | `CoachPriority`/int | the `high` chip; MUST be a **total order** (M5) so `Take(5)` + golden tests are deterministic | **P3-now (NEW)** |
| `PhraseRu` | `string` | the spoken/displayed phrase | P3-now |
| **`CornerNameShort`** | `string?` | `О-Руж` (Eau Rouge) display form | **P3-now (NEW)** |
| **`CornerNameSpokenRu`** | `string?` | P4 voice path; strip `(N)`, RU ordinal (m4) | **P3-now (NEW, P4 consumes)** |
| `Source` | `TipSource` (Llm/Template) | template→dimmed styling | P3-now |
| `NoPbYet` | `bool` | "no PB yet" label (FR-014) | P3-now |
| `ProviderModelId` | `string?` | dashboard `· gemini-2.5-flash` chip | P3-now |
| `GeneratedAtUtc` | `DateTimeOffset` | tip-log timestamp `12:03` | P3-now |

`CornerNameShort` / `CornerNameSpokenRu` derive at emit time from `CornerNameMap` (RU `.resx`, English code identifiers) + track context. RU strings live in `.resx`; the short/spoken forms are user-facing.

### 3.2 `ICoachTipSink` — the single seam (P3-now)

```csharp
public interface ICoachTipSink
{
    // Must be non-blocking so it cannot stall the coach pipeline.
    Task EmitTipAsync(CoachTip tip, CancellationToken ct);
}
```

- **P3** ships `ConsoleTipSink` (structured log + persist to `coach_tips`).
- **P4** Voice and **P5** Overlay both implement/subscribe to this same interface — do not invent a second seam.
- `CoachService` subscribes to `DomainEventFanOut` **in its constructor** (never misses opening events) and drains `.ReadAllAsync(ct)` to completion on shutdown so the final `SessionEvent`-derived tip survives the load-bearing stop order.

### 3.3 New `CoachCadence` value + Strategy seam (P3-reserve)

`CoachCadence { Corner, Sector, Lap, Session, Strategy }`. `Strategy` is **reserved now** (enum value + route-key mapping + a dedicated *strategy quiet-zone*) for the pit advisor, but **no Strategy tips are emitted in MVP** (owner decision: defer delivery, reserve the seam). Pit advisor timing (owner): **not** corner exit — main straight / pit-window approach, ~1 lap lead, event-driven on fuel/tyre/mandatory-window thresholds, gated so it never collides with a corner tip. Template-first / LLM-optional.

### 3.4 Live read contracts the overlay/dashboard require from compute

What compute **already emits** over `DomainEventFanOut` (lossless, causal order) — confirmed by harvest:

| Surface need | Already available? | Source |
|---|---|---|
| Live lap delta-to-reference | ✅ at lap finish | `LapEvent.delta_ms` |
| Per-sector time | ✅ stored | `laps.s1_ms/s2_ms/s3_ms` |
| Session best | ✅ | `SessionEvent.pb_time_ms` (running `_runningBestMs`) |
| Lap/clean counts | ✅ | `SessionEvent.lap_count`/`clean_lap_count` |
| Per-corner deltas + diffs/scores | ✅ | `CornerEvent` (delta_ms, brake_point_diff_m, min_speed_diff_kmh, trail_brake_pct_*, throttle_resume_diff_m, racing_line_deviation_m, off_track, understeer_score, oversteer_score) |
| Per-sector delta + top losses | ✅ | `SectorEvent.delta_ms`, `top_losses[]` |

What the UI needs that compute does **not yet** stream — note the contract, **do not block** P3 (deferrable to P5 compute extension or UI-side calc):

| Missing UI read | Resolution | Phase |
|---|---|---|
| **Live (intra-lap) delta-to-PB** for the always-on overlay number | UI-facing live delta stream (a `LiveDeltaSample{ delta_ms, sign, against_best_ever }`) — P5 compute extension; until then overlay shows last `LapEvent.delta_ms` | P5 |
| **Per-lap-per-sector delta-to-PB** with active-sector marker (S1/S2/S3 live) | `SectorEvent.delta_ms` exists; UI computes per-lap S1/S2/S3 vs PB from `laps` table, or P5 adds a `SectorLapAggregate` field | P5 |
| **normalizedCarPosition + corner-phase marker + live speed/throttle** (Screen 02 `ПОЗИЦИЯ НА КРУГЕ`; also the M7 gate-snapshot field) | Add `normalizedCarPosition` (+ corner-phase) to the gate-snapshot field list, OR mark apex-window/straight/user-quiet-zone gates **deferred** so they don't silently no-op (M7) | P3 decision, P5 read |
| **Speed-vs-distance trace** (current vs PB) | UI reads resampled parquet lap + reference channels; no new compute | P5 |
| **Theoretical-best gap + lap-time stddev** | UI calculates from stored `laps`; no new compute field | P6 |

**Key constraint:** Compute does **not** need per-sector deltas or stddev for Phase 3 to ship. Only the M7 `normalizedCarPosition` decision is a Phase-3 call (add the field, or mark the dependent gates deferred — pick one so gates don't silently no-op).

### 3.5 Persistence the UI reads back

| Table | Created/changed | Purpose | Phase |
|---|---|---|---|
| `coach_tips` (migration 003) | session_id, cadence, corner_id, lap_number, action_id, **rendered_param**, **priority**, phrase_ru, source, no_pb_yet, provider_model_id, generated_at_utc | tip log (Screen 02), debrief tip history (Screen 03), cost attribution | **P3-now** |
| `llm_usage` (migration 002) | adds `provider`, `cached_input_tokens` | cost queries (Screen 06), settings cost est (Screen 04) | **P3-now** |
| `settings` (existing 001) | key/value/updated_at_utc | all settings panels | **P3-now** (keys §3.8) |
| `debrief` row | prose, checklist items (+checked state), per-sector aggregate deltas, balance verdict, audio artifact ref, `setup_hint` nullable | Screen 03 | **P6** (schema **P3-reserve**: leave nullable columns + shape) |
| `sessions` / `laps` / references | existing | session history, reference library | exists |

`coach_tips` gains `rendered_param` + `priority` vs the harvest draft so the tip log/debrief can re-render the `+4м` chip and ordering offline.

### 3.6 Query-API signatures the UI consumes (declare in P3, implement P3/P5/P6)

```csharp
// Cost — Screen 06 + Screen 04 cost estimates + Screen 02 status bar. Persist in P3; queries P3-declared.
public interface ICostQueryRepository
{
    Task<CostSummary>                 GetSessionCostAsync(string sessionId, CancellationToken ct);
    Task<RollingCost>                 GetRolling30DayCostAsync(CancellationToken ct);
    Task<IReadOnlyList<CostByDay>>    GetCostByDayAsync(int days, CancellationToken ct);       // 14-day bar chart
    Task<IReadOnlyList<CostByRoute>>  GetCostByRouteAsync(DateTimeOffset fromUtc, CancellationToken ct); // per cadence+provider+model
}

// Settings store — Screen 04/05 + overlay/voice bindings. Interface + SQLite impl in P3.
public interface ISettingsStore
{
    Task<string?>  GetModelIdAsync(string cadenceKey, CancellationToken ct);   // "corner"|"sector"|"lap"|"debrief"
    Task           SetModelIdAsync(string cadenceKey, string modelId, CancellationToken ct);
    Task<decimal?> GetMonthlyBudgetAsync(CancellationToken ct);
    Task           SetMonthlyBudgetAsync(decimal usd, CancellationToken ct);
    Task<bool>     GetVoiceEnabledAsync(CancellationToken ct);
    Task           SetVoiceEnabledAsync(bool enabled, CancellationToken ct);
    // … voice engine, locale, race-mode, palette (theme/accent/canvasTone), hotkeys — keys in §3.8
}

// Reference library — Screen 07. Read contract noted; mutations pin/unpin/import/export.
public interface IReferenceQueryRepository
{
    Task<IReadOnlyList<ReferenceLap>> ListAsync(string? trackId, string? carId, string? weatherBucket, CancellationToken ct);
    Task SetPinnedAsync(string referenceId, bool pinned, CancellationToken ct);
    // import(parquet path), export(referenceId, path) — P7
}

// Session history + debrief read — Screen 03 + history browser (P7).
public interface ISessionHistoryRepository
{
    Task<IReadOnlyList<SessionSummary>> ListAsync(SessionFilter? filter, CancellationToken ct); // date/track/car/laps/best/clean/tips/cost
    Task<IReadOnlyList<CoachTipRow>>    GetSessionTipsAsync(string sessionId, CancellationToken ct);
}
```

`CostByRoute` carries `{ RouteKey (corner/sector/lap/debrief), ProviderId, ModelId, CallCount, InputTokens, OutputTokens, CachedInputTokens, CostUsd }` so Screen 06's per-model breakdown and Screen 04's per-cadence estimate both bind it. Budget enforcement: `RuleEngineOptions.SessionBudgetUsd` guard + `budget.monthly_usd` checked against `GetRolling30DayCostAsync`.

### 3.7 Debrief / session envelope additions (B2 blocker + M3)

**B2 (blocker — FR-060 is contradicted without this).** `SessionEvent` carries **no** loss data. Add an `internal sealed SessionLossAccumulator` inside `ComputeService` that accumulates over `CornerEvent`/`LapEvent` and emits, on `SessionEvent`:

```
aggregated_losses[] {
  corner_id, corner_name, total_loss_ms, avg_loss_ms, sample_count, dominant_reason
}
```

This is the data source for Screen 03 `РАЗБОР` ("Теряешь 0.6 с на выходах из 7/8" = `dominant_reason` per `corner_id`). Add it to the PR table.

- **m2:** put an explicit `maxItems` / post-parse cap on `aggregated_losses` (boundedness must be enforced, not implied).

**M3 — extend the session/debrief envelope** (feeds Screen 03 stat row + sector chart + setup hint):

| Field | Source | Note |
|---|---|---|
| `stints[]` (compound / degradation / pace) | `SessionEvent.stints` | proto field exists; **empty in MVP** (P3-reserve, future race-craft) |
| per-sector aggregate deltas | accumulated S1/S2/S3 vs PB | drives `ВРЕМЯ ПО СЕКТОРАМ vs ПБ` |
| lap-time consistency (stddev) + theoretical-best gap | computed from `laps` | UI-side calc acceptable |
| `setup_hint` | from `understeer_trend` / `tyre_degradation_pct` | nullable; **feed it or drop it** — do not leave a dangling key |
| balance verdict (`Недостаток · лёгкий`) | `SessionEvent.understeer_trend` (−1..1) | Screen 03 stat row |

### 3.8 Settings keys (IOptions + `settings` table) — provider-neutral

All thresholds are `IOptions<T>` with `EnsureValid()` called at composition (fail-fast). RU user-facing labels → `.resx`; keys/identifiers stay English. **P3-now** = key reserved + bindable in P3; consuming UI later.

| Category | Key | Type | Default | FR / amendment | Phase |
|---|---|---|---|---|---|
| General | `general.language` | enum (RU…) | RU | FR-073 | P3-now |
| | `general.theme` | enum (Тёмная/Светлая) | Тёмная | palette | P3-now |
| | `ui.accent` | enum (blue/green/orange/yellow) | blue | palette | P3-now |
| | `ui.canvas_tone` | enum (Графит/Чёрный/Сталь) | Графит | palette | P3-now |
| | `general.first_run_completed` | bool | false | FR-070 | P3-now |
| | `general.minimize_to_tray` | bool | true | — | P3-reserve |
| Coaching | `coaching.enabled` | bool | true | FR-030+ | P3-now |
| | `model.corner` | string | `google/gemini-2.5-flash-lite` | M1/m1 | P3-now |
| | `model.sector` | string | `google/gemini-2.5-flash-lite` | M1/m1 | P3-now |
| | `model.lap` | string | `google/gemini-2.5-flash-lite` | M1/m1 | P3-now |
| | `model.debrief` | string | `anthropic/claude-sonnet-4.6` | M1 (§3.9) | P3-now |
| | `budget.monthly_usd` | decimal | 5.0 (mockup) / 10.0 (FR-072) — reconcile §5 | FR-072 | P3-now |
| | `reasoning.debrief` | enum (Off/Low) | Low | M2 | P3-now |
| Voice | `voice.enabled` | bool | true | FR-040+ | P3-now (P4 consumes) |
| | `voice.engine` | enum (Silero/Yandex) | Silero v5 | FR-046 | P3-now |
| | `voice.volume` | int 0–100 | 100 | FR-045 | P3-reserve (P4) |
| | `voice.mute_on_startup` | bool | false | FR-044 | P3-reserve (P4) |
| Overlay | `ui.race_mode` | bool | false | FR-053 | P3-now |
| | `overlay.delta.visible/position/opacity/font` | … | … | FR-050…052 | P3-reserve (P5) |
| | `overlay.sectors.*` / `overlay.tip.*` / `overlay.lap.*` | … | … | FR-051 | P3-reserve (P5) |
| | `overlay.auto_hide_unfocused` | bool | true | FR-055 | P3-reserve (P5) |
| | `overlay.target_refresh_hz` | int | 30 | FR-054 | P3-reserve (P5) |
| Hotkeys | `hotkey.mute` | string | `Ctrl+Alt+M` | FR-044 | P3-now |
| | `hotkey.race_mode` | string | `Ctrl+Alt+R` | FR-053 | P3-reserve (P5) |
| | `hotkey.settings` | string | `Ctrl+Alt+S` | — | P3-reserve (P7) |
| Privacy | `privacy.gold_only_egress` | bool (locked true) | true | privacy doc | P3-now |
| | `storage.data_root` | path | `%LOCALAPPDATA%/SimCoach` | — | exists |
| | `privacy.crash_reporting` | bool | false (opt-in) | NFR | P3-reserve (P7) |
| LLM infra | `Llm:CircuitBreaker` (FailureThreshold 3 / 60 s / 60 s) | record | — | M2 | P3-now |
| | `Llm:Live` | bool | false (gated) | beta safety | P3-now |

API key/provider: stored in `secrets.json` (DPAPI on Windows), **not** the `settings` table, **not** Gold-egress. Key/provider id is provider-neutral.

### 3.9 LLM model + route defaults (M1/M2/m1 — folded in; pricing re-confirmed via claude-api skill 2026-06-27)

| Route | Model id (config) | OpenRouter-style slug | Reasoning | Rationale |
|---|---|---|---|---|
| corner | `google/gemini-2.5-flash-lite` | `google/gemini-2.5-flash-lite` | Off (timeout-forced, ~2000 ms) | m1: real-time eval-gated candidate; **not** Gemini 3 Flash family (thinking-first, blows the 2000 ms corner timeout) |
| sector / lap | `google/gemini-2.5-flash-lite` (eval-gated; `gemini-2.5-flash` fallback) | … | Off | cheap real-time |
| debrief (default) | `anthropic/claude-sonnet-4.6` | `anthropic/claude-sonnet-4.6` | **Low** (adaptive thinking on Sonnet 4.6) | M1 owner default (~1.8¢/session); **Haiku 4.5** middle ground (~0.6¢) |
| debrief (middle ground) | `anthropic/claude-haiku-4.5` | `anthropic/claude-haiku-4.5` | Low | optional cheaper debrief |

**Anthropic pricing (re-confirmed via claude-api reference, 2026-06-27)** — feeds `Llm:Providers[anthropic]` for cost estimates:

| Model | Canonical model id | Input $/1M | Output $/1M | Context |
|---|---|---|---|---|
| Claude Sonnet 4.6 | `claude-sonnet-4-6` | **$3.00** | **$15.00** | 1M |
| Claude Haiku 4.5 | `claude-haiku-4-5` | **$1.00** | **$5.00** | 200K |

> Provider note: the OpenRouter-style slug (`anthropic/claude-sonnet-4.6`, dotted) is how the route's `ModelId` is written for the OpenRouter provider; the **canonical Anthropic API id is hyphenated** (`claude-sonnet-4-6`) and is what a future native-Anthropic provider would send. The seam is provider-agnostic, so `ModelId` is opaque to the LLM library and resolved per provider — **do not lock to OpenRouter** (contradiction flagged in §2.4).

- **M2:** debrief route Reasoning=Low; **gate DeepSeek off** until vLLM #41132 (thinking+JSON corruption) is verified fixed. corner/sector/lap stay Reasoning=Off (timeout-forced).
- **m3:** the "no accuracy upside" claim for reasoning-off real-time is **design-asserted**, gated by the RU eval, **not measured**.

### 3.10 `ValidateOnStart` checklist (B3 — one test each)

Phase 3 must ship the enumerated fail-fast checklist so the host crashes at startup, not at runtime:

1. `CostMeter` rate coverage — every route's provider/model has input+output (+cached) rates.
2. Route/cadence completeness — every `CoachCadence` (incl. reserved Strategy mapping) has a route.
3. `FallbackRouteKey` acyclicity — no fallback cycle.
4. Registry-field-vs-Gold validity — every action-registry field exists in the Gold artifact for its cadence.
5. Positive timeouts / max-tokens — all `> 0` (timeouts `>= 100 ms`).
6. Prompt-resource existence — every referenced system/few-shot prompt resource resolves.

### 3.11 Compute kernels feeding tip quality (B1 — add, don't delete)

The canonical action registry references compute fields that don't exist yet; the fail-fast loader (3.10 #4) would crash. Owner decision: **add the kernels** (raw data exists). These are Phase-3 work in `SimCoach.Reference`/`SimCoach.Pipeline`, and they are what let the overlay tip card render quality actions:

| Action field | Kernel | Raw source |
|---|---|---|
| wheelspin | wheel-slip / slip-ratio | proto `wheel_slip`, ACC `WheelSlip`/`SlipRatio` |
| brake-overlap-steer | brake × steer overlap | brake + steer |
| steering-jitter | steer-rate variance | steer rate |
| tyre / brake temp (overheat-from-abuse advice, **IN** Phase 3, lap cadence) | temp kernels | proto `tyre_temp_c`/`brake_temp_c`, ACC `TyreTempI/M/O`/`BrakeTemp`/`PadLife` |

Pit/strategy data (fuel, `fuel_per_lap_l`, `tyre_wear_pct`, `tc_active`/`abs_active`, ACC `EngineMap`/`Tc`/`TcCut`/`Abs`, pit-state) is **plumbed frame→Gold now** (P3-reserve) for the deferred Strategy cadence; **advice actions deferred** to a later race-craft phase.

### 3.12 Registry actions the overlay tip card needs (M5/M6)

- **M5:** replace the 3-value priority enum with a **total order** (integer priority, or causal-phase tie-break brake>entry>apex>exit then metric magnitude) so `Take(5)` and golden tests are deterministic and root-cause beats symptom. The dashboard `priority` chip + tip-log ordering bind this.
- **M6 (in the registry, not prose):** add ≥2 reference-free corner actions (`ease_understeer` when `understeer_score>0.7`; `settle_oversteer` when `oversteer_score>0.6`; `requires_reference=false`), `overdrove_entry` (`brake_point_diff_m>2 AND min_speed_diff_kmh<-3 AND off_track==false`), and gated per-cadence catch-alls with explicit delta-floor when-clauses (so corner silence is preserved). Reference-free actions are what let the overlay render a tip while `NoPbYet=true`.
- **M4:** add `coach.system.debrief.v1.ru.txt` + few-shot; `PromptOptions` selects prompt+few-shot **per cadence**; generalize the real-time rule from `corner_name` to `(corner_name|top_corner)`; add sector/lap + no-PB few-shots, a negative example, and an explicit "when may a number appear" rule. (Drives debrief prose quality on Screen 03.)
- **m5:** define the RU eval as a real gate (judge, rubric, fixtures incl. no-PB + debrief, numeric pass bar) and decide per-provider prompt caching of the static prefix.

---

## 4. Cross-cutting requirements

| Concern | Requirement | Source |
|---|---|---|
| UI toolkit | **Avalonia, `net9.0`** (not `net9.0-windows`), macOS-dev compatible | ADR-0002 |
| Overlay window | Transparent topmost, click-through (`WS_EX_TRANSPARENT|WS_EX_LAYERED`), `TransparencyLevelHint="Transparent"`, **no DLL injection** | ADR-0002/0007 |
| Render cap | overlay ≤ 30 Hz (FR-054); auto-hide on game focus loss (FR-055) | FR-054/055 |
| Localisation | RU user-facing text → `.resx`; code identifiers + comments English; corner short/spoken forms in `CornerNameMap` `.resx` | hard rule, m4 |
| Theming | `general.theme` {Тёмная/Светлая}, `ui.accent` {blue/green/orange/yellow}, `ui.canvas_tone` {Графит/Чёрный/Сталь}; delta colour tokens are load-bearing (§0) | Screen 04, mockup |
| Typography | IBM Plex Sans (UI/Cyrillic) + JetBrains Mono (telemetry/numbers) | design system |
| Privacy / egress | **Only Gold-tier JSON leaves the machine.** Raw telemetry never leaves local disk; Gold = derived scalars (`delta_ms`, `*_diff`, scores), never raw frame arrays / world coords / exact car id | privacy doc, hard rule |
| API-key storage | provider-neutral, in `secrets.json` (DPAPI on Windows); "хранится локально · не покидает машину" | Screen 04 |
| Multi-monitor | overlay must position over the game window's monitor; per-monitor DPI-aware (P5) | Screen 01 |
| Perf | 333 Hz ingest → bounded channel (DropOldest); overlay/voice sinks must be non-blocking so they cannot stall the coach pipeline | spine, §3.2 |
| Pub/sub | `System.Threading.Channels` only — no MediatR/event-aggregators; consumers subscribe to fan-outs **in constructors** | hard rule |
| Hosted-service stop order | load-bearing; `CoachService` inserted between `ComputeService` and `McapRecorderService`, drains to completion | spine |
| Records / immutability | DTOs are records, `init`-only, `IReadOnlyList`/`IReadOnlyDictionary` on public surfaces; mutation isolated to `internal sealed` collectors (e.g. `SessionLossAccumulator`) | hard rule |

---

## 5. Phase-by-phase rollout (surface → Phase-3 contract it depends on)

| Surface | Phase | Phase-3 contract it binds (must exist now) |
|---|---|---|
| Overlay coach card / Race-mode | P5 | `CoachTip` DTO (§3.1, incl. RenderedParam/Priority/CornerNameShort), `ICoachTipSink` (§3.2) |
| Overlay/dashboard live delta + sectors | P5 | live/sector delta read decisions (§3.4); M7 normalizedCarPosition call |
| Dashboard tip log | P5 | `coach_tips` table + `ISessionHistoryRepository.GetSessionTipsAsync` (§3.5/§3.6) |
| Voice (TTS) | P4 | `ICoachTipSink` + `CornerNameSpokenRu` (m4); `voice.enabled`/`voice.engine` keys (§3.8) |
| Debrief window + checklist + export | P6 | `aggregated_losses` (B2/§3.7), debrief envelope (M3), `debrief` row schema (P3-reserve, §3.5) |
| Debrief AI prose | P6 | debrief Gold + `coach.system.debrief.v1.ru` prompt (M4); `model.debrief`=Sonnet 4.6 (§3.9) |
| Settings · LLM (model-per-cadence + cost est) | P7 | `ISettingsStore` + `ICostQueryRepository.GetCostByRouteAsync` (§3.6); provider-neutral keys (§3.8) |
| Cost meter + monthly limit | P7 | `ICostQueryRepository` (session/30d/day/route), `SessionBudgetUsd` guard, `budget.monthly_usd` |
| Reference library | P7 | `IReferenceQueryRepository` (§3.6) |
| Session history browser | P7 | `ISessionHistoryRepository.ListAsync` (§3.6) |
| Onboarding wizard | P7 | `general.first_run_completed`, API-key/voice/overlay keys (§3.8) |
| Settings · all 9 sections | P7 | `settings`-table keys (§3.8), `IOptions` + `EnsureValid` |
| Pit advisor card | P9+ | `CoachCadence.Strategy` + strategy quiet-zone reserved (§3.3); fuel/temp/wear/pit/TC/ABS/engine-map plumbed to Gold (§3.11) |
| Sim selector / iRacing | P8 | `Sim` column on references/sessions; sim-agnostic `ITrackLengthProvider` seam already at composition edge |

---

## 6. Open questions / risks for the owner

1. **Provider-neutral seam vs mockup copy (§2.4).** Mockup says "OpenRouter API-ключ" / "DeepSeek V3.2" / "Gemini 2.5 Flash". Confirm the settings label becomes provider-neutral ("LLM API-ключ / провайдер") and that the LLM library never assumes OpenRouter. **Owner action:** approve copy change.
2. **Monthly budget default mismatch.** Mockup shows `$5.00`; FR-072 / `CoachingOptions.MonthlyBudgetUsd` defaults `$10.0`. Pick one (`budget.monthly_usd` default). Recommend `$5.00` to match the shipped mockup.
3. **Debrief model id pin (M1).** Confirm `anthropic/claude-sonnet-4.6` (canonical `claude-sonnet-4-6`, $3/$15 per 1M) as the pinned default vs Haiku 4.5 ($1/$5) middle ground. Resolve the DeepSeek "v3.2-vs-V4" ambiguity → **DeepSeek stays gated** (M2) until vLLM #41132 fixed.
4. **Real-time model (m1).** Confirm `google/gemini-2.5-flash-lite` as the eval-gated real-time candidate and the deprecation watch (Gemini 3 Flash / 3.1 Flash-Lite / 3.5 Flash are thinking-first and would blow the 2000 ms corner timeout — do **not** adopt real-time). The "no accuracy upside" claim is design-asserted, **not measured** (m3) — RU eval gates it.
5. **M7 decision required in P3.** Either add `normalizedCarPosition` (+ corner-phase marker) to the gate-snapshot field list, **or** mark the apex-window / straight / user-quiet-zone gates **deferred**. Without one, those gates silently no-op and the dashboard `ПОЗИЦИЯ НА КРУГЕ` panel has no source. **Owner/eng decision.**
6. **Pit advisor scope (owner-confirmed defer).** Confirm MVP reserves only the seam (`CoachCadence.Strategy` + strategy quiet-zone + Gold data plumb) and ships **no** Strategy tips. Timing model (main-straight / pit-window approach, ~1 lap lead, threshold-driven, gated vs corner tips) recorded for the later phase.
7. **Live per-sector delta source (§3.4).** Decide P5: UI computes S1/S2/S3-vs-PB from the `laps` table, or compute adds a live `SectorLapAggregate` field. Deferrable, but the overlay sector chips need a chosen source before P5.
8. **Debrief audio + checklist persistence shape.** P6 owns the `debrief` row, but Phase 3 should reserve nullable columns (prose, checklist+checked, per-sector aggregate deltas, balance verdict, audio ref, `setup_hint`) so P6 doesn't migrate against live data. Confirm the reserved shape.
9. **RU eval gate (m5).** Confirm the eval is a real gate (judge + rubric + fixtures incl. no-PB and debrief + numeric pass bar) and decide per-provider prompt caching of the static prefix before the real-time model is unblocked.

---

*Folded-in amendments: B1 (kernels), B2 (`aggregated_losses`), B3 (`ValidateOnStart`), M1–M7, m1–m5, and the owner product decisions (tyre/brake-temp advice IN P3; engine-map/ABS/TC data plumbed, advice deferred; pit advisor seam reserved). Pricing/model ids re-confirmed against the claude-api reference on 2026-06-27.*
