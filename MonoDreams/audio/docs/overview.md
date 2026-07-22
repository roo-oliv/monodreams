# audio — overview

SFX and music playback built on the XNA `SoundEffect` / `SoundEffectInstance` API, which works identically on MonoGame DesktopGL (desktop) and KNI/BlazorGL (web, WebAudio buffers). One component, one message, one system, one seam — every playback need in a 2D game maps to exactly one idiomatic entry point.

## Purpose

This module is the engine's source of sound. It covers the four playback shapes a game needs:

| Use case | Entry point |
|---|---|
| One-shot fullplay (button click) | publish `PlaySoundRequest` — fire-and-forget |
| Interruptible playback (jukebox cut mid-play) | `AudioSourceComponent`; set `State = Stopped`, remove the component, or dispose the entity |
| Repeating loop (wind ambience) | `AudioSourceComponent` with `Loop = true` |
| Multiple simultaneous audios | every source and one-shot is its own backend instance with an independent handle |

## What ships

### Components

- `AudioSourceComponent` — entity-owned playback with a lifecycle: `SoundKey`, `Loop`, `Volume`, `Pitch`, `Pan`, desired `State` (`Playing` / `Paused` / `Stopped`), plus system-managed runtime fields (`Instance`, `AppliedState`)

### Messages

- `PlaySoundRequest` — one-shot playback: `SoundKey`, `Volume`, `Pitch`, `Pan`. Starts at publish time; the instance is released automatically when it finishes

### Systems

- `AudioSystem` — the single audio system. Subscribes to `PlaySoundRequest` (one-shots) and reconciles every `AudioSourceComponent`'s desired state against its live instance each update: start / pause / resume / stop, propagate volume/pitch/pan mutations, release finished instances. Component removal or entity disposal cuts playback immediately

### Seam

- `IAudioPlayer` — the playback seam (`Play → handle`, `Stop`, `Pause`, `Resume`, `SetVolume/SetPitch/SetPan`, `IsPlaying`). Unit tests inject a fake; games normally never touch it
- `ContentAudioPlayer` — default implementation backed by `ContentManager.Load<SoundEffect>` (cached per key). Degrades to a silent no-op (single `Logger.Warning`) when the machine has no audio backend — headless/CI safety

## Pipeline wiring

```csharp
var audioPlayer = new ContentAudioPlayer(content);
pipeline.Add(new AudioSystem(world, audioPlayer));
```

Position in the pipeline does not matter for correctness (the system only talks to the audio backend), but registering it after game logic means same-frame state changes are heard the frame they happen. In edit-capable screens register it with `EditTimeBehavior.Freeze` — audio is game logic.

## Cross-module dependencies

- `foundation` — `GameState` (the system's update contract) and `Logger` (the degraded-mode warning).

## Extension points

- **Custom backends.** Implement `IAudioPlayer` (streaming, a mixer, a different engine) and hand it to `AudioSystem` — the reconciliation logic is backend-agnostic.
- **Game-side triggers.** Publish `PlaySoundRequest` from any game system (collision handlers, UI interaction systems); no audio-module changes needed.

## Known limitations (v1)

- **Long music is held in RAM** (~10 MB per minute of stereo 44.1 kHz): everything goes through `SoundEffect`, including music. Streaming via `Song`/`MediaPlayer` was deliberately not used (global single-track singleton, second-way-to-do-it) — a streaming path is a named follow-up.
- **`Loop` applies at start only** — XNA's `IsLooped` cannot change on a live instance. Stop and restart the source to change it.

## See also

- [Premises](premises.md) — load-bearing invariants for this module
- Related modules: `foundation` (GameState, Logger)
