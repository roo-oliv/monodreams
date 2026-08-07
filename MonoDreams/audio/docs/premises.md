# audio — premises

> Technical invariants the engine assumes about the audio module:
> `AudioSourceComponent`, `PlaySoundRequest`, `AudioSystem`, and the
> `IAudioPlayer` / `ContentAudioPlayer` seam. Read this before changing any
> of those pieces or any system that starts or stops playback.

## A source's live instance is tied to its component lifecycle

`AudioSystem` cuts a source's live backend instance whenever the desired
`State` becomes `Stopped`, whenever the `AudioSourceComponent` is removed,
whenever its entity is disposed (the component-removed subscription covers
both), or whenever the component is **overwritten** via `entity.Set(new
AudioSourceComponent(...))` on an entity that already has one — DefaultEcs
fires `ComponentChanged` (never `Removed`) for an overwrite, so the system
also subscribes to component-changed and cuts the discarded old value's
instance there. The stop on removal/disposal/overwrite is immediate (fires
inside the callback, not on the next update), so no instance can outlive
the entity — or the component value — that owns it.

**Why:** entities are the lifecycle handle for everything they own; a
looping ambience that keeps sounding after its entity died is an audio
leak with no remaining handle to stop it.
**Breaks:** disposing a level's entities during a screen switch would leave
orphaned loops playing forever, with no component left to reference them;
likewise, handling only `Removed` would let a jukebox-style track swap
(`entity.Set(new AudioSourceComponent("track2", ...))`) orphan the old
track's loop — the live handle sits on the discarded component value.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`SettingStateStopped_CutsTheLiveInstance`,
`RemovingTheComponent_CutsTheLiveInstance`,
`DisposingTheEntity_CutsTheLiveInstance`,
`OverwritingTheComponentViaSet_CutsTheOldValuesLiveInstance`).
**Depends on:** —

## `AudioSystem` reconciles desired state — one live instance per source, no per-frame restarts

Game code writes the *desired* `AudioPlaybackState` on the component;
`AudioSystem` reconciles it against the live instance once per update. A
`Playing` source with a live instance is never restarted (the system
tracks the applied state to tell "paused by us" apart from "finished
naturally"); a non-looping source that plays to completion is released and
its component flipped to `Stopped` by the system, so it does not restart
on the next frame. Volume/pitch/pan mutations propagate to the live
instance on the next reconcile — including while the source is paused (a
paused instance is live, only silent; a pause-menu volume slider must not
wait for resume).

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
`VolumePitchPanMutations_PropagateToTheLiveInstance_OnNextReconcile`,
`VolumePitchPanMutations_PropagateWhilePaused_WithoutRePausing`).
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
loud (`ContentLoadException` propagates). A full voice pool
(`InstancePlayLimitException` — the backend's cap on simultaneous
instances, 256 in MonoGame 3.8) is a **transient** mixing condition, not
backend absence: `Play` drops just that voice (`InvalidHandle`,
`Logger.Debug`, the failed instance disposed — never leaked) and the
player stays enabled for future calls.

**Why:** `GameTestRunner` and CI spawn the real game on machines without
audio devices; a throw would kill every integration test that happens to
cross a sound trigger. Conversely, swallowing content misses would hide
real bugs — only backend absence is downgraded.
**Breaks:** catching too broadly turns typo'd sound keys into silence
(undebuggable); not catching at all makes every headless run crash on the
first sound; letting the voice cap propagate crashes the game loop on the
257th simultaneous voice, while treating it as backend absence would
permanently silence the game after one loud mix.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`ContentAudioPlayerTests.WithoutAudioBackend_PlayDegradesToSilentNoOp_WithASingleWarning`,
`ContentAudioPlayerTests.MissingContentKey_StillFailsLoud`,
`ContentAudioPlayerTests.VoiceCapReached_DropsTheVoice_WithoutDisablingThePlayer`).
**Depends on:** foundation — "`Logger` requires `Initialize` before any write".

## `Preload` is an optimisation, never a gate

`ContentAudioPlayer.Preload(IEnumerable<string>)` decodes sound keys into the
same `_sounds` cache `Play` reads, so no `Play` of a successfully warmed key
ever pays the disk read plus PCM decode mid-frame (a failed or omitted key
stays on the lazy path). It is meant to be called from a loading moment, where a
hitch is invisible — the reference warm is the module demo, which preloads its
three keys next to its font load (`MonoDreams/audio/demo/AudioDemoScreen.cs`).
Every failure mode of the warm is non-fatal by construction: a key that will
not load is logged (`Logger.Warning`) and **skipped**, which leaves it uncached
so `Play` behaves exactly as it did before — including still failing loud
there, where a content miss is a developer error. Backend absence
(`NoAudioHardwareException` / `DllNotFoundException` anywhere in the chain)
short-circuits the entire warm through the same `Disable` path `Play` uses, so
a deviceless machine spends nothing. Already-cached keys are skipped, and a
warm that runs to completion closes with a `Logger.Info` summary — an
already-disabled player, or a backend failure mid-warm, returns before it.
`Preload` lives on `ContentAudioPlayer` only, **not** on `IAudioPlayer`:
warming is a content-pipeline concern of this implementation, not part of the
playback seam.

**Why:** an unwarmed game stutters once per distinct sound (38 ms in one frame
for the first sound ever played, which also pays audio-backend spin-up), and
because it only ever happens the first time it reads as a gameplay bug rather
than a load. Conversely, letting the warm throw would trade a cosmetic hitch
for a refusal to boot, and letting it absorb the miss would move a developer
error out of `Play`, the one place it is diagnosable.
**Breaks:** making a warm failure fatal turns one absent effect into a crash at
startup; caching a sentinel — or otherwise recording a failed key as
"attempted" — silences the content miss `Play` is supposed to raise, converting
a loud developer error into the module's worst failure mode, silence; hoisting
`Preload` onto `IAudioPlayer` forces every backend (a streaming player, a test
fake) to implement a `SoundEffect`-cache concept it does not have.
**Tests:** `MonoDreams.Tests/Audio/AudioSystemTests.cs`
(`ContentAudioPlayerTests.PreloadedKeys_NeverTouchTheLoaderInPlay`,
`ContentAudioPlayerTests.PreloadFailingKey_WarnsAndSkips_WithoutAbortingTheWarm_AndStaysOnTheLazyPath`,
`ContentAudioPlayerTests.BackendAbsenceDuringWarm_ShortCircuitsAndDisables_SoHeadlessWarmIsANoOp`).
**Depends on:** this file — "`ContentAudioPlayer` degrades to a silent no-op
without an audio backend" (the warm reuses its `Disable` path and its
fails-loud-on-content-miss rule); foundation — "`Logger` requires `Initialize`
before any write".

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

## An unregistered `AudioSystem` is a silently mute game

Nothing in this module sounds until `AudioSystem` is registered in a screen's
update pipeline: its **constructor** is what subscribes to `PlaySoundRequest`
and what opens the `AudioSourceComponent` set. A screen that composes the
components and publishes the messages but never adds the system is not broken
in any observable way — `World.Publish` on a message nobody listens for returns
normally, sources sit at `State = Playing` with `Instance = null` forever, and
there is no exception, no log line, not even the degraded-mode warning a
missing audio device would produce. Registration is the load-bearing act; the
reference registration is the module demo (`MonoDreams/audio/demo/AudioDemoScreen.cs`,
`p.Add("audio", new AudioSystem(_world, _audioPlayer), EditTimeBehavior.Freeze)`).
A registered-but-disabled system (`IsEnabled = false`) is a partial form of the
same trap — one-shots still sound, because the message subscription is not
gated by `IsEnabled`, while every `AudioSourceComponent` goes silent (the same
update-seam boundary the Freeze premise below documents).

No runtime tripwire exists to catch this, and none can be added honestly.
DefaultEcs 0.18.0-beta01's public pub/sub surface is exactly `Publish<T>` /
`Subscribe<T>` (on `World` and `IPublisher`, plus the attribute-driven
`IPublisherExtension.Subscribe` helpers): there is no subscriber count, no
"has listeners" query, and no way to enumerate subscriptions — the lists live
in private fields of an undocumented internal `Publisher<T>`. The engine does
not own the publish call either: game code writes
`world.Publish(new PlaySoundRequest(...))` directly, so counting consumers
would mean routing one-shots through an engine-owned wrapper — a second way to
play a sound, which the entry-point premise above forbids. This premise is the
guard.

**Why:** it cost the shipped reference game's developer a debugging session. A
game that is *entirely* silent looks like a content, asset, or audio-hardware
problem, and each of those is investigated before "the system is missing from
the pipeline" — which is why the absence has to be documented where a wiring
mistake is made rather than detected where it manifests.
**Breaks:** any screen that wires `AudioSourceComponent`s or publishes
`PlaySoundRequest` without adding `AudioSystem` to its update pipeline — or
that drops it while reordering one. The whole game is mute with zero
diagnostics to work back from.
**Tests:** none yet, and not testable from inside the module.
`MonoDreams.Tests/IntegrationTests/HeadlessAudioDemoTests.cs` exercises the
*registered* path, which by construction cannot catch a missing registration;
an assertion would have to live in screen composition, which the module does
not own (CORE_TENETS §2 — the screen owns pipeline assembly, and a system makes
no assumption about what else is registered).
**Depends on:** —

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

## `MediaPlayer` is one stream — a failed `Song` load must `Stop()` or the old track resurrects

Forward-looking: the engine has no music system today (`Song`/`MediaPlayer` was
deliberately rejected for this module — see *Known limitations* below), so this
invariant is addressed to whoever builds the named streaming-backend follow-up.
XNA's `MediaPlayer` is a global, single-stream singleton: one song at a time,
which means a crossfade between tracks is necessarily a volume ramp on that one
stream — the outgoing track is turned **down**, never stopped. So a track swap
whose new `Song` fails to load must call `MediaPlayer.Stop()` in its catch
block, not merely log and return. The stream still holds the previous track at
a faded-down level, and the fade logic — running under a system that has
already recorded the swap as done — ramps it back **up**: the failed swap
resurrects the track it was replacing. The `Stop()` goes inside its own
`try`/`catch`, because it throws when there is nothing to stop or no audio
device at all, and a missing music track must never become a crash. Silence is
what a failed track has to sound like.

**Why:** a real defect in the shipped reference game's `MediaPlayer`-backed
music player, where it surfaced as the *previous* area's music playing in the
new area — an audio bug that presents as a level/state bug and gets
investigated as one. The one-stream shape is what makes "do nothing on failure"
different from "produce silence"; the per-instance `SoundEffect` mixer this
module is built on has no such coupling, which is exactly why the trap is
invisible to anyone reasoning from the current module.
**Breaks:** any streaming/music backend whose failed-load path returns without
stopping the stream: every failed swap leaves the old track audible while its
owning system believes the new one is playing, so the *next* swap crossfades
out of a track that is not the one it thinks it is. An unguarded `Stop()`
trades the resurrection for a crash on the deviceless machines the rest of this
module goes out of its way to survive.
**Tests:** none yet (no music system in the engine; invariant for the named
streaming-backend follow-up).
**Depends on:** this file — "`ContentAudioPlayer` degrades to a silent no-op
without an audio backend" (a music backend inherits the same
never-crash-without-a-device obligation).

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
