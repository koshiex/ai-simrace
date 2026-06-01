# Privacy Model — SimCoach

**Stance**: local-first. No raw telemetry, MCAP, Parquet, or reference data ever leaves the user's machine. Only short Gold-tier JSON artifacts (200–500 tokens) cross the network boundary, and only to the user's chosen LLM provider plus (optionally) the user's chosen cloud TTS.

---

## Data Flow Map

```
            ┌──────── stays local forever ────────────────┐
            │                                             │
ACC SHM → TelemetryFrame → MCAP file ─── Parquet (cold)   │
                                  │                       │
                                  └─► Reference Store     │
                                                          │
              ┌──────────────────── Gold artifact (200-500 tokens) ──────────┐
              │                                                              │
              ▼                                                              │
       OpenRouter (HTTPS, user's API key, user's chosen model)              │
              │                                                              │
              ▼                                                              │
       phrase_ru (≤ 25 words) + action metadata                              │
              │                                                              │
              ▼                                                              │
     TTS (Silero local in-process) OR Yandex SpeechKit (HTTPS, optional) ────┘
              │
              ▼
       Audio played locally
```

---

## What lives where

| Data | Location | Persistence |
|---|---|---|
| Raw telemetry frames | `%LOCALAPPDATA%/SimCoach/sessions/<ts>/raw.mcap` | until user deletes |
| Per-lap Parquet | `%LOCALAPPDATA%/SimCoach/sessions/<ts>/laps.parquet` | until user deletes |
| Reference laps (PBs) | `%LOCALAPPDATA%/SimCoach/references/<key>.parquet` | until user deletes |
| Sessions / settings / LLM usage | `%LOCALAPPDATA%/SimCoach/simcoach.db` (SQLite) | until user deletes |
| Secrets (API keys) | `%APPDATA%/SimCoach/secrets.json` (DPAPI-encrypted if available) | until user deletes |
| Logs | `%LOCALAPPDATA%/SimCoach/logs/*.log` | rotating, 7-day retention |

---

## Network egress

| Endpoint | When | Payload | User opt-out |
|---|---|---|---|
| `openrouter.ai/api/v1/chat/completions` | Per coaching cadence (corner/sector/lap/session) | Gold artifact JSON ≤ 500 tokens, no raw telemetry, no personally identifying info | Disable LLM in settings (fallback to template-only tips) |
| `tts.api.cloud.yandex.net` (optional) | Per utterance, only if Yandex backend selected | RU text string ≤ 25 words | Choose Silero local TTS instead |
| Velopack update server | App start | Version check only | Disable auto-update |

No telemetry, analytics, or crash reports leave the machine in MVP. Phase 7 may add opt-in crash reporting (Sentry); strictly opt-in, disabled by default.

---

## Personally identifying info

- **In MVP, none collected**. User's OpenRouter API key is theirs.
- The user's iRacing customer ID or ACC profile name are not sent off-machine.
- If the user enables Yandex SpeechKit, Yandex sees the synthesized text. Yandex's data-residency policy applies (their Russia DCs).

---

## Data deletion

- Settings → Privacy → "Delete all SimCoach data" wipes `%LOCALAPPDATA%/SimCoach/*` and `%APPDATA%/SimCoach/*`.
- Per-session delete from the sessions browser.
- Per-reference-lap delete from the references browser.

---

## Security

- `secrets.json` is excluded from `.gitignore` and from any export/share feature.
- On Windows we wrap secrets with DPAPI per-user encryption where available.
- API key fields in the settings UI are write-only — never displayed back, only their presence is shown.

---

## Compliance

- Local-first means no GDPR data-controller exposure for SimCoach itself in MVP (we are not a processor of any user data we cannot already inspect on the user's own machine).
- If we add SimCoach-managed billing or cloud sync (phase 2+), we will publish a privacy policy first.
