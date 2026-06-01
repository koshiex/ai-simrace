# Product Requirements Document — SimCoach

**Status**: Draft v1 (2026-06-01)
**Owner**: solo founder
**Audience**: future contributors, contractors, beta users

---

## 1. Vision

SimCoach is the AI sim racing coach that finally speaks proper Russian. It listens to your game telemetry in real time, knows where you lose time vs. your own personal best, and tells you — by voice and on a minimalist overlay — exactly what to fix in the next corner, the next sector, and the next session.

It is not a replacement for a human coach at the elite tier. It is a friend in your headset who will not yell at you and is always available, for the 99% of sim racers who will never book a human coach.

## 2. Target User

Primary persona — **"Misha, 32, GT3 enthusiast"**:
- 200–2000 hours in ACC, also dabbles in iRacing and LMU
- Russian speaker, has a triple-screen rig and a load-cell pedal set
- Watches Aris/Coach Dave/Mansell YouTube but can't book live coaching
- Wants to break 2:18 at Spa in his Audi R8 GT3 Evo II
- Will pay 500–1500₽/month for a tool that demonstrably saves him 0.5s/lap

Secondary personas:
- Russian-speaking F1 25 player (console-PC bridge — phase 2)
- LMU early adopter looking for any analysis tool
- LFM/SRO esports competitor at amateur tier

## 3. Why now

- **Trophi.ai** has proven the real-time voice-coach category is sellable, but its Russian TTS is mechanical and the product is built English-first.
- **Coach Dave Delta** focuses on post-session and English content.
- **Garage61** is data-only — no AI coaching layer.
- **TrackTitan** is post-lap, not in-corner.
- **No competitor** ships a Russian-first product. There is a wedge here.
- **OpenRouter** + cheap models (Gemini 2.5 Flash, DeepSeek V3.2) make 1-2 ¢/session real-time LLM coaching economically viable.
- **Silero v5** released MIT-licensed Russian TTS that runs on CPU in real time — eliminating the cloud TTS dependency for the offline-first user.

## 4. Competitive Landscape

See `competitive-analysis.md` for full matrix.

Headline gaps SimCoach exploits:
1. Native-quality Russian voice (Silero v5 + optional Yandex SpeechKit premium).
2. Race-craft coaching, not just hot-lap pace (planned phase 9).
3. First-class LMU support (phase 2 instead of phase 5).
4. Affordable mid-tier price (target 500–1500₽/mo, between Garage61 free and Trophi.ai €15/mo).
5. Local-first architecture — telemetry never leaves the machine, only Gold-tier JSON (200–500 tokens).

## 5. Scope

### MVP (8 weeks)
- ACC only
- Russian language only
- 4-layer coaching cadence (in-corner, sector, lap, post-session)
- Own personal best as reference, matched on (track, car, weather)
- Avalonia transparent overlay (delta, sector bars, current tip)
- Silero v5 RU TTS in-process; optional Yandex SpeechKit
- OpenRouter for LLM (Gemini 2.5 Flash + DeepSeek V3.2 by default)
- MCAP capture + Parquet cold storage
- Post-session debrief window with PDF/MD export

### Phase 2 (post-MVP)
- iRacing adapter
- LMU adapter
- F1 25 adapter

### Out of scope (MVP)
- Setup recommendations beyond understeer/oversteer flag
- Race-craft (overtaking, defending, fuel saving)
- Community-shared reference laps
- Mobile companion
- Multi-language UI (English may come in phase 9)
- AC Evo (wait for stable API)

## 6. Success Metrics

| Metric | MVP target | 6-month target |
|---|---|---|
| Lap-time improvement per beta user, week 1 → week 4 | ≥ 0.5 s | ≥ 1.0 s |
| Session retention (% who run ≥ 2nd session) | ≥ 60% | ≥ 75% |
| Voice mute-rate during sessions | ≤ 25% | ≤ 15% |
| Average LLM cost / 30-min session | ≤ $0.05 | ≤ $0.03 |
| Crash-free sessions | ≥ 95% | ≥ 99% |
| Net Promoter Score | n/a | ≥ 40 |

## 7. Monetisation

- **Free tier**: 3 sessions / week, post-session debrief, no real-time voice.
- **Pro tier (target 800₽/mo)**: unlimited sessions, real-time voice, all 4 coaching cadences, Silero TTS, reference lap library.
- **Pro+ tier (1500₽/mo)**: premium Yandex SpeechKit voice, choice of premium LLM models (Sonnet 4.6, Haiku 4.5), priority support.

User brings their own OpenRouter API key in MVP — eliminates billing complexity. Phase 2 introduces SimCoach-managed billing.

## 8. Risks

- **Trophi.ai** improves Russian TTS and closes our wedge → mitigation: ship native-quality Silero now, faster cadence than them.
- **OpenRouter price hikes** → mitigation: support multiple model paths, fall back to rule-engine-only.
- **ACC EOLs / Kunos abandons it** → mitigation: phase 2 adapters reduce single-sim risk.
- **Anti-cheat changes block overlays** → mitigation: no DLL injection, only transparent topmost windows + shared-memory reads (already universally tolerated).
- **Solo dev burnout** → mitigation: ship MVP in 8 weeks, recruit collaborators after first real revenue.

## 9. Open Questions

- Pricing — convert ₽ to $ for international users or RU-only at launch?
- Distribution — direct download or Steam? (Steam takes 30% but lower friction.)
- Anti-cheat dialogue with iRacing — proactive disclosure?
- Russian voice talent — settle for Silero or commission a custom voice?
