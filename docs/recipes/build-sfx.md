# Recipe — deterministic sfxr-style sound effects

> Synthesise a game's sound effects as short 16-bit mono WAVs from a script, so
> the origin is known, no third-party licence attaches to the audio, and
> re-running the script produces byte-identical files.

Placeholder audio has a sourcing problem, not a quality problem. "Only free
sounds, and I need to know the origin so I can credit it" is a constraint that
costs hours per sound to satisfy from asset packs, and third-party terms can
change after shipping. Generating the sounds answers it completely and
permanently: **the origin is the script**, there is nothing to attribute, and no
external terms attach to the generated audio. (The adapted script itself is
vendored code like any other — it keeps whatever terms its source grants.) It also makes every sound *tweakable* — if the
coin is too bright, edit a number and re-run, which is not true of a downloaded
clip.

The output is placeholder-grade by design. Licensed or recorded material sounds
richer and is the right end state for the sounds that matter; this recipe is how
you get a complete, credited soundscape on day one of a jam and replace it
selectively later.

## The mechanism

The synthesis is deliberately **sfxr-shaped** — the palette a pixel-art game
wants — and small enough to read in one sitting:

- **Short envelopes over square / triangle / sine / noise, with pitch sweeps.** A
  rising sweep reads as gain, a falling one as impact. Filtered noise with a
  moving cutoff reads as a *material* (wood, cloth, stone) rather than as static.
- **A per-sound recipe is a function** `fn(i, n) -> [-1, 1]`, composed from
  primitives (`tone_sweep`, `noise_burst`, `sine`, `mix`) and rendered by one
  `render(name, seconds, fn, volume)` helper that applies the shared envelope,
  clips, and writes the WAV.
- **Nothing longer than ~0.3 s.** These play constantly; length is what turns a
  sound effect into an irritation.
- **16-bit mono at 22050 Hz.** Plenty for short blips and a quarter the size of
  44.1 kHz stereo — which matters here, because the audio module holds
  everything in RAM as `SoundEffect` (see below).

**Determinism is the load-bearing property.** The noise generator is a tiny LCG
**seeded per sound**, so "noise" is reproducible run to run: on the same
toolchain, two runs produce byte-identical WAVs and the repo sees no churn. (The
LCG is pure integer math; the tone generators go through `math.sin` — the
platform's libm — so byte-identity across *machines* holds in practice but is
only guaranteed per toolchain. A quick hash comparison settles it where it
matters.) Nothing reads the clock, unseeded randomness, or any input beyond the
script itself. That is what makes the files safe to commit and the script safe
to re-run.

**One guard worth copying: a do-not-overwrite list.** Once a synthesised
placeholder has been replaced by a licensed or recorded clip, re-running the
script would overwrite audio that cost money and a licence to obtain, replace it
with a beep, and **report success**. The reference tool keeps a `LICENSED` set of
names that `render` refuses, printing why and pointing at the attribution file
that records the replacement. Add a name to that set the moment you replace a
sound.

**No dependencies.** The reference script uses only the Python standard library
(`math`, `struct`, `wave`) — unlike the other recipes here, there is no Pillow or
numpy to install.

## Feeding the engine

The output is plain `.wav` — the source asset the content pipeline builds into
the `SoundEffect` the [`audio`](../../MonoDreams/audio/docs/overview.md) module
consumes at runtime. The module is
`SoundEffect`-only — one XNA API that behaves identically on MonoGame DesktopGL
and KNI/BlazorGL — and reaches it through `ContentAudioPlayer`, the default
`IAudioPlayer` backed by `ContentManager.Load<SoundEffect>` and cached per key.

1. **Add each WAV to `Content.mgcb`** with the sound-effect importer/processor:

   ```text
   #begin Sounds/hit.wav
   /importer:WavImporter
   /processor:SoundEffectProcessor
   /processorParam:Quality=Best
   /build:Sounds/hit.wav
   ```

2. **The content key is the sound key.** `Sounds/hit.wav` → `Sounds/hit`, which
   is what you pass the module:

   ```csharp
   // one-shot, fire and forget
   world.Publish(new PlaySoundRequest("Sounds/hit", 0.6f));

   // entity-owned playback with a lifecycle (loops, interruptible sources)
   entity.Set(new AudioSourceComponent("Sounds/wind", loop: true, volume: 0.3f));
   ```

3. **Register `AudioSystem`, or the game is silently mute** — every
   `PlaySoundRequest` publishes into a world with no listener and every
   `AudioSourceComponent` sits inert, with no exception and no log line:

   ```csharp
   var audioPlayer = new ContentAudioPlayer(content);
   pipeline.Add(new AudioSystem(world, audioPlayer));
   ```

4. **Warm the cache from a loading moment** —
   `audioPlayer.Preload(new[] { "Sounds/hit", "Sounds/coin", … })` — so no first
   `Play` pays the disk read + PCM decode mid-frame. An unwarmed game hitches once
   per distinct sound, which reads as a gameplay bug.

Two module facts that shape the recipe: everything (music included) is held in
RAM as `SoundEffect` — roughly 10 MB per minute of 44.1 kHz stereo — so short
22 kHz mono blips are the cheap case and long tracks are a known limitation; and
in edit-capable screens `AudioSystem` is registered `EditTimeBehavior.Freeze`,
since audio is game logic. See the module's
[premises](../../MonoDreams/audio/docs/premises.md) for the full contracts.

**On the web head**, a `.wav` in the `.mgcb` means the KNI content build shells
out to `ffmpeg`/`ffprobe`, which the KNI builder package does not ship for any
OS — the build fails with `Failed to open file <name> … not DRM protected` until
they are staged. See
[`docs/web-targeting.md`](../web-targeting.md) § "macOS / Linux native-lib shim",
step 5.

## Usage sketch

```bash
# No dependencies — stdlib only.
python3 tools/build-sfx.py
# writing MyGame.Core/Content/Sounds
#   coin.wav  120ms
#   hit.wav   100ms
#   land.wav   80ms
#   chest.wav  SKIPPED — a licensed clip owns this sound (see Content/ATTRIBUTION.md)
# done — add each to Content.mgcb (SoundEffect)
```

Editing a sound is editing one line in the script and re-running:

```python
# The hit: a hard, falling crack. Noise for the impact, a dropping square
# for the weight.
render("hit", 0.10, mix(noise_burst(23, tilt=0.5), tone_sweep(440, 150, 0.5)),
       volume=0.30)
```

Re-run, listen, adjust. Because the noise seed is fixed, the only thing that
changed between two runs is the number you edited.

## Adapt these

- **The output directory** — the reference tool hardcodes that game's
  `Gmtk2026.Core/Content/Sounds` relative to the repo root. Point it at your
  content tree.
- **The sound list itself** — the recipes in `main()` are that game's vocabulary
  (`coin`, `chest`, `swing`, `hit`, `hurt`, `land`, `enemy-death`). Keep the
  primitives, replace the list. The names become your content keys.
- **The `LICENSED` set** — starts empty for a new game; add a name the moment a
  real clip replaces a placeholder, and record the replacement wherever you keep
  attribution.
- **`RATE` (22050) and the per-sound `volume`s** — mixing levels are per-game;
  the quietest sound should be the one that plays most often.
- **The noise seeds** — any fixed integers work. What matters is that they are
  fixed.

## Reference implementation

[`tools/build-sfx.py`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/build-sfx.py)
in `roo-oliv/gmtk-2026gj` (recipe validated against commit `26d3729`; the link
tracks `main`).

This is the implementation to **copy and adapt**, not a dependency to install.
The engine deliberately does not vendor the script: a game's sound vocabulary,
mixing levels and content layout are game-specific, so tooling of this shape
ships as a documented recipe that each game owns a tuned copy of. What to change
is listed under "Adapt these" above.
