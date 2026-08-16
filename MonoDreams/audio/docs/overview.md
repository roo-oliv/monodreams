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
- `ContentAudioPlayer` — default implementation backed by `ContentManager.Load<SoundEffect>` (cached per key). Degrades to a silent no-op (single `Logger.Warning`) when the machine has no audio backend — headless/CI safety. Its `Preload(IEnumerable<string>)` warms that cache from a loading moment, so no `Play` of a successfully warmed key pays the disk-read + PCM decode mid-frame (an unwarmed game hitches once per distinct sound, which reads as a gameplay bug). Warming is an optimisation and never a gate: a key that fails to load is logged and skipped, staying on the lazy path in `Play`, and backend absence short-circuits the whole warm. `Preload` is deliberately not on `IAudioPlayer` — it's a content-pipeline concern of this implementation, not part of the playback seam

## Pipeline wiring

```csharp
var audioPlayer = new ContentAudioPlayer(content);
pipeline.Add(new AudioSystem(world, audioPlayer));
```

Registering the system is the load-bearing act: forget it and every `PlaySoundRequest` publishes into a world with no listener while every `AudioSourceComponent` sits inert — a silently mute game, with no exception and no log line to debug from (see the premises; DefaultEcs exposes no subscriber count, so there is no runtime tripwire for it). Position in the pipeline does not matter for correctness (the system only talks to the audio backend), but registering it after game logic means same-frame state changes are heard the frame they happen. In edit-capable screens register it with `EditTimeBehavior.Freeze` — audio is game logic. Note the Freeze boundary: it stops *reconciliation*, not already-live instances (an ambient loop keeps sounding in Edit) — see the premises for the full contract. The reference registration is the module demo (`demo/AudioDemoScreen.cs`).

## Recommended wiring: one `AudioCueSystem`, zero `Play` calls in gameplay

Publishing `PlaySoundRequest` is legal from anywhere, but the wiring this module is *designed*
for — and the one that keeps audio disposable — is a **single game-side cue system that
subscribes to the gameplay messages the game already publishes** and maps them to sounds.
Gameplay systems keep publishing exactly what they published before; none of them mentions
audio. It is the film-scoring model: the composer watches the finished cut, the actors never
play instruments on set.

```csharp
/// Game-side (not part of the module): the ONLY place in the game that knows
/// what things sound like. Subscribes to gameplay messages that already exist
/// and turns them into PlaySoundRequests.
public sealed class AudioCueSystem : ISystem<GameState>
{
    public bool IsEnabled { get; set; } = true;

    private readonly World _world;
    private readonly List<IDisposable> _subscriptions = [];

    public AudioCueSystem(World world)
    {
        _world = world;
        // Messages the game/engine already publishes for their own reasons.
        _subscriptions.Add(world.Subscribe<UIFocusActivated>(OnActivated));
        _subscriptions.Add(world.Subscribe<CollisionMessage>(OnCollision));
        // …and the game's own: PaperStamped, DrawerOpened, …
    }

    private void OnActivated(in UIFocusActivated msg) =>
        _world.Publish(new PlaySoundRequest("Sounds/click", 0.6f));

    private void OnCollision(in CollisionMessage msg)
    {
        if (msg.Type != CollisionType.Collectible) return;
        _world.Publish(new PlaySoundRequest("Sounds/pickup"));
    }

    // Nothing per-frame: cues are message-driven. (An entity-scoped cue — an ambience
    // that follows a machine — is an AudioSourceComponent this system adds instead.)
    public void Update(GameState state) { }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
    }
}
```

Register it like any other game system, with `Freeze` in edit-capable screens (cues are game
logic):

```csharp
p.Add("audio.cues", new AudioCueSystem(_world), EditTimeBehavior.Freeze);
p.Add("audio", new AudioSystem(_world, _audioPlayer), EditTimeBehavior.Freeze);
```

Order relative to `AudioSystem` does not matter for one-shots: `PlaySoundRequest` is handled by
the subscription `AudioSystem`'s constructor opens, synchronously at publish time, so a cue
sounds the instant it is published no matter which system ran first. It matters only for the
entity-scoped case — an `AudioSourceComponent` a cue adds or mutates is picked up by the next
`AudioSystem.Update`, so registering the cue system ahead of it starts that source the same
frame instead of the next one.

Why this shape rather than a `Play` call inside each gameplay system:

- **Audio is a pure observer.** Mute the game, retheme it, or delete the audio module
  entirely, and no gameplay system changes — you delete one system and its registration.
- **Every sound is in one file.** "Which sounds does this game make, and when?" is answered by
  reading `AudioCueSystem`, not by grepping for `PlaySoundRequest` across the codebase.
- **The trigger already exists.** A gameplay system that publishes a message for its own
  reasons needs no audio-shaped edit; if a cue has no message to hang on, that is usually a
  missing gameplay message rather than a reason to reach for the audio API in place.
- **Testing stays honest.** Gameplay tests never touch the audio seam, and cue mapping is
  testable on its own: publish the gameplay message into a `World` and assert the
  `PlaySoundRequest` that comes out.

The shipped reference game (*NFs, Please!*) ran this way end to end: one cue system, zero `Play`
calls inside gameplay systems.

## Cross-module dependencies

- `foundation` — `GameState` (the system's update contract) and `Logger` (the degraded-mode warning).

## Extension points

- **Custom backends.** Implement `IAudioPlayer` (streaming, a mixer, a different engine) and hand it to `AudioSystem` — the reconciliation logic is backend-agnostic.
- **Game-side triggers.** Publish `PlaySoundRequest` from game code; no audio-module changes needed. Route them through one `AudioCueSystem` subscribed to the gameplay messages you already publish rather than sprinkling publishes through gameplay systems — see *Recommended wiring* above.

## Known limitations (v1)

- **Long music is held in RAM** (~10 MB per minute of stereo 44.1 kHz): everything goes through `SoundEffect`, including music. Streaming via `Song`/`MediaPlayer` was deliberately not used (global single-track singleton, second-way-to-do-it) — a streaming path is a named follow-up. Before building one, read the premises entry "`MediaPlayer` is one stream": a failed `Song` load must stop the stream, or a failed track swap resurrects the previous track.
- **`Loop` applies at start only** — XNA's `IsLooped` cannot change on a live instance. Stop and restart the source to change it.

## Demo

`demo/AudioDemoScreen.cs` (installed with `--with-demo`, registered in the Demos launcher) exercises every playback shape at once: one-shot click on key press, a toggleable looping wind ambience, and a jukebox riff started and cut mid-play — all mixing simultaneously. A frame-scripted boot sequence plays each case once and logs every start/stop, which is what the headless integration test (`HeadlessAudioDemoTests`) asserts on.

## See also

- [Premises](premises.md) — load-bearing invariants for this module
- [`docs/web-targeting.md`](../../../docs/web-targeting.md) › Audio — the browser autoplay unlock and the web content-build shim
- Related modules: `foundation` (GameState, Logger)
