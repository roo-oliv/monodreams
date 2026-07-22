---
flow: audio
covers:
  - MonoDreams/audio/**
sensitive: false
---

# Audio playback reconcile

Sound enters the world through exactly two doors, both ending at the same backend seam.
A fire-and-forget one-shot (button click) is a `PlaySoundRequest` message: `AudioSystem`
plays it **at publish time** (synchronously, so a click sounds the frame it happens) and
tracks the handle until the instance finishes, then releases it. Lifecycle playback (a
looping wind ambience, a jukebox track cut mid-play) is an `AudioSourceComponent` on an
entity: game code writes the *desired* `AudioPlaybackState` (`Playing` / `Paused` /
`Stopped`) plus `Volume`/`Pitch`/`Pan` onto the component, and once per update
`AudioSystem` reconciles each source's desired state against its live backend instance —
start, pause, resume, stop, propagate parameter mutations. All backend access goes
through the injected `IAudioPlayer` (handle-based; `InvalidHandle = 0` means "no
playback"); the default `ContentAudioPlayer` loads `SoundEffect`s through the
`ContentManager` (cached per key) and degrades to a permanent silent no-op — one
`Logger.Warning`, no throw — when the machine has no audio backend. The system owns the
instance lifecycle; the player owns the hardware.

## Entities & lifecycle

- **One-shot**: `world.Publish(PlaySoundRequest)` → instance starts immediately →
  `AudioSystem` polls it each update → finished ⇒ released. No entity involved; nothing
  can address it after publish (that is the contract — use a source if you need to).
- **Source**: entity carries `AudioSourceComponent`. Desired-state writes drive the
  per-update reconcile. The system's bookkeeping lives on the component (`Instance`
  handle + `AppliedState`, which distinguishes "paused by us" from "finished
  naturally"). A non-looping source that plays to completion is released and its `State`
  flipped to `Stopped` **by the system** — reality is reflected back onto the component
  so it does not restart next frame.
- **Cut paths** (all immediate): `State = Stopped` (next reconcile), component removal,
  entity disposal — the `SubscribeEntityComponentRemoved` callback covers the last two
  and fires inside the removal, so no instance outlives its entity.
- **Teardown**: `AudioSystem.Dispose` stops everything it started and unsubscribes; the
  injected `IAudioPlayer` is disposed by whoever created it (the screen), after the
  pipeline.

## Invariants

Authoritative list in [`MonoDreams/audio/docs/premises.md`](../../MonoDreams/audio/docs/premises.md);
the ones this flow leans on:

- A source's live instance is tied to its component lifecycle — removal/disposal cuts
  playback immediately, never leaks past the entity.
- Reconcile keeps **one** live instance per source and never restarts a `Playing`
  source that already has one (no per-frame restarts; no infinite one-shot restarts).
- One-shot vs source is one-way-per-use-case: no manager singleton, no third entry
  point.
- Backend absence degrades to a silent no-op with a single warning; a **missing content
  key still fails loud** (`ContentLoadException` propagates — only backend absence is
  downgraded).
- `EditTimeBehavior.Freeze` is the reference edit-mode policy; it freezes
  *reconciliation*, not already-live instances (and not the message subscription or the
  removal callback, which live outside the update seam).
- Web playback unlocks on the first user gesture — the shared web host layer
  (`host.js`) resumes the suspended AudioContext; engine/game code stays
  gesture-unaware.

## Load-bearing quantities

- `Volume` — 0–1, default 1; `Pitch` — −1–1, default 0; `Pan` — −1–1, default 0.
  Clamped in `ContentAudioPlayer`; propagated to the live instance on every reconcile
  (unconditional, idempotent — no applied-params cache).
- Instance handle — positive `int`; `IAudioPlayer.InvalidHandle` (0) = "no playback"
  (the degraded path returns it and the system treats the source as having no
  instance).
- `Loop` and `SoundKey` — **start-time-only**: XNA's `IsLooped` cannot change on a live
  instance and the key is read at `Play`; mutating either on a live source has no
  effect until stop + restart.
- Memory: music lives in RAM (~10 MB per minute of stereo 44.1 kHz) — everything is a
  `SoundEffect` buffer; no streaming path yet.

## Failure modes

- **Restart-per-frame loop** — a reconcile change that re-plays a `Playing` source with
  a live instance turns every loop into a machine-gun stutter; the applied-state
  bookkeeping exists to prevent exactly this and its sibling (a finished one-shot
  restarting forever).
- **Orphaned loop** — instance lifecycle decoupled from the component (e.g. stopping
  only on `State = Stopped` and forgetting the removal path): a screen switch disposes
  the level's entities and an ambience keeps sounding with no handle left to stop it.
- **Silent web** — the AudioContext gesture-resume hook removed from `host.js` (or a
  standalone web head that copies the host wiring without it): every sound on web is
  permanently muted with no exception to debug from.
- **Swallowed typo** — widening the degrade-catch to include `ContentLoadException`
  turns a misspelled sound key into silence instead of a crash; undebuggable.
- **Backend touched from game code** — playing through anything other than the two
  entry points (a second system, a manager) splits the instance bookkeeping across
  owners and drifts from the reconcile semantics.
