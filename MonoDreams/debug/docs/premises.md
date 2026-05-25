# debug — premises

> Technical invariants the engine assumes about the debug overlays
> block: `ColliderDebugSystem`, `SpriteDebugSystem`, and
> `ScreenshotCaptureSystem`. (The `Logger` and the input-replay
> scaffold live in `foundation` because they're useful in production;
> this block adds the *visual* debug overlays and screenshot capture
> only.) Read this before changing any of those pieces or relying on
> the screenshot output for testing.

## This block is opt-in; nothing requires it

A screen's pipeline assembly never *needs* a debug system. Each system
in this block is registered only if the screen explicitly wants it,
and each respects a static `Enabled` flag plus an instance `IsEnabled`
flag so a registered system can be muted without removing it from the
pipeline. Tests and production-bound builds simply omit the
registrations.

**Why:** the framework-not-library tenet says required behavior lives
in `foundation`, optional behavior in its own block. Debug overlays
allocate per-frame (transient mesh entities), so making them mandatory
would impose a cost on every screen.
**Breaks:** if a future refactor moves any of these systems into a
required block, every game pays for debug rendering whether they
asked for it or not. The static `Enabled` toggles also become global
state that's harder to keep off across compositions.
**Tests:** none yet.
**Depends on:** —

## Debug overlays draw via the same `DrawComponent` path as everything else

`ColliderDebugSystem` and `SpriteDebugSystem` create ephemeral entities
with `DrawComponent { Type = Mesh }` and a high `LayerDepth` (e.g.
1.0, 0.98) each frame, then dispose them at the start of the next
frame. They do not call `SpriteBatch` directly and they do not fork
`MasterRenderSystem`.

**Why:** the rendering pipeline's "`MasterRenderSystem` is the sole
renderer" premise forbids a second SpriteBatch path. Generating
transient mesh entities is the engine-pure way to render overlays.
The high layer depth keeps them on top of the rest of the scene
without needing a separate target.
**Breaks:** if a debug system bypassed `MasterRenderSystem` to draw
directly, the next render-target switch would lose the overlay (the
SpriteBatch would be in the wrong state) — and the "no parallel
renderer" review lens would flag the PR.
**Tests:** none yet.
**Depends on:** rendering — "`MasterRenderSystem` is the sole renderer";
rendering-mesh — "Mesh entities use the same `DrawComponent` slot".

## Debug overlays must be prep'd before `MasterRenderSystem` runs

`ColliderDebugSystem` and `SpriteDebugSystem` register inside the
prep block (between `SpritePrepSystem` and `MasterRenderSystem` in the
reference pipeline at
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`). They
must run before `MasterRenderSystem` so the transient mesh entities
are in the world when the renderer iterates draw queries; they should
run after `SpritePrepSystem` so the sprite bounds they visualize
reflect the same frame's `DrawComponent` data.

**Why:** the overlay entities have no draw data until the debug system
creates them. If `MasterRenderSystem` runs first, the entities aren't
in the world yet and the overlays render one frame late. Likewise,
`SpriteDebugSystem` reads `DrawComponent.Position` / `Origin` / `Size`
to compute bounds; reading before `SpritePrepSystem` writes those gives
stale data.
**Breaks:** overlays flicker, lag, or visualize last frame's bounds
during camera motion or animation.
**Tests:** none yet.
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## `ScreenshotCaptureSystem` is gated by `IsEnabled` set from `input_replay.json`

`ScreenshotCaptureSystem` is disabled by default. The integration test
pattern is to write `"screenshots": true` into `debug/input_replay.json`
and have the screen set `screenshotSystem.IsEnabled = replayPlan?.Screenshots
?? false` after constructing the system. Production builds either omit
the registration or leave `IsEnabled = false`.

**Why:** capturing PNGs of the backbuffer every 2 seconds allocates,
hits the disk, and stalls the render thread briefly. Default-off keeps
the cost out of any session that didn't explicitly ask for it. The
replay file is the natural carrier of "this run wants screenshots"
intent — it already lives in the debug dir.
**Breaks:** a screenshot system that captured unconditionally would
slow every run by a few % and fill the debug directory with stale
PNGs. The opt-in pattern keeps the system free in normal use.
**Tests:** none yet (the integration-test screens follow the pattern,
but no test asserts that `IsEnabled = false` produces no PNG output).
**Depends on:** foundation — "`Logger` requires `Initialize` before any
write" (screenshots respect `MONODREAMS_DEBUG_DIR` the same way logs
do — both default to `debug/` next to the executable, and both honor
the env-var override).

## Debug output respects `MONODREAMS_DEBUG_DIR`

`ScreenshotCaptureSystem` is constructed with an `outputDirectory`
parameter; the reference screen reads `MONODREAMS_DEBUG_DIR` from the
environment and falls back to `<AppDomain.BaseDirectory>/debug`. The
same convention lets `GameTestRunner` redirect a parallel test run's
screenshots into its own scratch directory without colliding with
others.

**Why:** parallel test execution is the load-bearing case. Without an
override, every concurrent test would write into the same `debug/`
folder, clobbering each other's PNGs and logs.
**Breaks:** if a new debug system is added that hardcodes `debug/`
instead of honoring the env var, parallel tests intermittently fail
with file-locking errors or with screenshots from the wrong test
attributed to the wrong run.
**Tests:** none yet (every `GameTestRunner` test depends on this
indirectly).
**Depends on:** foundation — "`Logger` requires `Initialize` before any
write".

## Open questions

- **`ColliderDebugSystem` / `SpriteDebugSystem` two-toggle design** —
  both have a static `Enabled` flag *and* the standard `IsEnabled`
  instance flag. The intended split (compile-time global vs runtime
  per-instance?) isn't documented. May simplify to one toggle.
- **Capture interval as a per-screen setting** — currently hardcoded
  at the system's construction (2 seconds in the reference screen).
  No reason the interval couldn't read from `input_replay.json` too.

## Aspirational direction

- Debug HUD overlay (FPS, entity count, draw call count) as another
  opt-in system in this block, sharing the same transient-entity
  pattern.
- A `--capture-on-exit` mode for `ScreenshotCaptureSystem` that
  guarantees one final PNG at game shutdown, useful for replay
  post-mortems.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- This block is opt-in; nothing requires it
- Debug overlays draw via the same `DrawComponent` path as everything else
- Debug overlays must be prep'd before `MasterRenderSystem` runs
- `ScreenshotCaptureSystem` is gated by `IsEnabled` set from `input_replay.json`
- Debug output respects `MONODREAMS_DEBUG_DIR`
