# debug — overview

Optional, opt-in visual debug overlays: collider outlines, sprite bounds, periodic backbuffer screenshots — plus `SystemProfiler`, per-system ms/frame accounting driven by `MONODREAMS_PROFILE=1`. Every system in this module respects a flag so it can be muted without removing it from the pipeline. The structured `Logger` and input-replay scaffold live in `foundation` (production-useful); this module adds only the *visual* overlays and screenshot capture. Safe to install in any game — registering none of its systems incurs zero cost.

## Purpose

When debugging an ECS game, the visible bug ("the player passes through walls") rarely tells you where in the pipeline the cause lives. Visual overlays — collider outlines, sprite bounds, pivot points — turn "why doesn't this collide" into "the collider is offset two pixels from the sprite." Combined with `input_replay.json`-driven runs and periodic screenshots, this module makes integration tests reproducible by visual diff. The module is fully opt-in: a game that doesn't need debug overlays simply doesn't register the systems, and the module adds no runtime cost. Production and release builds typically omit the registrations entirely (or leave `IsEnabled = false` flags).

## What ships

### Systems

- `ColliderDebugSystem(world, camera)` — creates ephemeral mesh entities each frame outlining every collider (AABB for `BoxColliderComponent`, polygon for `ConvexColliderComponent`). Static `Enabled` flag + instance `IsEnabled` flag for muting
- `SpriteDebugSystem(world, camera)` — same pattern for sprite bounds + pivot points. Reads `DrawComponent` data after `SpritePrepSystem` runs
- `ScreenshotCaptureSystem(graphicsDevice, captureIntervalSeconds, outputDirectory, format, maxFrames, captureTarget)` — dumps the composited frame to the debug dir: the window backbuffer by default, or a named `RenderTargetID` at its own fixed resolution (`captureTarget`). Two formats (`CaptureFormat.Png` / `CaptureFormat.Raw`), an interval gate, an optional frame cap, an `AnnotateFilename` hook (PNG only), and `CaptureNow(gameTime)` for a synchronous, deterministic single frame. Off by default; gated by `IsEnabled` (typically set from `"screenshots": true` in `input_replay.json`). `ScreenshotCaptureSystem.FromEnvironment(graphicsDevice, outputDirectory)` builds the instance the environment asked for, or `null` — see [Frame capture](#frame-capture)

Both overlay systems draw through the standard `DrawComponent` path (transient `Type = Mesh` entities), not via parallel `SpriteBatch` calls — they ride `MasterRenderSystem` like everything else.

<<<<<<< HEAD
- `PointerReplaySystem(world, plan, camera, viewportManager, requestExit)` / `PointerReplaySystem.TryLoad(debugDir, world, camera, viewportManager, requestExit)` — **scripted mouse replay**: drives a `PointerReplayPlan` (`debug/pointer_replay.json`) of `move` / `click` / `wheel` / `type` / `waitUntil` / `label` commands by injecting into the real `CursorInputComponent`. `TryLoad` returns `null` without the file. See [Pointer replay](#pointer-replay)
||||||| 342dba6
=======
### Unattended runs

- `KeepAwake.FromEnvironment()` — opt-in (`MONODREAMS_KEEP_AWAKE=1`) macOS power-management assertion, held by the returned token for as long as the host keeps it. Not a system: hosts call it once at boot and dispose it at shutdown. `null` when the environment did not ask, and a logged no-op off macOS — see [Unattended runs and the sleep footgun](#unattended-runs-and-the-sleep-footgun)
>>>>>>> origin/main

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

## Pointer replay

`input_replay.json` speaks a **gamepad** vocabulary — named actions (`Jump`, `Grab`, `Interact`). It can drive a platformer and it can say nothing at all to a business sim, a card game, a menu or an editor: there is no way to express *"move to (960, 610) and click"*. `PointerReplaySystem` is the pointer half of the same idea, and it shares the input replay's philosophy exactly: **file-gated** (no `pointer_replay.json` → no driver → a normal run is unchanged), **deterministic** (frame-counted, never wall-clock), **fully logged** (every command writes a `[pointer]` line), and **auto-exiting** when the plan drains.

### The plan file

`debug/pointer_replay.json` (relocated with the rest of the debug output by `MONODREAMS_DEBUG_DIR`):

```json
{
  "description": "buy the first item in the shop",
  "tailFrames": 5,
  "commands": [
    { "kind": "waitUntil", "entity": "ShopPanel", "timeoutFrames": 300 },
    { "kind": "label",     "text": "shop-open" },
    { "kind": "move",      "x": 960, "y": 610 },
    { "kind": "click" },
    { "kind": "waitUntil", "log": "Purchase confirmed", "timeoutFrames": 120 },
    { "kind": "label",     "text": "purchased" },
    { "kind": "wheel",     "delta": -240 },
    { "kind": "type",      "text": "rodrigo" }
  ]
}
```

| Kind | Fields | Frames | Notes |
|---|---|---|---|
| `move` | `x`, `y` | 1 | Authoring-space (virtual-resolution) coordinates. |
| `click` | `x`, `y`, `button`, `hold` | `hold` + 1 | Optional move first; `button` is `left` (default) / `right` / `middle`; the button stays down `hold` frames (default 1), so a press-edge consumer AND a release-edge consumer both see it. |
| `wheel` | `delta` | 1 | Raw wheel units — 120 per detent, the value consumers divide by. |
| `type` | `text` | 2 per character | Synthesized key presses, one every two frames so an edge-triggered reader sees repeats. `a-z`, `0-9`, space. |
| `waitUntil` | one of `entity` / `log` / `frames`, plus `timeoutFrames` | until satisfied | The stage gate. `entity` = an entity whose `EntityInfoComponent` type/name matches exists; `log` = an **unconsumed** log line contains this substring; `frames` = idle. Times out (default 600) with an `ERROR` line and continues. |
| `label` | `text` | 1 | A stage marker in the log, so screenshots and log lines correlate per stage. |

`waitUntil` is the load-bearing command, not decoration: without stage gating a script races the game it is driving ("don't click Submit until the dialog exists") and flakes.

**A `log` wait consumes the line it matched.** The driver keeps a watermark over its log ring and moves it past every line a wait matches, so in `[click Save, waitUntil "Scene saved", click Save, waitUntil "Scene saved"]` the second wait gates on the *second* save instead of passing instantly on the first one's line (which would fire the second click ungated — the exact race the command exists to remove). The watermark is **not** reset when a wait starts: the line a wait gates on is usually written by the command before it, downstream of the driver in a frame that has already finished, so a start-of-wait snapshot would skip precisely the line being waited for.

### Wiring it into a screen

```csharp
var cursorInput    = new CursorInputSystem(world, viewportManager);
var cursorPosition = new CursorPositionSystem(world, camera, viewportManager);

var pointer = PointerReplaySystem.TryLoad(debugDir, world, camera, viewportManager, requestExit: game.Exit);
if (pointer != null)
{
    cursorInput.SkipHardwareRead = true;    // the hardware read must not overwrite the injection
    cursorPosition.SkipDerivation = true;   // …and the derivation must not recompute over it
}

p.Add("input", cursorInput);
if (pointer != null) p.Add("pointerReplay", pointer);   // immediately after the input stage
// … game / UI systems read the injected cursor exactly as they read a real one …
p.Add("cursorPosition", cursorPosition);
```

`MonoDreams.Examples.Core/Screens/LevelSelectionScreen.cs` is the reference wiring, and `MonoDreams.Tests/IntegrationTests/PointerReplayTests.cs` drives it end to end (a scripted click on a menu button really changes screens).

For typed text, hand the driver's synthesized keyboard to any system that takes the repo's keyboard seam:

```csharp
var textInput = new TextInputSystem(world) { KeyboardStateProvider = pointer.ReadKeyboard };
```

### Why it injects instead of simulating

The driver writes the same `CursorInputComponent` a real mouse fills — position, button levels, the press/release edges, the scroll accumulator — and places the cursor entity through `Cursor.ApplyPose`, the same per-render-target pose rule `CursorPositionSystem` applies. Everything downstream in the game (hover, picking, UI focus, buttons, scroll views) therefore runs unchanged. A driver that instead called `button.OnClick()` would verify the driver, not the game.

Coordinates are **authoring space**, never window pixels, for two reasons: a script then survives a resize or a resolution change, and a headless host (whose backbuffer is 1x1) has no meaningful window-to-virtual mapping to invert. World coordinates are derived from the authored ones through the screen's camera.

The one cursor field that is *not* in that space is `ScreenPosition`, which is **backbuffer pixels** by contract — `CursorInputSystem` scales the raw OS mouse by `ViewportManager.DevicePixelRatio` precisely to keep it there. So the driver maps the authored point forward through `ViewportManager.ScaleVirtualToScreenCoordinates` (the exact inverse of the mouse mapping) instead of writing an authoring-space number into a device-pixel field. Pass the screen's viewport manager or that mapping is the identity, which is only true when the two spaces already coincide.

**What a pointer plan cannot reach.** Because an authored point is virtual-space, it maps *inside the game viewport* by construction — the editor shell's chrome (toolbar, panels, tabs) lives in the inset margins and is hit-tested in screen space, so it is not addressable from a pointer plan at all. That is deliberate, not a gap to fill here: scripting the editor's own controls is `EditorOpReplaySystem`'s job, by action name (see below).

### From a test

`GameTestRunner.RunAsync(plan, pointerPlan: …)` drops the JSON into the run's isolated debug dir, so a pointer scenario is written in C# and asserted through the usual log helpers.

### Relationship to the editor-op channel

`level-editor`'s `EditorOpReplaySystem` is a *different* channel with an overlapping mechanism: it scripts **editor operations** (transport Play/Pause/Restart, toolbar and palette actions) and moves the cursor as a means to that end. `PointerReplaySystem` is the game-facing one — any screen, any module, pointer only. Run one channel per session: two drivers stamping the same cursor entity fight, and the menu screen logs a warning when it sees both.

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
| `MONODREAMS_SCREENSHOT_TARGET` | *unset*, `window` | Read the window backbuffer — the default, and today's behaviour byte for byte. |
| | `Main`, `UI`, `HUD`, `Scroll`, `Editor` | Read that `RenderTargetID`'s target instead, at **its own fixed resolution** (case-insensitive). See [Capturing a render target](#capturing-a-render-target-instead-of-the-window). |
| | anything else | `Logger.Error` naming the valid values, then `null`. Names only: `0` is not an alias for `Main`. |

Both the mode and the source are recorded on the run's own init line — `ScreenshotCaptureSystem initialized. Format: Raw, interval: every frame, source: Scroll render target, output: …` — because a directory of frames does not otherwise say what it is a picture OF.

Output goes to the capture system's `outputDirectory`, which every host resolves from `MONODREAMS_DEBUG_DIR` (falling back to `<BaseDirectory>/debug`) — so a capture lands in the same scratch directory as the run's log, and a parallel test run never collides with another.

File names are self-describing, so the directory needs no manifest that can fall out of step with it:

- PNG — `screenshot_{counter:D6}_gt{seconds:F2}[_{annotation}]_{wallclock}.png`. The `AnnotateFilename` hook (a `Func<string>`, evaluated per capture) injects the optional middle part — camera position/zoom, say — so an agent reading the shots can map pixels to world space without reverse-engineering the view. **PNG only:** raw names deliberately carry geometry and time and nothing else.
- Raw — `raw_{counter:D6}_{width}x{height}_{gametimeMs:D8}.rgba`. Game time is an **integer millisecond count**, not a formatted float: a decimal separator is culture-dependent and would make any indexing tool machine-specific.

### Capturing a render target instead of the window

A backbuffer capture is a photograph of a *window*: resize the window, run on a machine with a
smaller display, or let the aspect-fit letterbox change, and the same game frame comes out at a
different size, so every pixel coordinate an agent noted in one run means nothing in the next. The
engine already renders into fixed-resolution targets before it composites them, so
`MONODREAMS_SCREENSHOT_TARGET=<id>` reads one of *those* instead:

- **The geometry is the target's**, always. It does not follow the window size, a mid-run resize, or
  the letter/pillarbox — the two are no longer related at all. (The UI demo shows this at its
  sharpest: its `Scroll` target is 360x220 while the backbuffer is 1280x720.)
- **One layer, not the composite.** `UI` alone, `HUD` alone — an assertion about UI pixels stops
  depending on what the world drew behind them.
- **No window management is involved.** There is nothing to pin, freeze, or restore; a game that
  wants a fixed-size capture does not have to give up a resizable window to get one.

How the target is found is worth knowing, because nothing registers it: screens own their targets
privately. Each `MasterRenderSystem` pass announces `(source id, destination)` through
`MasterRenderSystem.RenderedTargetSink` — a null-by-default socket in `rendering` that this module
plugs into *only* when a target was named — so the passes that actually ran this frame are the
lookup. No screen has to announce a teardown: a resolved target that has since been **disposed**
(a screen switch, or the window resize that makes the editor chrome rebuild its target) loses to
the next pass's target, and the read path drops it rather than reading it dead — that check is the
whole invalidation protocol, and it is what keeps an interval capture, which reads its target many
frames after resolving it, alive across a switch. When a screen runs several passes for one id
(the camera demo renders `Main` twice, world then minimap), the first live pass of the frame wins,
which is the primary one every screen composites first.

Two practical notes:

- The capture must run **after the composite**, as it always had to — `FinalDrawSystem` is what
  unbinds the target, and a bound target cannot be read back.
- If the current screen has no pass for the requested id, nothing is captured and the log says so
  once (`no render pass has drawn the … target`). Capture resumes by itself if a screen with that
  pass loads. Refusing beats guessing: a file at the wrong geometry looks right and compares with
  nothing.

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

The documented recipe for the desktop half — parsing the raw filename contract and assembling the frames into an animated GIF, including the three artefacts (fractional-factor shimmer, per-frame palette crawl, index-decimation judder) that make a naive assembler look wrong — is [`docs/recipes/frames-to-gif.md`](../../../docs/recipes/frames-to-gif.md).

## Unattended runs and the sleep footgun

A run nobody is watching is not left alone by the operating system. On **macOS** two mechanisms take
it away:

- **App Nap** throttles a process whose windows are hidden or occluded — which is every headless run
  by construction (`HeadlessWindow.Hide`).
- **Display / idle sleep** suspends the app entirely once the machine dozes off.

The observed failure is not a crash and not a log line: the process simply stops making progress. A
three-hour agent run was found hung inside `Cocoa_GL_SwapWindow` — blocked mid-present, with the last
captured frame dating from the moment the display went to sleep. Every artefact of the run looks
fine; there is just nothing after a certain timestamp.

`MONODREAMS_KEEP_AWAKE=1` removes it for that run: the host holds an `NSProcessInfo` activity
(`NSActivityUserInitiated | NSActivityIdleDisplaySleepDisabled`) for its whole lifetime — the
in-process equivalent of leaving `caffeinate -disu` running beside the game — and releases it at
shutdown. Both lines are in the run's log (`Keep-awake: NSProcessInfo activity held` / `… released`),
and while it runs, `pmset -g assertions` lists the process by the reason string. Values: `1`/`true`/
`on` to hold it, unset/`0`/`off` for nothing, anything else is a logged error and no assertion.

**What the flag does NOT cover**, and what to do instead:

| Platform | Status | Do this instead |
|---|---|---|
| macOS | Covered by the flag | — |
| Windows | Not covered | Adjust the power plan, or hold `SetThreadExecutionState` in your own host |
| Linux | Not covered | `systemd-inhibit --what=idle` around the run, or a desktop-session inhibitor |
| Web (KNI/BlazorGL) | Not coverable from engine code | Browser throttling of a background tab is not overridable; the Screen Wake Lock API needs a user gesture and does not stop tab throttling. Keep the tab foregrounded |

And it does not make a hung run recoverable: it prevents this cause of the hang, it does not detect
one. A long unattended run should still have a frame cap and an outer timeout.

## Cross-module dependencies

<<<<<<< HEAD
- `rendering` — overlays draw through `DrawComponent` and `MasterRenderSystem`; screenshots capture the backbuffer; `PointerReplaySystem` derives world coordinates through `Camera`.
||||||| 342dba6
- `rendering` — overlays draw through `DrawComponent` and `MasterRenderSystem`; screenshots capture the backbuffer.
=======
- `rendering` — overlays draw through `DrawComponent` and `MasterRenderSystem`; screenshots capture the backbuffer, or a `RenderTargetID` target resolved through `MasterRenderSystem.RenderedTargetSink`. That socket points the same way `foundation`'s profiler socket does: `rendering` owns the (null-by-default) socket and never references this module.
>>>>>>> origin/main
- `collision` — `ColliderDebugSystem` reads `BoxColliderComponent` and `ConvexColliderComponent` to know what to outline.
- `cursor` — `PointerReplaySystem` injects into `CursorInputComponent` and places the cursor through `Cursor.ApplyPose`; the screen stands the hardware path down with `CursorInputSystem.SkipHardwareRead` + `CursorPositionSystem.SkipDerivation`. This is a hard dependency *because* the channel refuses to simulate a pointer: there is no injection without the real cursor component.
- `foundation` — `SystemProfiler` plugs into `GatedSystem.TimingSink` and `PointerReplaySystem` into `Logger.LineSink` (the `waitUntil`-on-log predicate); both report through `Logger`. The arrow points this way only: `foundation` defines the sockets and never references this module.

## Extension points

- **New debug overlays.** Follow the pattern of the existing two systems: create transient `DrawComponent { Type = Mesh }` entities each frame at a high `LayerDepth` (so they render on top), dispose them at the start of the next frame, and ship a static `Enabled` flag plus an instance `IsEnabled` flag. Never call `SpriteBatch` directly.
- **HUD overlays (FPS, entity count, draw call count).** Same pattern with `DrawComponent { Type = Text }` on a HUD target. Aspirational direction list.
- **Capture-on-exit screenshot.** Mode where `ScreenshotCaptureSystem` guarantees one final PNG at game shutdown — useful for replay post-mortems. Aspirational direction list.
- **New `waitUntil` predicates.** The three shipped ones (`entity`, `log`, `frames`) are deliberately the pragmatic minimum. A new one is a nullable field on `PointerCommand` plus a branch in `IsSatisfied` — e.g. "a component of type T exists", "this entity is at this position". Keep them observable from outside the game: a predicate that reaches into a game-specific system belongs in the game, not here.

## See also

<<<<<<< HEAD
- [Premises](premises.md) — load-bearing invariants (opt-in nothing required, overlays via same `DrawComponent` path, must run after prep + before render, `ScreenshotCaptureSystem` gated by replay-file flag, `FromEnvironment` as the single owner of the capture env contract, raw capture's synchronous zero-allocation write, `MONODREAMS_DEBUG_DIR` env-var override, the profiler's injected-sink direction + its `[perf]` format contract, the pointer channel's injection-not-simulation rule and its frame-counted stage gating)
- Related modules: `rendering` (overlays ride its draw stack), `collision` (provides the collider components `ColliderDebugSystem` visualizes), `cursor` (the component the pointer channel injects into), `foundation` (provides `Logger`, the `Logger.LineSink` tap and the keyboard/input replay scaffold — the *non-visual* debug infrastructure that lives there because it's production-useful)
||||||| 342dba6
- [Premises](premises.md) — load-bearing invariants (opt-in nothing required, overlays via same `DrawComponent` path, must run after prep + before render, `ScreenshotCaptureSystem` gated by replay-file flag, `FromEnvironment` as the single owner of the capture env contract, raw capture's synchronous zero-allocation write, `MONODREAMS_DEBUG_DIR` env-var override, the profiler's injected-sink direction + its `[perf]` format contract)
- Related modules: `rendering` (overlays ride its draw stack), `collision` (provides the collider components `ColliderDebugSystem` visualizes), `foundation` (provides `Logger` and the replay scaffold — the *non-visual* debug infrastructure that lives there because it's production-useful)
=======
- [Premises](premises.md) — load-bearing invariants (opt-in nothing required, overlays via same `DrawComponent` path, must run after prep + before render, `ScreenshotCaptureSystem` gated by replay-file flag, `FromEnvironment` as the single owner of the capture env contract, target capture resolved through the render socket, raw capture's synchronous zero-allocation write, `MONODREAMS_DEBUG_DIR` env-var override, keep-awake as an opt-in macOS-only assertion, the profiler's injected-sink direction + its `[perf]` format contract)
- Related modules: `rendering` (overlays ride its draw stack), `collision` (provides the collider components `ColliderDebugSystem` visualizes), `foundation` (provides `Logger` and the replay scaffold — the *non-visual* debug infrastructure that lives there because it's production-useful)
>>>>>>> origin/main
