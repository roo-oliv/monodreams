# debug — overview

Optional, opt-in visual debug overlays: collider outlines, sprite bounds, periodic backbuffer screenshots — plus `SystemProfiler`, per-system ms/frame accounting driven by `MONODREAMS_PROFILE=1`. Every system in this module respects a flag so it can be muted without removing it from the pipeline. The structured `Logger` and input-replay scaffold live in `foundation` (production-useful); this module adds only the *visual* overlays and screenshot capture. Safe to install in any game — registering none of its systems incurs zero cost.

## Purpose

When debugging an ECS game, the visible bug ("the player passes through walls") rarely tells you where in the pipeline the cause lives. Visual overlays — collider outlines, sprite bounds, pivot points — turn "why doesn't this collide" into "the collider is offset two pixels from the sprite." Combined with `input_replay.json`-driven runs and periodic screenshots, this module makes integration tests reproducible by visual diff. The module is fully opt-in: a game that doesn't need debug overlays simply doesn't register the systems, and the module adds no runtime cost. Production and release builds typically omit the registrations entirely (or leave `IsEnabled = false` flags).

## What ships

### Systems

- `ColliderDebugSystem(world, camera)` — creates ephemeral mesh entities each frame outlining every collider (AABB for `BoxColliderComponent`, polygon for `ConvexColliderComponent`). Static `Enabled` flag + instance `IsEnabled` flag for muting
- `SpriteDebugSystem(world, camera)` — same pattern for sprite bounds + pivot points. Reads `DrawComponent` data after `SpritePrepSystem` runs
- `ScreenshotCaptureSystem(world, graphicsDevice, debugDir)` — writes PNG of backbuffer every 2 seconds when enabled. Off by default; gated by `IsEnabled` (typically set from `"screenshots": true` in `input_replay.json`)

Both overlay systems draw through the standard `DrawComponent` path (transient `Type = Mesh` entities), not via parallel `SpriteBatch` calls — they ride `MasterRenderSystem` like everything else.

### Profiling

- `SystemProfiler` — per-system ms/frame accounting. Not a pipeline system: it is a static that plugs into `foundation`'s socket, `GatedSystem.TimingSink`. Setting `SystemProfiler.Enabled` (hosts read `MONODREAMS_PROFILE=1` at boot) installs `SystemProfiler.Record` as that sink; every pipeline entry is gate-wrapped, so one seam times every screen's pipelines, and rows are labelled with the entry's full registration name from `EditorPipelineRegistrar` (`logic.game`, `logic.game.enemies`). With profiling off nothing is installed and the cost is one null check per gated system per frame.

## Pipeline wiring

This module is **safe to install and register nothing**. No mandatory wiring; every consumer is opt-in.

When you do want overlays:

1. **`ColliderDebugSystem`** and **`SpriteDebugSystem`** — register inside the prep stage, after `SpritePrepSystem` (so sprite bounds reflect the current frame's `DrawComponent` data) and before `MasterRenderSystem` (so the transient mesh entities they create exist when the renderer iterates).
2. **`ScreenshotCaptureSystem`** — register anywhere in the screen pipeline (typically at the tail). Set `screenshotSystem.IsEnabled = replayPlan?.Screenshots ?? false` after constructing it to honor the replay-file opt-in.
3. **`SystemProfiler`** — nothing to register. Wire it in the *host*: `SystemProfiler.Enabled = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_PROFILE") == "1";` at boot, then `SystemProfiler.CountFrame();` + `SystemProfiler.ReportPeriodically(state, ref timer);` in `Update` (see `MonoDreams.Demos/Game1.cs`). Every `ReportInterval` seconds (2 by default) a `[perf]` table is written through `Logger`.

**Replay testing workflow.** Write `debug/input_replay.json` with `"screenshots": true`, run the game (or `dotnet run -- --headless`), check `debug/` for the resulting screenshots + log. The `MONODREAMS_DEBUG_DIR` env var redirects all debug output to a custom path — `GameTestRunner` uses this for parallel test isolation.

See `docs/CORE_TENETS.md` (debug section) and `MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs` for the canonical replay-and-screenshot workflow.

## Cross-module dependencies

- `rendering` — overlays draw through `DrawComponent` and `MasterRenderSystem`; screenshots capture the backbuffer.
- `collision` — `ColliderDebugSystem` reads `BoxColliderComponent` and `ConvexColliderComponent` to know what to outline.
- `foundation` — `SystemProfiler` plugs into `GatedSystem.TimingSink` and reports through `Logger`. The arrow points this way only: `foundation` defines the socket and never references this module.

## Extension points

- **New debug overlays.** Follow the pattern of the existing two systems: create transient `DrawComponent { Type = Mesh }` entities each frame at a high `LayerDepth` (so they render on top), dispose them at the start of the next frame, and ship a static `Enabled` flag plus an instance `IsEnabled` flag. Never call `SpriteBatch` directly.
- **HUD overlays (FPS, entity count, draw call count).** Same pattern with `DrawComponent { Type = Text }` on a HUD target. Aspirational direction list.
- **Capture-on-exit screenshot.** Mode where `ScreenshotCaptureSystem` guarantees one final PNG at game shutdown — useful for replay post-mortems. Aspirational direction list.

## See also

- [Premises](premises.md) — load-bearing invariants (opt-in nothing required, overlays via same `DrawComponent` path, must run after prep + before render, `ScreenshotCaptureSystem` gated by replay-file flag, `MONODREAMS_DEBUG_DIR` env-var override, the profiler's injected-sink direction + its `[perf]` format contract)
- Related modules: `rendering` (overlays ride its draw stack), `collision` (provides the collider components `ColliderDebugSystem` visualizes), `foundation` (provides `Logger` and the replay scaffold — the *non-visual* debug infrastructure that lives there because it's production-useful)
