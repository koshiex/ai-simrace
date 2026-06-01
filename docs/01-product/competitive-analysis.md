# Competitive Analysis — AI Sim Racing Coaches

**As of**: 2026-06-01.
**Method**: web research, vendor docs, Reddit r/simracing, ACC forums, product comparisons.

---

## Feature Matrix

| Product | Sims | Real-time voice | Telemetry | Setups | Language | Price |
|---|---|:-:|:-:|:-:|---|---|
| **Coach Dave Delta 5.5** | iRacing, ACC, LMU, AC Evo, AC, AMS2, GT7 | — | yes | yes (auto-install) | EN | ~£15/mo |
| **Garage 61** | iRacing | — | yes | shared | EN | €5/mo Pro |
| **Track Titan** | iRacing, ACC, AC, F1, Forza, LMU | — | yes (post-lap) | yes | EN | ~$8/mo |
| **Trophi.ai** (Driver61) | iRacing, ACC, F1 24/25, LMU, Rocket League | **yes** | yes | optional | 59 langs incl. RU (TTS) | $16.66+/mo |
| **VRS** | iRacing only | — (human coaches) | yes | data packs | EN | tiered |
| **MoTeC i2** | ACC, iRacing, many | — | yes (deep) | — | EN | free/pro |
| **PitGPT** (RACEMAKE) | iRacing, ACC + | — | yes (chat) | — | EN | tiered |
| **RaceData.AI** | iRacing, AC, ACC | — | yes | — | EN | freemium |
| **Sim Racing Telemetry** | F1 25, AC, ACC, iRacing+ | — | yes | — | **RU UI** | Steam per-game |
| **vTelemetry PRO** | multi | — | yes (330Hz) | — | EN | paid |
| **RaceCraft.ai / Pro** | iRacing | — (real-time data) | yes | — | EN | freemium |

## Headline Findings

### Trophi.ai is the closest competitor
- Already does real-time voice coaching in Russian (TTS) for ACC + iRacing + LMU + F1 24/25.
- Sells at $16.66/mo (annual) or higher tiers (~$58 for human coach included).
- Russian TTS is mechanical (auto-translated TTS quota, not Russian-first voice work).
- Backed by Mike Winters + Scott Mansell, $3.3M raised — well-funded.
- **Edge to attack**: native-quality Russian voice (Silero v5 + Yandex SpeechKit), Russian-first UX, race-craft, LMU equal-class support, lower price.

### Coach Dave Delta is the analytics king
- Strong at post-session: corner segmented into Braking/Entry/Apex/Exit, video synced to telemetry.
- No voice. No real-time. English only.
- We don't compete head-on — we live in the in-session voice slot, they live in post-session deep dive.

### Garage61 is data-only
- 50M+ shared laps, clean web UI, no coaching.
- Their AI fortunately doesn't exist yet; if they add it, the wedge narrows fast.

### TrackTitan = post-lap text tips
- "Coaching Flows" pick the biggest mistake; users say tips often lack actionable specificity.
- We deliver during the lap, in voice, in Russian — different product even before features.

### PitGPT = conversational chat over recorded telemetry
- Promising but small audience.
- We can later add conversational mode (phase 11) as a free addition to coach.

### Sim Racing Telemetry has RU UI
- But it's a viewer, not an AI coach. Confirms there's a Russian-speaking market segment that already pays for sim racing data tools.

## Top 10 features any modern AI coach needs

1. Multi-sim native ingest (no manual conversion)
2. Delta-to-reference lap (per sector + per corner)
3. Corner phase decomposition (Braking/Entry/Apex/Exit) with time-loss attribution
4. Real-time voice coaching, hands-on-wheel, in user's language
5. Auto-installed pro reference setups (we skip in MVP — phase 9+)
6. Video synced to telemetry overlay (phase 9+)
7. Actionable prescriptive language ("brake 8m later, trail 30% to apex") not vague observations
8. Conversational query interface (phase 11)
9. Tyre/brake temperature traces with degradation modelling
10. Community-shared lap database (phase 10)

## Top 5 market gaps SimCoach exploits

1. **Native Russian voice**. Trophi has Russian TTS but it sounds mechanical. We use Silero v5 (MIT, Russian-first) and Yandex SpeechKit (Russian neural voices) — both purpose-built for Russian.
2. **Race-craft, not just hot-lap pace**. Everyone else optimises lap time. Few help with overtaking, defending, race starts, fuel-saving, tyre management. Phase 9 target.
3. **First-class LMU support**. Trophi has LMU; Delta has LMU. But the toolchains are iRacing-first. SimCoach phase 2 makes LMU a first-class adapter alongside iRacing.
4. **Affordable mid-tier**. Delta £15. Trophi $16+. Garage61 free but no AI. We sit in the middle at 800₽ / ~$8/mo with real AI.
5. **Local-first privacy**. All raw telemetry stays on disk. Only Gold-tier (200–500 token JSON) leaves the machine, and only to the user-chosen LLM provider.

## Drivers' most-valued comparisons (forum consensus)

1. Delta time graph (#1 used view across MoTeC/Delta/Garage61 communities)
2. Speed trace overlay vs reference
3. Brake pressure trace (peak, release rate, trail-brake %, ABS activation)
4. Throttle trace (application point, modulation, full-throttle moment)
5. Steering angle trace (smoothness, fighting the car indicator)
6. Racing line overlay on track map
7. Coasting overlap (long flat sections = braked too early)
8. Tyre/brake temperatures over a lap
9. G-force / lat-acc trace
