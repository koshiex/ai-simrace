# ADR-0005: Silero v5 RU via ONNX Runtime, in-process

**Status**: Accepted
**Date**: 2026-06-01

## Context

We need a Russian TTS that:
- Sounds natural (driver listens for an hour at a time).
- Starts the first audio frame within ~200 ms of text being ready.
- Doesn't share GPU with the game (game owns the GPU).
- Costs as close to zero as possible per session.
- Handles racing-specific terms ("апекс", "трейл-брейкинг", "торможение").

Candidates: Silero v5, Yandex SpeechKit v3, Microsoft Azure, ElevenLabs Flash v2.5, OpenAI gpt-4o-mini-tts, Piper (Ruslan), SberDevices Salute, Vosk-TTS.

## Decision

- **Primary**: **Silero v5 `v5_ru`** loaded into the SimCoach process via `Microsoft.ML.OnnxRuntime`. CPU-only inference. MIT-licensed. RTF 0.06–0.3 on 4 threads. First audio frame ≤ 150 ms typical.
- **Premium (configurable)**: **Yandex SpeechKit v3** bidirectional gRPC `StreamSynthesis` with voice `filipp` or `alena`. User already has Yandex Cloud quota.
- **Skip**: Piper RU (broken phonemizer, GitHub issue #771), OpenAI TTS (weak RU per January 2026 reports), ElevenLabs v3 (no real-time RU streaming).

## Why

- Silero v5 supports SSML and stress marks (`торм+оз`) which we leverage in the voice template.
- CPU-only inference frees the GPU entirely for the game.
- ONNX bundle ships with the installer (~50 MB additional payload, acceptable under our 200 MB cap).
- Yandex SpeechKit gives the user a one-click upgrade path to higher quality.

## Tradeoffs

- Silero v5 lacks the per-voice fine-tuning ElevenLabs offers — we use the default speakers.
- ONNX inference on CPU consumes ~1 core during synthesis. Acceptable; bursty workload.

## Consequences

- `Voice.SileroOnnxSynthesizer` is the default `ITtsBackend`.
- `Voice.YandexSpeechKitClient` ships as an alternate `ITtsBackend` selectable from settings.
- Audio output pipeline (priority queue + WASAPI shared) is identical for both backends.
- Phonetic preprocessing (stress-mark insertion for foreign terms) lives in `Voice.RuPhonetics`.
