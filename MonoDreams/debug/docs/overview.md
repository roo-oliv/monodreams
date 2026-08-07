# debug — overview

Optional, opt-in visual debug overlays: collider outlines, sprite bounds, periodic backbuffer screenshots — plus `SystemProfiler`, per-system ms/frame accounting driven by `MONODREAMS_PROFILE=1`. Every system in this module respects a flag so it can be muted without removing it from the pipeline. The structured `Logger` and input-replay scaffold live in `foundation` (production-useful); this module adds only the *visual* overlays and screenshot capture. Safe to install in any game — registering none of its systems incurs zero cost.

## Purpose

When debugging an ECS game, the visible bug ("the player passes through walls") rarely tells you where in the pipeline the cause lives. Visual overlays — collider outlines, sprite bounds, pivot points — turn "why doesn't this collide" into "the collider is offset two pixels from the sprite." Combined with `input_replay.json`-driven runs and periodic screenshots, this module makes integration tests reproducible by visual diff. The module is fully opt-in: a game that doesn't need debug overlays simply doesn't register the systems, and the module adds no runtime cost. Production and release builds typically omit the registrations entirely (or leave `IsEnabled = false` flags).

## What ships

### Systems

- `ColliderDebugSystem(world, camera)` — creates ephemeral mesh entities each frame outlining every collider (AABB for `BoxColliderComponent`, polygon for `ConvexColliderComponent`). Static `Enabled` flag + instance `IsEnabled` flag for muting
- `SpriteDebugSystem(world, camera)` — same pattern for sprite bounds + pivot points. Reads `DrawComponent` data after `SpritePrepSystem` runs
- `ScreenshotCaptureSystem(graphicsDevice, captureIntervalSeconds, outputDirectory, format, maxFrames)` — dumps the composited backbuffer to the debug dir. Two formats (`CaptureFormat.Png` / `CaptureFormat.Raw`), an interval gate, an optional frame cap, an `AnnotateFilename` hook (PNG only), and `CaptureNow(gameTime)` for a synchronous, deterministic single frame. Off by default; gated by `IsEnabled` (typically set from `"screenshots": true` in `input_replay.json`). `ScreenshotCaptureSystem.FromEnvironment(graphicsDevice, outputDirectory)` builds the instance the environment asked for, or `null` — see [Frame capture](#frame-capture)

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

## Frame capture

`ScreenshotCaptureSystem` has two jobs that look alike and are not: **verification** (did the renderer put the right pixels on the screen?) and **recording** (what did the last twenty seconds look like?). Both are served by the same system, in two formats, and the environment picks which.

### The env contract

`ScreenshotCaptureSystem.FromEnvironment(graphicsDevice, outputDirectory)` is the **only** reader of these variables. It returns a ready-to-run instance (`IsEnabled = true`) or `null` when the environment asked for nothing — a host wires it as `_capture = ScreenshotCaptureSystem.FromEnvironment(GraphicsDevice, debugDir);` and then calls `_capture?.Update(state)` after the frame is composited. See `MonoDreams.Demos/Game1.cs` for the reference wiring.

| Variable | Values | Effect |
|---|---|---|
| `MONODREAMS_SCREENSHOT` | *unset*, `0`, `off` | No capture; `FromEnvironment` returns `null`. |
| | `1`, `png` | `CaptureFormat.Png` — one encoded PNG per capture, default interval **0.5 s**. |
| | `raw`, `rgba` | `CaptureFormat.Raw` — one uncompressed RGBA8888 blob per frame, default interval **0** (every frame). |
| | anything else | `Logger.Error` naming the valid modes, then `null`. An unrecognised mode never silently degrades to "capture something". |
| `MONODREAMS_SCREENSHOT_INTERVAL` | seconds, invariant-culture float, `>= 0` | Overrides either format's interval. `0` means every frame. A value that doesn't parse (or is negative) is ignored and the format default stands. |
| `MONODREAMS_SCREENSHOT_MAX_FRAMES` | positive integer | Stop capturing after n frames, logging `Frame capture stopped at the n-frame cap (… MiB written)`. Unset/`0` means no cap. |

Output goes to the capture system's `outputDirectory`, which every host resolves from `MONODREAMS_DEBUG_DIR` (falling back to `<BaseDirectory>/debug`) — so a capture lands in the same scratch directory as the run's log, and a parallel test run never collides with another.

File names are self-describing, so the directory needs no manifest that can fall out of step with it:

- PNG — `screenshot_{counter:D6}_gt{seconds:F2}[_{annotation}]_{wallclock}.png`. The `AnnotateFilename` hook (a `Func<string>`, evaluated per capture) injects the optional middle part — camera position/zoom, say — so an agent reading the shots can map pixels to world space without reverse-engineering the view. **PNG only:** raw names deliberately carry geometry and time and nothing else.
- Raw — `raw_{counter:D6}_{width}x{height}_{gametimeMs:D8}.rgba`. Game time is an **integer millisecond count**, not a formatted float: a decimal separator is culture-dependent and would make any indexing tool machine-specific.

### The disk cost, which is the whole reason for the frame cap

| Resolution | Bytes/frame | At 60 fps | 20-second take |
|---|---|---|---|
| 1280x720 RGBA8888 | 3.5 MiB | ~220 MB/s | ~4.2 GB |

`MONODREAMS_SCREENSHOT_MAX_FRAMES` is therefore **not optional decoration** — it is the safety valve. A forgotten raw capture fills a disk inside a minute, and the mode says so as it goes (a progress line every 120 frames, and a frame/byte summary on `Dispose`). A write failure — the shape a full disk actually takes — stops the capture and logs the totals rather than throwing per frame for the rest of the run. Capture to a scratch dir, assemble, delete.

### Why raw exists, and why encoded clips do not

**Raw is the verification tool.** Its properties are the ones verification needs and encoding destroys:

- **Exact pixels.** No lossy step, no encoder heuristics — the bytes in the file are the bytes the GPU produced, so a test can assert on them.
- **Deterministic cost.** A memcpy per frame instead of a ~50 ms encode. That matters more than it sounds: under MonoGame's fixed timestep a draw that overruns its frame budget makes the host run about four simulation updates per draw, so PNG-per-frame capture doesn't just *record* slowly, it makes the captured game *behave* differently (~15–26 fps of capture over a game running in fast-forward). Raw sustains ~59.8 fps at 1280x720, so the recording is of the game the player would have played.
- **Scriptable indexing.** Game time in the filename means a tool can seek "the frame at 1.25 s" without decoding anything or trusting a sidecar.

**Encoded clip capture is deliberately out of scope.** The engine does not ship an mp4/gif writer, and the omission is a boundary, not a gap: verification and sharing are different jobs, and conflating them would put a codec's timing behaviour inside the loop being verified. When a clip is what you want, the platform already has one:

- **Desktop** — pipe the raw frames to the **ffmpeg binary MGCB already bundles**, on a background thread. The engine's job ends at producing exact frames at full rate; muxing them is a tooling concern outside the frame loop.
- **Web** — `canvas.captureStream()` + `MediaRecorder`. The browser encodes the canvas natively; an engine-side encoder would be strictly worse and would need a filesystem the platform doesn't have (which is also why the Demos host builds its env capture under `#if !MONODREAMS_WEB`).

The documented recipe for the desktop half — parsing the raw filename contract and assembling the frames into an animated GIF, including the three artefacts (fractional-factor shimmer, per-frame palette crawl, index decimation) that make a naive assembler look wrong — is [`docs/recipes/frames-to-gif.md`](../../../docs/recipes/frames-to-gif.md).

## Cross-module dependencies

- `rendering` — overlays draw through `DrawComponent` and `MasterRenderSystem`; screenshots capture the backbuffer.
- `collision` — `ColliderDebugSystem` reads `BoxColliderComponent` and `ConvexColliderComponent` to know what to outline.
- `foundation` — `SystemProfiler` plugs into `GatedSystem.TimingSink` and reports through `Logger`. The arrow points this way only: `foundation` defines the socket and never references this module.

## Extension points

- **New debug overlays.** Follow the pattern of the existing two systems: create transient `DrawComponent { Type = Mesh }` entities each frame at a high `LayerDepth` (so they render on top), dispose them at the start of the next frame, and ship a static `Enabled` flag plus an instance `IsEnabled` flag. Never call `SpriteBatch` directly.
- **HUD overlays (FPS, entity count, draw call count).** Same pattern with `DrawComponent { Type = Text }` on a HUD target. Aspirational direction list.
- **Capture-on-exit screenshot.** Mode where `ScreenshotCaptureSystem` guarantees one final PNG at game shutdown — useful for replay post-mortems. Aspirational direction list.

## See also

- [Premises](premises.md) — load-bearing invariants (opt-in nothing required, overlays via same `DrawComponent` path, must run after prep + before render, `ScreenshotCaptureSystem` gated by replay-file flag, `FromEnvironment` as the single owner of the capture env contract, raw capture's synchronous zero-allocation write, `MONODREAMS_DEBUG_DIR` env-var override, the profiler's injected-sink direction + its `[perf]` format contract)
- Related modules: `rendering` (overlays ride its draw stack), `collision` (provides the collider components `ColliderDebugSystem` visualizes), `foundation` (provides `Logger` and the replay scaffold — the *non-visual* debug infrastructure that lives there because it's production-useful)
