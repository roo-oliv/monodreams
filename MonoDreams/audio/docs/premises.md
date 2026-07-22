# audio — premises

> Technical invariants the engine assumes about the audio module:
> `AudioSourceComponent`, `PlaySoundRequest`, `AudioSystem`, and the
> `IAudioPlayer` / `ContentAudioPlayer` seam. Read this before changing any
> of those pieces or any system that starts or stops playback.

## A source's live instance is tied to its component lifecycle

`AudioSystem` cuts a source's live backend instance whenever the desired
`State` becomes `Stopped`, whenever the `AudioSourceComponent` is removed,
or whenever its entity is disposed (the component-removed subscription
covers both). The stop on removal/disposal is immediate (fires inside the
removal callback, not on the next update), so no instance can outlive the
entity that owns it.

**Why:** entities are the lifecycle handle for everything they own; a
looping ambience that keeps sounding after its entity died is an audio
leak with no remaining handle to stop it.
**Breaks:** disposing a level's entities during a screen switch would leave
orphaned loops playing forever, with no component left to reference them.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`SettingStateStopped_CutsTheLiveInstance`,
`RemovingTheComponent_CutsTheLiveInstance`,
`DisposingTheEntity_CutsTheLiveInstance`).
**Depends on:** —

## `AudioSystem` reconciles desired state — one live instance per source, no per-frame restarts

Game code writes the *desired* `AudioPlaybackState` on the component;
`AudioSystem` reconciles it against the live instance once per update. A
`Playing` source with a live instance is never restarted (the system
tracks the applied state to tell "paused by us" apart from "finished
naturally"); a non-looping source that plays to completion is released and
its component flipped to `Stopped` by the system, so it does not restart
on the next frame. Volume/pitch/pan mutations propagate to the live
instance on the next reconcile.

**Why:** the desired-state model is what makes cut/pause/resume expressible
as plain component writes (ECS purity — no methods on components), and the
applied-state bookkeeping is what prevents the two classic failure modes:
restarting a loop every frame, and restarting a finished one-shot forever.
**Breaks:** reconciling naively ("desired Playing + instance not playing →
play") restarts finished sources infinitely; skipping the bookkeeping makes
resume indistinguishable from finished-and-release.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`LoopingSource_StartsOnFirstReconcile_AndKeepsASingleInstanceAcrossFrames`,
`NonLoopingSource_ThatFinishes_FlipsItselfToStopped_AndDoesNotRestart`,
`PausedSource_PausesTheInstance_AndResumeContinuesWithoutRestart`,
`VolumePitchPanMutations_PropagateToTheLiveInstance_OnNextReconcile`).
**Depends on:** —

## One-shot vs source: exactly one idiomatic way per use case

Fire-and-forget playback (button click) is a `PlaySoundRequest` message —
no entity, starts at publish time, instance released automatically after
it finishes. Playback with a lifecycle (loop, cut, pause) is an
`AudioSourceComponent` on an entity. These are the only two entry points;
there is no audio manager singleton and no second way to play a sound.
Simultaneous playback is the default: every source and every one-shot is
its own backend instance with an independent handle.

**Why:** framework rule — one way to do a thing. A component-only API would
force games to create a throwaway entity per click; a message-only API
could not express cut/pause. The message mirrors the
`LoadLevelRequest`/`EntitySpawnRequest` convention.
**Breaks:** a second playback path (manager singleton, parallel system)
would drift from the reconciliation semantics and split the instance
lifecycle bookkeeping across owners.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`PlaySoundRequest_StartsExactlyOneOneShot_WithRequestedParameters`,
`OneShot_PlaysToCompletion_ThenItsInstanceIsReleased`,
`MultipleSourcesAndOneShots_PlaySimultaneously_OnIndependentInstances`).
**Depends on:** —

## `ContentAudioPlayer` degrades to a silent no-op without an audio backend

On a machine with no audio hardware (headless CI, containers), the XNA
audio backend throws on first use (`NoAudioHardwareException`, or
`DllNotFoundException` for a missing native audio library — detected
anywhere in the exception's inner chain). `ContentAudioPlayer` catches
exactly those, logs a **single** `Logger.Warning`, and becomes a permanent
no-op: `Play` returns `IAudioPlayer.InvalidHandle` (0) and every other
call is safe. A missing content key is a developer error and still fails
loud (`ContentLoadException` propagates).

**Why:** `GameTestRunner` and CI spawn the real game on machines without
audio devices; a throw would kill every integration test that happens to
cross a sound trigger. Conversely, swallowing content misses would hide
real bugs — only backend absence is downgraded.
**Breaks:** catching too broadly turns typo'd sound keys into silence
(undebuggable); not catching at all makes every headless run crash on the
first sound.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`ContentAudioPlayerTests.WithoutAudioBackend_PlayDegradesToSilentNoOp_WithASingleWarning`,
`ContentAudioPlayerTests.MissingContentKey_StillFailsLoud`).
**Depends on:** foundation — "`Logger` requires `Initialize` before any write".

## Web playback unlocks on the first user gesture — the shared host layer owns the resume

Browsers start an `AudioContext` created before any user interaction in the
`suspended` state, and a suspended context renders nothing until `resume()`
is called from a gesture handler. KNI's BlazorGL stack (4.2.9001) never
does that: `ConcreteAudioService.Suspend()`/`Resume()` are empty method
bodies, nothing in `Kni.Platform.dll` calls `AudioContext.ResumeAsync()`,
and the `nkast.Wasm.Audio` JS shim is a bare 1:1 WebAudio interop with no
gesture listener (binary inspection, 2026-07-22 — see
`docs/web-targeting.md › Audio`). The shared host page
(`MonoDreams.Web.Hosting/wwwroot/js/host.js`) therefore wraps the shim's
AudioContext factories and resumes any suspended context on the first
`pointerdown`/`keydown`. Engine and game code stay gesture-unaware: sounds
started before the first interaction begin sounding at the gesture.

**Why:** without the host-level resume, every sound on web is permanently
and silently muted — no exception is thrown; the suspended context simply
never renders, which reads as "audio module broken on web".
**Breaks:** removing the hook from `host.js` — or shipping a standalone web
head that copies the host wiring without it — mutes all web audio with no
error to debug from.
**Tests:** browser-only behaviour, not headlessly testable (per plan
contract 12): the KNI-side evidence is binary inspection recorded in
`docs/web-targeting.md`, and "first click unlocks audio in Chrome" is an
explicit manual item in the PR test plan.
**Depends on:** foundation — "The platform (backend + OS services) is
selected by the head project, never by engine source" (the hook lives in
web host infrastructure, not engine source).

## `EditTimeBehavior.Freeze` is the reference edit-mode policy — it freezes reconciliation, not playback

Audio is game logic: in edit-capable screens the single `AudioSystem` is
registered with `EditTimeBehavior.Freeze`, so it stops reconciling while the
editor is in `Edit` (the reference registration is the module demo,
`MonoDreams/audio/demo/AudioDemoScreen.cs`). Freeze gates only the per-frame
reconcile, which bounds exactly what it can and cannot stop: an already-live
instance keeps sounding in Edit (an ambient loop keeps playing — the gate
skips the system's update, it cannot reach into the backend); a
`PlaySoundRequest` published in Edit still plays immediately (one-shots are
message-driven, not update-driven — in practice publishers are themselves
Freeze-gated game systems, so nothing publishes); and a lifecycle cut
(component removed / entity disposed) still stops the instance immediately
(the removal subscription is not gated).

**Why:** the run-state contract (CORE_TENETS §9) freezes game logic in `Edit`
so it does not act out from under the designer; audio follows the same
policy. The gate operates at the update seam, so everything the system does
outside `Update` — the message subscription, the removal callback —
deliberately stays live: cutting a disposed entity's loop must not wait for
Play mode.
**Breaks:** registering with `RunNormally` makes source edits audible
mid-editing (a designer flipping `State` in the Inspector starts playback in
Edit); conversely, expecting Freeze to silence a live loop files as a bug
("audio keeps playing in Edit") when it is the documented boundary of an
update-seam gate.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`FreezeGatedInEdit_SkipsReconciliation_ButAlreadyLiveInstancesKeepPlaying`);
the reference Freeze registration is exercised end-to-end by
`MonoDreams.Tests/IntegrationTests/HeadlessAudioDemoTests.cs`.
**Depends on:** foundation — "Edit-time behaviour is a per-system policy
honoured by `GatedSystem`".

## Known limitations (acknowledged gaps)

- **Music lives in RAM** — no streaming path yet; `Song`/`MediaPlayer` was
  deliberately rejected (global single-track singleton, a second way to
  play audio). ~10 MB per minute of stereo 44.1 kHz. Streaming is a named
  follow-up.
- **`Loop` is start-time only** — XNA's `SoundEffectInstance.IsLooped`
  cannot change after `Play()`; mutating `AudioSourceComponent.Loop` on a
  live source has no effect until it is stopped and restarted.
- **`SoundKey` is start-time only** — same reconcile rule: changing the key
  mid-play does not swap the sound; stop and restart.

## Aspirational direction

- Streaming music backend (an `IAudioPlayer` implementation over
  `Song`/`MediaPlayer` or a custom decoder) for long tracks.
- Audio buses / mixer categories (SFX vs Music master volume); XNA's
  global `SoundEffect.MasterVolume` exists natively today.
